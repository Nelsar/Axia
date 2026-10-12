using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>
/// User accounts, as the directory holds them.
///
/// A null return means "no such account" rather than a failure — the caller turns that into
/// whatever its transport calls absent. Everything else that can go wrong (a malformed value,
/// a name already taken, a directory that will not answer) is raised, not returned, and the
/// host's error handling is what decides how it reaches the client.
/// </summary>
public interface IUsersModule : IOpencoreModule
{
    Task<UserListingResponse> GetAllAsync(CancellationToken cancellationToken = default);

    Task<PagedResponse<UserDto>> SearchAsync(
        string? filter,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default);

    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every attribute the account's directory entry carries, under the directory's own names —
    /// a separate read from <see cref="GetByIdAsync"/>, which returns only the fields the
    /// application itself uses. Credential attributes are listed by name with their values
    /// withheld.
    /// </summary>
    Task<UserAttributesDto?> GetAttributesAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UserDto> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default);

    Task<UserDto?> UpdateAsync(Guid id, UserUpdateRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Queues a batch for background processing and answers with the job id to poll.</summary>
    Task<string> EnqueueBulkAsync(
        IEnumerable<BulkUserRequest>? requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a batch inline and reports a per-item result, since a batch can be a mix of
    /// per-object successes and failures.
    /// </summary>
    Task<BulkOperationResponse<BulkUserResult>> ProcessBulkAsync(
        IEnumerable<BulkUserRequest>? requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a queued batch: the same as <see cref="ProcessBulkAsync"/>, for the background worker,
    /// which may run batches larger than a caller waiting on the answer may send.
    /// </summary>
    Task<BulkOperationResponse<BulkUserResult>> ProcessQueuedBulkAsync(
        IEnumerable<BulkUserRequest>? requests,
        CancellationToken cancellationToken = default);

    /// <summary>Null when there is no such job, or it is not the caller's to see.</summary>
    Task<BulkOperationResponse<BulkUserResult>?> GetBulkJobStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    Task<PatternGeneratorResponse> GeneratePatternsAsync(
        PatternGeneratorRequest? request,
        CancellationToken cancellationToken = default);
}
