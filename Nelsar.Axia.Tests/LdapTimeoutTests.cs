using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Infrastructure.Identity;

namespace Nelsar.Axia.Tests;

/// <summary>
/// A directory that accepts the connection and then never says anything — a hung server, or a
/// firewall that lets the handshake through and drops what follows.
///
/// Directory calls run on request threads, and none of them had a limit: each request that reached
/// such a directory held its thread for as long as the operating system's TCP timeouts allowed,
/// and enough of them left the API unable to answer anyone. These pin that every way of opening a
/// connection now gives up.
/// </summary>
public sealed class LdapTimeoutTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> _held = [];
    private readonly CancellationTokenSource _stop = new();

    public LdapTimeoutTests()
    {
        _listener.Start();
        // Accepts every connection and keeps it open, reading nothing and writing nothing.
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    lock (_held)
                        _held.Add(client);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private LdapConnectionFactory Factory(LdapSecurity security) =>
        new(Options.Create(new LdapSettings
        {
            Server = "127.0.0.1",
            Port = Port,
            Security = security,
            AdminUsername = "cn=admin,dc=example,dc=org",
            AdminPassword = "not-a-real-password",
            ConnectTimeoutSeconds = 1,
            OperationTimeoutSeconds = 1
        }), NullLogger<LdapConnectionFactory>.Instance);

    [Theory]
    [InlineData(LdapSecurity.None)]     // connects, then the bind waits for an answer
    [InlineData(LdapSecurity.StartTls)] // connects, then StartTLS waits for an answer
    [InlineData(LdapSecurity.Ldaps)]    // connects, then the TLS handshake waits for the server's hello
    public async Task ASilentDirectory_IsGivenUpOn(LdapSecurity security)
    {
        var factory = Factory(security);
        var elapsed = Stopwatch.StartNew();

        // On another thread, so that a regression fails here instead of hanging the test run.
        var connecting = Task.Run(() => factory.CreateAdminConnection().Dispose());
        var first = await Task.WhenAny(connecting, Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.True(first == connecting, $"{security}: still waiting after 20 seconds.");
        await Assert.ThrowsAnyAsync<Exception>(() => connecting);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"{security}: gave up only after {elapsed.Elapsed}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void ATimeoutOutsideItsBounds_StopsTheHostAtStartup(int seconds)
    {
        // Zero would read as "no limit" to the LDAP library, which is what these exist to prevent.
        var problem = LdapSettingsRules.Problem(new LdapSettings { OperationTimeoutSeconds = seconds }, "Ldap");

        Assert.NotNull(problem);
        Assert.Contains("Ldap:OperationTimeoutSeconds", problem);
    }

    [Fact]
    public void AFailoverHost_KeepsTheTimeouts()
    {
        var settings = new LdapSettings { ConnectTimeoutSeconds = 3, OperationTimeoutSeconds = 7 };

        var failover = settings.ForEndpoint("dc02.example.org", 636);

        Assert.Equal(3, failover.ConnectTimeoutSeconds);
        Assert.Equal(7, failover.OperationTimeoutSeconds);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        lock (_held)
            _held.ForEach(client => client.Dispose());
        _stop.Dispose();
    }
}
