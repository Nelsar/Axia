using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>
/// Computer account management, backed by the directory itself.
///
/// What these operations do is manage directory entries, which is not the same as managing
/// machines. Creating an account pre-stages an entry a machine can later claim when it joins the
/// domain; it does not join anything. Deleting one revokes a joined machine's trust, and that
/// machine has to rejoin before it can authenticate again. Renaming re-addresses the entry
/// without renaming the machine.
///
/// A computer is an Active Directory concept: <c>computer</c> has no RFC 4519 equivalent, and
/// entries read from a directory that only knows <c>device</c> arrive without an operating system
/// or an enabled state, which is reported as null rather than guessed at.
/// </summary>
public interface IComputersModule : IOpencoreModule
{
    Task<PagedResponse<ComputerDto>> SearchAsync(
        string? filter,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default);

    Task<ComputerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ComputerDto> CreateAsync(CreateComputerRequest request, CancellationToken cancellationToken = default);

    Task<ComputerDto?> UpdateAsync(
        Guid id,
        UpdateComputerRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
