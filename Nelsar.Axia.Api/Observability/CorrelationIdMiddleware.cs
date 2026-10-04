using System.Diagnostics;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Nelsar.Axia.Api.Observability;

/// <summary>
/// Gives every request one id, puts it on everything logged while that request runs, and hands it
/// back to the caller.
///
/// Without it, answering "what happened to this request?" in Kibana means matching on a timestamp
/// and a path and hoping nobody else called the same endpoint that second. With it, the caller
/// quotes the id from the response header, and one LogQL or Kibana query returns the request
/// summary, the use case, the authorisation decision and the exception together, across however
/// many log lines the request produced.
///
/// The id is pushed onto Serilog's <see cref="LogContext"/>, which is ambient for the rest of the
/// request. That is what lets the Application layer stay ignorant of it: a handler logs through
/// <c>ILogger</c> as it always did and the property is attached underneath.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>Where the id is parked for anything that needs it without a logger.</summary>
    public const string ItemsKey = "CorrelationId";

    /// <summary>The property name the id appears under in the log.</summary>
    public const string PropertyName = "CorrelationId";

    /// <summary>
    /// Long enough for a GUID, a W3C trace id or an upstream gateway's own id, short enough that
    /// nobody can pay for a megabyte of it on every line of a log they do not own.
    /// </summary>
    internal const int MaxLength = 64;

    private readonly RequestDelegate _next;
    private readonly string _headerName;

    public CorrelationIdMiddleware(RequestDelegate next, IOptions<ObservabilityOptions> options)
    {
        _next = next;
        _headerName = options.Value.Correlation.HeaderName;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Sanitise(context.Request.Headers[_headerName]) ?? Generate();

        context.Items[ItemsKey] = correlationId;

        // OnStarting rather than a straight assignment: the header has to be on the response
        // whether it ends at a controller, at the rate limiter or at GlobalExceptionHandler, and
        // an error is precisely when the caller needs an id to quote.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[_headerName] = correlationId;
            return Task.CompletedTask;
        });

        // Everything logged from here down carries the id — the use case, the authorisation
        // decision, the exception, and the one request-completion event Serilog's request logging
        // writes, since that middleware runs inside this scope rather than around it.
        using (LogContext.PushProperty(PropertyName, correlationId))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Accepts a caller-supplied id only if it is short and alphanumeric-ish, and returns null
    /// otherwise so a fresh one is minted.
    ///
    /// This value is attacker-controlled and its destination is a log file, a Logstash pipeline
    /// and a response header. Unfiltered, a newline in it forges log entries in any
    /// non-JSON-formatted sink (OWASP log injection) and a caller can pin a chosen id onto
    /// somebody else's trail; unbounded, it is free storage in every system the log reaches.
    /// Kestrel refuses CR/LF in a response header on its own, but the log file is not Kestrel's
    /// to protect. Silently replacing a bad id beats refusing the request: a malformed
    /// diagnostic header is no reason to fail an otherwise valid API call.
    /// </summary>
    internal static string? Sanitise(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        var trimmed = candidate.Trim();

        if (trimmed.Length > MaxLength)
            return null;

        foreach (var character in trimmed)
        {
            var permitted = char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or ':';

            if (!permitted)
                return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Prefers the ambient W3C trace id, so that the id in the log is the same one a tracing
    /// backend already knows this request by, and the two can be lined up without a translation
    /// table. Falls back to a GUID when nothing is tracing.
    /// </summary>
    private static string Generate() =>
        Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
}
