using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Application.Common.Behaviours;

/// <summary>
/// Enforces <see cref="IRequirePermission"/> before a handler runs.
///
/// It sits in front of validation on purpose. A caller who may not perform an operation should
/// not be told, by the shape of the 400 they get back, which fields it takes and what they must
/// contain — that is a free description of an endpoint they have no business calling. Refusing
/// first also means no work is spent on a request that was never going to run.
///
/// A request that declares no permission passes straight through. That is not a loophole to
/// leave quiet: it is why the controller attributes stay in place as the outer check, and why
/// <see cref="IRequirePermission"/> documents which use cases are expected to carry one.
/// </summary>
internal sealed class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ILogger<AuthorizationBehaviour<TRequest, TResponse>> _logger;

    public AuthorizationBehaviour(
        ICurrentUser currentUser,
        IPermissionResolver permissionResolver,
        ILogger<AuthorizationBehaviour<TRequest, TResponse>> logger)
    {
        _currentUser = currentUser;
        _permissionResolver = permissionResolver;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IRequirePermission guarded)
            return await next();

        // Work the application runs for itself carries no principal. It was authorised when a
        // person asked for it — the bulk job is checked at the point it is queued, not each time
        // the worker picks it up — so re-checking here would only ever deny it.
        if (_currentUser.IsSystem)
            return await next();

        var subject = _currentUser.Subject;
        if (string.IsNullOrWhiteSpace(subject))
        {
            _logger.LogWarning("{Request} refused: no authenticated principal.", typeof(TRequest).Name);
            throw new NotAuthenticatedException(ApplicationMessages.AuthenticationRequired());
        }

        // Throws if group membership cannot be established, and that is deliberate: "we could not
        // find out" must never be served as "holds nothing", which would silently strip an
        // administrator mid-session. See IPermissionResolver.
        var held = await _permissionResolver.ResolveAsync(subject, _currentUser.ProviderId, cancellationToken);

        if (!held.Contains(guarded.RequiredPermission))
        {
            // The permission is named here and not in the response — the log is where an operator
            // answering "why was I refused?" should have to look.
            _logger.LogWarning("{Request} refused: caller does not hold {Permission}.",
                typeof(TRequest).Name, guarded.RequiredPermission);

            throw new PermissionDeniedException(ApplicationMessages.PermissionDenied());
        }

        return await next();
    }
}
