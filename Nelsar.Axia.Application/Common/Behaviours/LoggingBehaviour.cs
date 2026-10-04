using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Domain.Exceptions;

namespace Nelsar.Axia.Application.Common.Behaviours;

/// <summary>
/// Logs that a use case started, that it finished, and how long it took — once, here, so no
/// handler has to carry that plumbing itself.
///
/// Only the request's type name is logged, never the request itself. Commands on this API carry
/// bind passwords and user credentials (AuthenticateCommand, CreateUserCommand,
/// CreateProviderCommand), and a behaviour that logged its input would put every one of them in
/// plaintext into the log — the one place they must never reach.
///
/// A refused request is not a system fault, so a <see cref="DomainException"/> leaves at Warning
/// with its reason. Anything else is a bug or an outage and leaves at Error with the exception,
/// which mirrors how GlobalExceptionHandler grades the very same failures at the edge.
///
/// Nothing here knows what a sink is. The scope below is opened through <c>ILogger</c>'s own
/// abstraction, and it is the host's logging provider that turns it into structured properties —
/// which is why this layer references Microsoft.Extensions.Logging and not Serilog, and why
/// swapping the provider changes nothing in this file.
/// </summary>
internal sealed class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        // Tags everything the handler and its collaborators log with the use case they were doing
        // it for. Combined with the correlation id the host puts on the request, a single query
        // returns one request's whole path: which use case ran, what it decided, and why it failed.
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["UseCase"] = name });

        // Debug rather than Information: one of the outcome lines below follows every request
        // that finishes, so on a healthy system the start line says nothing the completion line
        // does not, at twice the volume. It earns its place only when a use case never completes
        // — a directory call that hangs leaves no other trace at all — and that is the case worth
        // turning it on for:
        //
        //   "Serilog": { "MinimumLevel": { "Override": { "Nelsar.Axia.Application": "Debug" } } }
        _logger.LogDebug("{Request} started.", name);

        try
        {
            var response = await next();
            _logger.LogInformation("{Request} handled in {ElapsedMs}ms", name, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("{Request} refused after {ElapsedMs}ms: {Reason}",
                name, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Request} failed after {ElapsedMs}ms", name, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
