using MediatR;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Application.Modules;

/// <summary>
/// The users module: one place where a wire request becomes the command or query that carries it
/// inward, and the only thing outside this assembly ever needed to reach a user use case.
///
/// The translation used to live in the API layer, which meant the API had to name Application's
/// command types to perform it — so every host that wanted to call a use case had to reference
/// this assembly and know that MediatR was what dispatched it. Here it costs the same line and
/// leaks neither.
///
/// Nothing is decided in this class. It maps, it dispatches, it returns. Validation, the
/// permission check and the logging all happen behind <see cref="ISender"/>, in the pipeline
/// configured by <see cref="DependencyInjection.AddApplicationLayer"/>, exactly as they did when
/// the controller sent the command itself.
/// </summary>
internal sealed class UsersModule : IUsersModule
{
    private readonly ISender _sender;

    public UsersModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<UserListingResponse> GetAllAsync(CancellationToken cancellationToken = default) =>
        _sender.Send(new GetAllUsersQuery(), cancellationToken);

    public Task<PagedResponse<UserDto>> SearchAsync(
        string? filter,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new SearchUsersQuery(filter, pageSize, pageToken), cancellationToken);

    public Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new GetUserByIdQuery(id), cancellationToken);

    public Task<UserAttributesDto?> GetAttributesAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new GetUserAttributesQuery(id), cancellationToken);

    public Task<UserDto> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default) =>
        _sender.Send(new CreateUserCommand(request.Email, request.Name, request.Password), cancellationToken);

    public Task<UserDto?> UpdateAsync(
        Guid id,
        UserUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new UpdateUserCommand(id, request.Name, request.Email, request.Password), cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new DeleteUserCommand(id), cancellationToken);

    /// <summary>An empty batch is refused by EnqueueBulkUserJobCommandValidator, not here.</summary>
    public Task<string> EnqueueBulkAsync(
        IEnumerable<BulkUserRequest>? requests,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new EnqueueBulkUserJobCommand(AsList(requests)), cancellationToken);

    public Task<BulkOperationResponse<BulkUserResult>> ProcessBulkAsync(
        IEnumerable<BulkUserRequest>? requests,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new ProcessBulkUsersCommand(AsList(requests)), cancellationToken);

    public Task<BulkOperationResponse<BulkUserResult>> ProcessQueuedBulkAsync(
        IEnumerable<BulkUserRequest>? requests,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new ProcessBulkUsersCommand(AsList(requests), Queued: true), cancellationToken);

    public Task<BulkOperationResponse<BulkUserResult>?> GetBulkJobStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new GetBulkJobStatusQuery(jobId), cancellationToken);

    /// <summary>A null body binds as null rather than as an empty request; the validator needs an
    /// object to find blank.</summary>
    public Task<PatternGeneratorResponse> GeneratePatternsAsync(
        PatternGeneratorRequest? request,
        CancellationToken cancellationToken = default)
    {
        request ??= new PatternGeneratorRequest();

        return _sender.Send(
            new GenerateUserPatternsQuery(
                request.Pattern,
                request.MaxItems,
                request.RawText,
                request.RegexPattern,
                request.UsernameTemplate,
                request.Transliterate,
                request.CheckConflicts),
            cancellationToken);
    }

    /// <summary>A null body binds as null, not as an empty sequence; the validator needs a
    /// collection to find empty.</summary>
    private static IReadOnlyList<BulkUserRequest> AsList(IEnumerable<BulkUserRequest>? requests) =>
        requests?.ToList() ?? [];
}
