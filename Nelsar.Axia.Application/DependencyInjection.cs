using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Common.Behaviours;
using Nelsar.Axia.Application.EventHandlers;
using Nelsar.Axia.Application.Features.Computers;
using Nelsar.Axia.Application.Features.Groups;
using Nelsar.Axia.Application.Features.OrganizationalUnits;
using Nelsar.Axia.Application.Features.Users;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Modules;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything this layer is made of: its handlers and validators, the collaborators
    /// they lean on, its subscribers to domain events, and the pipeline they all run behind.
    ///
    /// Handlers and validators are discovered by assembly scan rather than listed: a new command
    /// is a new file and nothing else, and the alternative — a registration line per handler —
    /// is a list that silently goes stale, since a missing entry fails at runtime on one endpoint
    /// rather than at build time.
    ///
    /// The named registrations below used to live in Infrastructure's AddApplicationServices.
    /// That had Infrastructure naming this layer's own collaborators — UserDirectory,
    /// PermissionResolver, the audit subscriber — which is knowledge it has no use for, and which
    /// forced every one of them to be public purely so a foreign assembly could utter the type
    /// name. Composing the layer here is what lets them be internal, and it means a host adds
    /// this layer with one call rather than by knowing what is inside it.
    ///
    /// Behaviour order is registration order, and it is deliberate. Logging is outermost so that
    /// a request refused by anything downstream is still recorded with its duration. Auditing is
    /// next, outside authorisation, so that a refused attempt at a change reaches the audit trail
    /// as well as a permitted one. Authorisation comes next, ahead of validation, so that a caller who may not perform an operation is not
    /// handed a description of the payload it takes. Validation sits directly in front of the
    /// handler so no handler ever runs on input it would have rejected.
    /// </summary>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // PermissionResolver caches a resolved permission set per subject and provider; the
        // registration is idempotent, so a host that also asks for it loses nothing.
        services.AddMemoryCache();

        // TryAdd: a host that supplies a real principal wins, whichever order it registers in.
        // The default reports nobody rather than a system context, so a host that forgets to
        // supply one refuses every guarded use case instead of running them all unchecked.
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();

        // Who the use case runs as when there is no request behind it — a queued job runs as
        // the person who queued it — and the one answer to "who is acting" that authorisation
        // and the audit trail both read.
        services.AddScoped<BackgroundPrincipal>();
        services.AddScoped<ActingUser>();

        // The second half of authorisation: AuthorizationBehaviour asks whether the caller may
        // perform the operation at all, this asks whether they may perform it on that target.
        services.AddScoped<PrivilegeGuard>();

        // This layer's own collaborators. Ports they depend on (ILdapService, the repositories)
        // are still Infrastructure's to supply.
        services.AddScoped<UserDirectory>();
        services.AddScoped<GroupDirectory>();
        services.AddScoped<OrganizationalUnitDirectory>();
        services.AddScoped<ComputerDirectory>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IRoleMappingResolver, RoleMappingResolver>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();

        // The layer's own front door: the IOpencoreModule implementations a host talks to. They are
        // the only public thing this assembly offers a caller, and they are what let a host stay on
        // Contracts alone — no Application reference, no MediatR reference, no command types in a
        // controller. Each one is a thin translation from a wire request to a command or query and
        // a Send, so the pipeline registered below still runs for every call.
        services.AddScoped<IUsersModule, UsersModule>();
        services.AddScoped<IAuthModule, AuthModule>();
        services.AddScoped<IGroupsModule, GroupsModule>();
        services.AddScoped<IOrganizationalUnitsModule, OrganizationalUnitsModule>();
        services.AddScoped<IComputersModule, ComputersModule>();
        services.AddScoped<IProvidersModule, ProvidersModule>();
        services.AddScoped<IRoleMappingsModule, RoleMappingsModule>();
        services.AddScoped<IRolesModule, RolesModule>();
        services.AddScoped<IRateLimitSettingsModule, RateLimitSettingsModule>();
        services.AddScoped<IAuditModule, AuditModule>();

        // Subscribers to domain events. Publishing them is Infrastructure's job; deciding what
        // this layer does when one is raised is not. One handler instance per scope, shared by
        // both subscriptions. Sign-ins are the only thing recorded this way; every change is
        // recorded by AuditBehaviour instead, with the person who made it as the actor.
        services.AddScoped<AuditTrailEventHandler>();
        services.AddScoped<IDomainEventHandler<UserAuthenticationSucceeded>>(sp => sp.GetRequiredService<AuditTrailEventHandler>());
        services.AddScoped<IDomainEventHandler<UserAuthenticationFailed>>(sp => sp.GetRequiredService<AuditTrailEventHandler>());

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));

        return services;
    }
}
