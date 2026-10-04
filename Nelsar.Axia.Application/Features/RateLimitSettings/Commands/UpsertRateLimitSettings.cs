using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.RateLimitSettings.Commands;

/// <summary>
/// Creates the row for a policy name if missing, otherwise updates it. Takes effect within the
/// provider's cache TTL — no restart.
/// </summary>
internal sealed record UpsertRateLimitSettingsCommand(
    string PolicyName,
    int PermitLimit,
    int WindowSeconds,
    int QueueLimit,
    bool Enabled) : IRequest<RateLimitSettingsDto>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RateLimitsManage;
}

internal sealed class UpsertRateLimitSettingsCommandValidator : AbstractValidator<UpsertRateLimitSettingsCommand>
{
    public UpsertRateLimitSettingsCommandValidator()
    {
        RuleFor(c => c.PolicyName).NotEmpty();

        // Mirrors the [Range] attributes on RateLimitSettingsUpdateRequest, which only MVC model
        // binding enforces. A limit of zero would wedge the policy shut for every caller, so the
        // rule has to hold for anything that dispatches the command, not just for HTTP.
        RuleFor(c => c.PermitLimit).GreaterThanOrEqualTo(1);
        RuleFor(c => c.WindowSeconds).GreaterThanOrEqualTo(1);
        RuleFor(c => c.QueueLimit).GreaterThanOrEqualTo(0);
    }
}

internal sealed class UpsertRateLimitSettingsCommandHandler
    : IRequestHandler<UpsertRateLimitSettingsCommand, RateLimitSettingsDto>
{
    private readonly IRateLimitSettingsRepository _repo;

    public UpsertRateLimitSettingsCommandHandler(IRateLimitSettingsRepository repo) => _repo = repo;

    public async Task<RateLimitSettingsDto> Handle(
        UpsertRateLimitSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var saved = await _repo.UpsertAsync(Domain.Entities.RateLimitSettings.Create(
            request.PolicyName,
            request.PermitLimit,
            request.WindowSeconds,
            request.QueueLimit,
            request.Enabled));

        return RateLimitSettingsMapping.ToDto(saved);
    }
}
