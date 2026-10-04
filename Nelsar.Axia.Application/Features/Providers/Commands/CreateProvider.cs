using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Providers.Commands;

internal sealed record CreateProviderCommand(
    string Name,
    string Host,
    int Port,
    string ProviderType,
    bool IsActive,
    string? FailoverHosts,
    string BaseDn,
    string AdminUsername,
    string? AdminPassword,
    string UserSearchFilter,
    int UserSearchPageSize) : IRequest<ProviderDto>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ProvidersManage;
}

internal sealed class CreateProviderCommandValidator : AbstractValidator<CreateProviderCommand>
{
    public CreateProviderCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
        RuleFor(c => c.Host).NotEmpty();
        RuleFor(c => c.ProviderType).NotEmpty();
        // The port's range is LdapPort's rule, not restated here — the handler builds one and
        // it raises a domain validation failure that the pipeline reports the same way.
    }
}

internal sealed class CreateProviderCommandHandler : IRequestHandler<CreateProviderCommand, ProviderDto>
{
    private readonly IProviderRepository _repo;
    private readonly IEncryptionService _encryptionService;

    public CreateProviderCommandHandler(IProviderRepository repo, IEncryptionService encryptionService)
    {
        _repo = repo;
        _encryptionService = encryptionService;
    }

    public async Task<ProviderDto> Handle(CreateProviderCommand request, CancellationToken cancellationToken)
    {
        // Every rule about a provider is inside Create, so this handler cannot build a half-valid
        // one however it is called. The password is encrypted before the domain ever sees it:
        // EncryptedSecret's type says it holds ciphertext, and there is no path that puts a
        // plaintext password on the aggregate.
        var entity = Provider.Create(
            request.Name,
            request.Host,
            LdapPort.Create(request.Port),
            request.ProviderType,
            request.IsActive,
            request.FailoverHosts,
            request.BaseDn,
            request.AdminUsername,
            string.IsNullOrEmpty(request.AdminPassword)
                ? EncryptedSecret.None
                : EncryptedSecret.FromCipherText(_encryptionService.Encrypt(request.AdminPassword)),
            LdapSearchFilter.Create(request.UserSearchFilter),
            request.UserSearchPageSize);

        var created = await _repo.AddAsync(entity);
        return ProviderMapping.ToDto(created);
    }
}
