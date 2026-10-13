using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc.Controllers;
using Nelsar.Axia.Contracts.Modules;

namespace Nelsar.Axia.Api.Authorization;

/// <summary>
/// Puts an attempt to change something that an endpoint's permission check refused in the audit
/// trail, then answers as the framework would.
///
/// The trail promises every attempt to change a directory or this application's settings,
/// permitted or refused. AuditBehaviour keeps that promise for everything that reaches a use
/// case, but <c>[Authorize(Policy)]</c> refuses before one is reached: a signed-in account
/// without the permission at all got its 403 and left no trace — the refusal the trail exists
/// to show. Reads are left out, as they are everywhere else in the trail; so is a caller who is
/// not signed in, who has no principal to record and is answered 401.
/// </summary>
internal sealed class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && IsChange(context.Request.Method))
        {
            var missing = authorizeResult.AuthorizationFailure?.FailedRequirements
                .OfType<PermissionRequirement>()
                .Select(requirement => requirement.Permission)
                .ToList() ?? [];

            // Only a permission refusal is this handler's to record: what was attempted, and
            // what the caller lacked for it.
            if (missing.Count > 0)
            {
                var audit = context.RequestServices.GetRequiredService<IAuditModule>();
                await audit.RecordRefusedAsync(
                    ActionName(context),
                    $"{context.Request.Method} {context.Request.Path}",
                    missing,
                    context.RequestAborted);
            }
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private static bool IsChange(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    /// <summary>"Users.Create" for UsersController.Create: the action names repeat across controllers.</summary>
    private static string ActionName(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return action is null ? "Request" : $"{action.ControllerName}.{action.ActionName}";
    }
}
