using Microsoft.AspNetCore.Diagnostics;
using Nelsar.Axia.Api.Resources;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Exceptions;

namespace Nelsar.Axia.Api.Middleware;

// Catches anything that bubbles up past a controller action unhandled. Returns a
// generic message instead of exception.Message — the previous per-controller
// `catch (Exception ex) { return StatusCode(500, new { message = ex.Message }) }`
// pattern leaked internal details (LDAP server info, stack-trace-adjacent text) to
// API clients. The real exception is still logged server-side for diagnostics.
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // A DomainException states which HTTP status it means, and its message was written
        // for the client — so it is safe, and useful, to return verbatim. Anything else is a
        // bug or an outage: log it as an error and tell the caller nothing.
        var (statusCode, message) = exception switch
        {
            DomainValidationException => (StatusCodes.Status400BadRequest, exception.Message),
            DomainNotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            DomainConflictException => (StatusCodes.Status409Conflict, exception.Message),
            // Raised by AuthorizationBehaviour when a use case is reached without the permission
            // it requires. Normally the route attribute has already refused and this never fires;
            // when it does, it means a request arrived by a path the edge check does not cover,
            // which is exactly the case the second check exists for.
            NotAuthenticatedException => (StatusCodes.Status401Unauthorized, exception.Message),
            PermissionDeniedException => (StatusCodes.Status403Forbidden, exception.Message),
            // The directory is upstream of us, so its failure is a gateway failure. The message
            // is ours, not the LDAP library's, so it is safe to return: the original exception
            // travels as InnerException to the log and no further.
            DirectoryUnavailableException => (StatusCodes.Status502BadGateway, exception.Message),
            // The route exists and the caller used it correctly; we simply have not built what
            // sits behind it. 501 says that, where a 404 would send them hunting for a typo.
            FeatureNotImplementedException => (StatusCodes.Status501NotImplemented, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, ApiMessages.UnexpectedError())
        };

        if (exception is DirectoryUnavailableException)
        {
            // An outage, not a client mistake — this must stay at Error so it alerts.
            _logger.LogError(exception, "Directory unavailable handling {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else if (exception is FeatureNotImplementedException)
        {
            // Expected while a planned capability is outstanding — worth seeing that someone is
            // asking for it, not worth waking anybody.
            _logger.LogWarning("Not implemented: {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else if (exception is NotAuthenticatedException or PermissionDeniedException)
        {
            // A refusal, not a fault. AuthorizationBehaviour has already logged which permission
            // was missing; repeating it at Error would make routine access control look like an
            // outage.
            _logger.LogWarning("Refused {Method} {Path}: not permitted.",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else if (exception is DomainException)
        {
            // The caller sent something we refused. That is not a system fault, and logging it
            // at Error would bury real failures under routine input validation noise.
            _logger.LogWarning("Rejected {Method} {Path}: {Reason}",
                httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }
        else
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(new { message }, cancellationToken);

        return true;
    }
}
