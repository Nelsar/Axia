using System.Reflection;
using MediatR;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The Application layer's public surface, asserted rather than left to habit.
///
/// A handler is how a use case is implemented; the contract is the request type and its response.
/// Nothing enforced that, so every handler and validator was public by default — and a public
/// handler can be constructed and called directly, which skips the whole MediatR pipeline and
/// with it validation and the permission check. These tests fail the moment that becomes possible
/// again, which is the only way the boundary stays where it was put.
/// </summary>
public class LayerBoundaryTests
{
    private static readonly Assembly ApplicationAssembly = typeof(DependencyInjection).Assembly;

    public static IEnumerable<Type> ApplicationTypes => ApplicationAssembly.GetTypes();

    [Fact]
    public void NoRequestHandler_IsReachableFromOutsideTheLayer()
    {
        var exposed = ApplicationTypes
            .Where(type => type is { IsClass: true, IsPublic: true })
            .Where(type => type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }

    [Fact]
    public void NoValidator_IsReachableFromOutsideTheLayer()
    {
        var exposed = ApplicationTypes
            .Where(type => type is { IsClass: true, IsPublic: true })
            .Where(type => typeof(IValidator).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }

    [Fact]
    public void NoPipelineBehaviour_IsReachableFromOutsideTheLayer()
    {
        var exposed = ApplicationTypes
            .Where(type => type is { IsClass: true, IsPublic: true })
            .Where(type => type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }

    [Fact]
    public void NoRequestType_IsReachableFromOutsideTheLayer()
    {
        // This assertion used to run the other way — request types had to stay public, because a
        // controller built the command and sent it itself. Once the modules took that over, a
        // public command was a second way in to every operation: undocumented, covered by no
        // boundary test, and duplicating the wire request the module already takes. So the request
        // type joined the handler and the validator as how a use case is implemented rather than
        // what it offers, and what a caller may say is now exactly IOpencoreModule and nothing else.
        //
        // The practical half: the shape of a command is no longer public API. It can be renamed,
        // split or reordered without breaking a build in another project.
        var exposed = ApplicationTypes
            .Where(type => type.IsPublic && !type.IsNested)
            .Where(type => type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }

    [Fact]
    public void EveryRequestType_IsStillFoundByTheScan()
    {
        // The assertion above passes on an empty set, and an empty set is what a rename of the
        // MediatR marker or a move out of this assembly would produce. Requests are the one thing
        // this file counts rather than merely constrains.
        var requests = ApplicationTypes
            .Where(type => type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToList();

        Assert.NotEmpty(requests);
    }

    [Fact]
    public void TheDomainKnowsNothingOfTheLayersAboveIt()
    {
        // The dependency rule, stated as a fact about the compiled assembly rather than as an
        // intention about the .csproj.
        var referenced = typeof(Provider).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name is not null && name.StartsWith("Nelsar.Axia", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(referenced);
    }

    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void TheApiTalksToTheSystemThroughContractsAlone()
    {
        // The API names Contracts and Domain, and nothing else of ours beyond the composition root.
        // Application went with the ports: ICurrentUser and IPermissionResolver are interfaces the
        // host implements or consumes, the four non-domain exceptions are statuses the host has to
        // map, and LogMask is how it renders a subject for the log — all of that is the boundary,
        // so all of it lives in Contracts now. What is left in Application is how the use cases
        // work, which no host has any business naming.
        //
        // Infrastructure stays: Program.cs is the composition root and someone has to know what
        // gets wired. That is the one place where knowing is the job.
        //
        // Asserted on the compiled assembly rather than on the .csproj, because Application and
        // MediatR both still arrive through Infrastructure transitively. A reference is only
        // emitted for an assembly whose types the code actually uses, so this is the fact that
        // matters, not what the project graph permits.
        var referenced = ApiAssembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .ToList();

        Assert.DoesNotContain("Nelsar.Axia.Application", referenced);
        Assert.DoesNotContain("MediatR", referenced);
        Assert.Contains("Nelsar.Axia.Contracts", referenced);
    }

    [Fact]
    public void NoController_NamesAnApplicationType()
    {
        // The finer-grained half of the rule above, and the half that survives it. Should host
        // plumbing ever need an Application type again — a new port that has not been moved out
        // yet — the assembly-level assertion has to be relaxed, and this is what still holds the
        // line where it matters: everything a controller says and everything it hears comes from
        // Contracts, or the module surface is a suggestion rather than the way in.
        var controllers = ApiAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .ToList();

        // Without this the assertion below passes on an empty set, which is what it would do if
        // the controllers were ever renamed out of reach of this scan.
        Assert.NotEmpty(controllers);

        var offenders = controllers
            .SelectMany(SignatureTypes, (controller, used) => (Controller: controller, Used: used))
            .Where(pair => pair.Used.Assembly == ApplicationAssembly)
            .Select(pair => $"{pair.Controller.Name} -> {pair.Used.Name}")
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>Every type a controller names in its own declarations, generic arguments
    /// unwrapped — <c>Task&lt;PagedResponse&lt;UserDto&gt;&gt;</c> counts as all three.</summary>
    private static IEnumerable<Type> SignatureTypes(Type controller)
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var fields = controller.GetFields(Declared).Select(field => field.FieldType);

        var constructors = controller.GetConstructors(Declared)
            .SelectMany(ctor => ctor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        var methods = controller.GetMethods(Declared)
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType));

        return fields.Concat(constructors).Concat(methods).SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type) =>
        type.IsGenericType
            ? type.GetGenericArguments().SelectMany(Flatten).Append(type.GetGenericTypeDefinition())
            : [type];

    [Fact]
    public void EveryModule_IsResolvableAsIOpencoreModule()
    {
        // A module the host can name but the container cannot build fails at the first request to
        // an endpoint nobody exercised. Registration lives in AddApplicationLayer, and this is what
        // catches the interface that was declared and then never wired.
        var declared = typeof(IOpencoreModule).Assembly
            .GetTypes()
            .Where(type => type.IsInterface && type != typeof(IOpencoreModule))
            .Where(type => typeof(IOpencoreModule).IsAssignableFrom(type))
            .ToList();

        Assert.NotEmpty(declared);

        var provider = new ServiceCollection()
            .AddApplicationLayer()
            .BuildServiceProvider();

        var unregistered = declared
            .Where(type => provider.GetService(type) is null)
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(unregistered);
    }

    [Fact]
    public void NoModuleImplementation_IsReachableFromOutsideTheLayer()
    {
        // Same reasoning as the handlers above: the interface in Contracts is the contract, the
        // class in Application is how it is met. A public implementation could be newed up with a
        // hand-made ISender and bypass the composition the host relies on.
        var exposed = ApplicationTypes
            .Where(type => type is { IsClass: true, IsPublic: true })
            .Where(type => typeof(IOpencoreModule).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }

    [Fact]
    public void EveryPermissionRequirement_IsDeclaredOnARequest_NotOnAHandler()
    {
        // A handler carrying the marker would be checked by nothing: AuthorizationBehaviour reads
        // it off the request, so a requirement placed anywhere else is a guard that never runs.
        var misplaced = ApplicationTypes
            .Where(type => !type.IsInterface && typeof(IRequirePermission).IsAssignableFrom(type))
            .Where(type => !type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(misplaced);
    }
}
