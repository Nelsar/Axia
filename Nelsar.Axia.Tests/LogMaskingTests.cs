using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Identity.Providers;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The places that still wrote a login to the log in full after everything else had been moved
/// to <see cref="Nelsar.Axia.Contracts.Diagnostics.LogMask"/>: a failed bulk row, a failed
/// conflict check in the name generator, and every line the Active Directory provider wrote.
/// Whatever the log reaches — Logstash, Loki — the login reached with it.
/// </summary>
public class LogMaskingTests
{
    private const string Login = "ivan.petrov@example.com";

    [Fact]
    public async Task AFailedBulkRow_NamesItsAccountMasked()
    {
        var (sender, log, ldap) = Pipeline();
        ldap.Setup(x => x.GetUserDetailsAsync(It.IsAny<string>())).ReturnsAsync((LdapUserInfo?)null);
        ldap.Setup(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("the directory said no"));

        await sender.Send(new ProcessBulkUsersCommand(
            [new BulkUserRequest { Operation = "create", Email = Login, Name = "Ivan Petrov", Password = "Pa55-word!" }]));

        var entry = Assert.Single(log.Entries, e => e.Message.StartsWith("Bulk user operation failed", StringComparison.Ordinal));
        Assert.Equal("i***@example.com", entry.Properties["Login"]);
        log.AssertNothingContains("ivan.petrov");
    }

    [Fact]
    public async Task AFailedConflictCheck_NamesTheGeneratedLoginMasked()
    {
        var (sender, log, ldap) = Pipeline();
        ldap.Setup(x => x.GetUserDetailsAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("the lookup broke"));

        await sender.Send(new GenerateUserPatternsQuery("ivan.petrov{1..1}", 1, null, null, null, false, CheckConflicts: true));

        var entry = Assert.Single(log.Entries, e => e.Message.StartsWith("Conflict check failed", StringComparison.Ordinal));
        Assert.Equal("i***", entry.Properties["Login"]);
        log.AssertNothingContains("ivan.petrov");
    }

    [Fact]
    public void TheActiveDirectoryProvider_NamesTheAccountMasked_AndKeepsTheException()
    {
        // Nothing listens on port 389 here, so the provider cannot ask — and off Windows it
        // cannot even start to. Both arrive in the same catch, which is the line under test.
        var log = new CapturingLoggerProvider();
#pragma warning disable CA1416 // Off Windows the provider throws, and that throw is what is logged.
        var provider = new ActiveDirectoryProvider(
            Options.Create(new LdapSettings
            {
                Server = "127.0.0.1",
                BaseDn = "dc=example,dc=com",
                AdminUsername = "svc-bind",
                AdminPassword = "not-a-real-password"
            }),
            log.CreateLogger<ActiveDirectoryProvider>());

        Assert.Throws<DirectoryUnavailableException>(() => provider.Authenticate(Login, "Pa55-word!"));
#pragma warning restore CA1416

        var entry = Assert.Single(log.Entries);
        Assert.Equal("i***@example.com", entry.Properties["Login"]);
        // It used to keep only ex.Message, inside an interpolated string: no stack trace, and no
        // property a Kibana or LogQL query could match on.
        Assert.NotNull(entry.Exception);
        log.AssertNothingContains("ivan.petrov");
    }

    private static (ISender Sender, CapturingLoggerProvider Log, Mock<ILdapService> Ldap) Pipeline()
    {
        var log = new CapturingLoggerProvider();
        var ldap = new Mock<ILdapService>();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(log).SetMinimumLevel(LogLevel.Trace));
        services.AddApplicationLayer();
        services.AddSingleton(ldap.Object);
        services.AddSingleton(new Mock<Nelsar.Axia.Domain.Repositories.IUserLookupRepository>().Object);
        services.AddSingleton(new Mock<IDomainEventPublisher>().Object);
        services.AddSingleton(new Mock<IBulkJobQueue>().Object);
        services.AddSingleton(new Mock<IBulkJobStore>().Object);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser("admin@example.com"));
        services.AddSingleton<IPermissionResolver>(new FixedPermissionResolver([.. Permissions.All]));
        services.WithAuditTrail();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), log, ldap);
    }

    /// <summary>One log call: the rendered line, its named properties and its exception.</summary>
    private sealed record Entry(string Message, IReadOnlyDictionary<string, string?> Properties, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Logger(_entries);

        public ILogger<T> CreateLogger<T>() => new Logger<T>(new SingleProviderFactory(this));

        /// <summary>
        /// Checks the rendered line and every property. The exception is left out on purpose:
        /// these tests supply their own, and whether exception messages may name an account is
        /// a question for the exceptions, not for these log lines.
        /// </summary>
        public void AssertNothingContains(string text)
        {
            Assert.All(Entries, entry =>
            {
                Assert.DoesNotContain(text, entry.Message, StringComparison.OrdinalIgnoreCase);
                Assert.All(entry.Properties.Values, value =>
                    Assert.DoesNotContain(text, value ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            });
        }

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<Entry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString())
                    : new Dictionary<string, string?>();

                entries.Enqueue(new Entry(formatter(state, exception), properties, exception));
            }
        }

        private sealed class SingleProviderFactory(ILoggerProvider provider) : ILoggerFactory
        {
            public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
