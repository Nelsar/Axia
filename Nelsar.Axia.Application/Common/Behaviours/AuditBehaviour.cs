using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;

namespace Nelsar.Axia.Application.Common.Behaviours;

/// <summary>
/// Writes one audit entry for every <see cref="IAuditedCommand"/> dispatched, whatever its outcome.
///
/// It sits outside authorisation and validation on purpose. An attempt that was refused is
/// exactly what the trail is for: someone trying to put themselves in a privileged group, or to
/// reset an administrator's password, should show up whether or not they were allowed to.
///
/// The actor is the principal the command runs as — the signed-in caller, or the person who
/// queued a job (see <see cref="ActingUser"/>) — not anything the command carries, so a request
/// cannot name someone else as the one responsible for it.
/// </summary>
internal sealed class AuditBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IAuditService _audit;
    private readonly ActingUser _actingUser;

    public AuditBehaviour(IAuditService audit, ActingUser actingUser)
    {
        _audit = audit;
        _actingUser = actingUser;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IAuditedCommand audited)
            return await next();

        TResponse response;
        try
        {
            response = await next();
        }
        catch (Exception ex)
        {
            // The exception's type says what kind of refusal or failure it was; its message is
            // the one the caller was shown, so it holds nothing they did not already see.
            await RecordAsync(audited, success: false, $"error={ex.GetType().Name}: {ex.Message}");
            throw;
        }

        // A null or false answer is how these use cases say "no such object" — nothing changed,
        // and the trail should not read as if it had.
        var changed = response is not null && response is not false;
        await RecordAsync(audited, changed, changed ? null : "not found");

        return response;
    }

    private Task RecordAsync(IAuditedCommand audited, bool success, string? outcome)
    {
        var target = audited.AuditTarget;
        var details = new List<string> { $"target={target}", AuditDetails.Directory(_actingUser.ProviderId) };
        if (audited.AuditDescription is { Length: > 0 } description)
            details.Add(description);
        if (outcome is not null)
            details.Add(outcome);

        return _audit.LogModificationAsync(
            Guid.TryParse(target, out var objectId) ? objectId : null,
            objectDn: null,
            actor: _actingUser.Subject ?? string.Empty,
            action: ActionName(audited),
            success,
            string.Join("; ", details));
    }

    /// <summary>"DeleteUserCommand" is recorded as "DeleteUser".</summary>
    private static string ActionName(IAuditedCommand audited)
    {
        var name = audited.GetType().Name;
        return name.EndsWith("Command", StringComparison.Ordinal) ? name[..^"Command".Length] : name;
    }
}
