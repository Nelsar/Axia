using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Providers.Commands;

/// <summary>Null when there is no such provider.</summary>
internal sealed record UpdateProviderCommand(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string? Security,
    string ProviderType,
    bool IsActive,
    string? FailoverHosts,
    string BaseDn,
    string AdminUsername,
    string? AdminPassword,
    string UserSearchFilter,
    int UserSearchPageSize) : IRequest<ProviderDto?>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ProvidersManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();

    /// <inheritdoc />
    public string? AuditDescription => $"host={Host}:{Port}"
        + (Security is null ? string.Empty : $", security={Security}")
        + (string.IsNullOrEmpty(AdminPassword) ? string.Empty : ", bind password changed");
}

internal sealed class UpdateProviderCommandValidator : AbstractValidator<UpdateProviderCommand>
{
    public UpdateProviderCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
        RuleFor(c => c.Host).NotEmpty();
        RuleFor(c => c.ProviderType).NotEmpty();
        RuleFor(c => c.Security).MustBeASecurityMode();
    }
}

internal sealed class UpdateProviderCommandHandler : IRequestHandler<UpdateProviderCommand, ProviderDto?>
{
    private readonly IProviderRepository _repo;
    private readonly IEncryptionService _encryptionService;

    public UpdateProviderCommandHandler(IProviderRepository repo, IEncryptionService encryptionService)
    {
        _repo = repo;
        _encryptionService = encryptionService;
    }

    public async Task<ProviderDto?> Handle(UpdateProviderCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repo.GetByIdAsync(request.Id);
        if (existing == null)
            return null;

        // The aggregate is changed in place through its own operations rather than rebuilt as a
        // detached copy for the repository to assign across. That copy was where a rule could be
        // enforced on the way in and lost on the way out, and it is also why keeping the existing
        // password took a ternary: the copy started blank, so the old value had to be carried
        // over explicitly or it was erased. Here, not rotating the credential simply leaves it.
        existing.Rename(request.Name);
        existing.PointAt(
            request.Host,
            LdapPort.Create(request.Port),
            ProviderSecurity.Parse(request.Security, whenOmitted: existing.Security),
            request.FailoverHosts);
        existing.ChangeType(request.ProviderType);
        existing.UseSearchRoot(request.BaseDn);
        existing.UseAdminAccount(request.AdminUsername);
        existing.ConfigureUserSearch(LdapSearchFilter.Create(request.UserSearchFilter), request.UserSearchPageSize);

        if (!string.IsNullOrEmpty(request.AdminPassword))
            existing.RotateAdminPassword(EncryptedSecret.FromCipherText(_encryptionService.Encrypt(request.AdminPassword)));

        if (request.IsActive)
            existing.Activate();
        else
            existing.Deactivate();

        var updated = await _repo.UpdateAsync(existing);
        return updated == null ? null : ProviderMapping.ToDto(updated);
    }
}
