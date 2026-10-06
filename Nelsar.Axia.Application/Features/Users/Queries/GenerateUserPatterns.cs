using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Contracts.Diagnostics;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Services;

namespace Nelsar.Axia.Application.Features.Users.Queries;

/// <summary>
/// Previews the logins a pattern would produce, optionally flagging which already exist.
///
/// A query despite generating: nothing is written, and <c>CheckConflicts</c> is the whole point
/// of running it — an operator looks at the preview before committing to a bulk create.
/// </summary>
internal sealed record GenerateUserPatternsQuery(
    string Pattern,
    int MaxItems,
    string? RawText,
    string? RegexPattern,
    string? UsernameTemplate,
    bool Transliterate,
    bool CheckConflicts) : IRequest<PatternGeneratorResponse>, IRequirePermission
{
    /// <summary>
    /// The most logins one preview may produce.
    ///
    /// <c>MaxItems</c> is the only thing bounding the generator: a mask expands lazily through
    /// <c>Enumerable.Range</c> and is cut by <c>Take(MaxItems)</c> at the end, so
    /// <c>{ Pattern = "user{1..2000000000}", MaxItems = int.MaxValue }</c> is a two-line request
    /// that asks the server to materialise two billion strings. Ten thousand is well past any
    /// real preview — an operator reads these before committing to a bulk create — and small
    /// enough that the worst case is a large response rather than an exhausted process.
    /// </summary>
    public const int MaximumItems = 10_000;

    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersGenerate;
}

internal sealed class GenerateUserPatternsQueryValidator : AbstractValidator<GenerateUserPatternsQuery>
{
    public GenerateUserPatternsQueryValidator()
    {
        // Was a guard in UsersController.Generate, which demanded Pattern unconditionally. That
        // was stricter than the feature: UserPatternGenerator generates from a mask *or* from
        // RawText + RegexPattern, and PatternGeneratorRequest documents Pattern as the input
        // "used when RawText/RegexPattern are not supplied". The controller check made the
        // raw-text mode unreachable over HTTP — implemented, documented, and rejected at the
        // door. Scoping the rule to the case it was meant for is what makes the two agree.
        RuleFor(q => q.Pattern)
            .NotEmpty()
            .When(q => string.IsNullOrWhiteSpace(q.RawText))
            .WithMessage(_ => ApplicationMessages.PatternRequired());

        // See GenerateUserPatternsQuery.MaximumItems for why an upper bound matters here. The
        // lower one is the same reasoning as a page size of zero: a request that can only ever
        // return nothing is a mistake worth naming rather than an empty list worth serving.
        RuleFor(q => q.MaxItems)
            .InclusiveBetween(1, GenerateUserPatternsQuery.MaximumItems)
            .WithMessage(_ => ApplicationMessages.GeneratedItemLimitOutOfRange(
                1, GenerateUserPatternsQuery.MaximumItems));
    }
}

internal sealed class GenerateUserPatternsQueryHandler
    : IRequestHandler<GenerateUserPatternsQuery, PatternGeneratorResponse>
{
    private readonly ILdapService _ldapService;
    private readonly ILogger<GenerateUserPatternsQueryHandler> _logger;

    public GenerateUserPatternsQueryHandler(
        ILdapService ldapService,
        ILogger<GenerateUserPatternsQueryHandler> logger)
    {
        _ldapService = ldapService;
        _logger = logger;
    }

    public async Task<PatternGeneratorResponse> Handle(
        GenerateUserPatternsQuery request,
        CancellationToken cancellationToken)
    {
        var generated = UserPatternGenerator.Generate(new UserPatternSpec
        {
            Pattern = request.Pattern,
            RawText = request.RawText,
            RegexPattern = request.RegexPattern,
            UsernameTemplate = request.UsernameTemplate,
            Transliterate = request.Transliterate,
            MaxItems = request.MaxItems
        });

        var response = new PatternGeneratorResponse
        {
            Items = generated,
            Count = generated.Count
        };

        if (request.CheckConflicts)
        {
            var details = new List<PatternGeneratorItem>(generated.Count);
            foreach (var item in generated)
            {
                var exists = false;
                try
                {
                    exists = await _ldapService.GetUserDetailsAsync(item) != null;
                }
                catch (DirectoryUnavailableException)
                {
                    // Deliberately not swallowed. This is the preview an operator relies on
                    // before bulk-creating: answering "no conflicts" because the directory was
                    // unreachable is the one wrong answer, since it invites them to create
                    // duplicates of accounts we simply failed to look at.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Conflict check failed for generated login {Login}.", LogMask.Login(item));
                }
                details.Add(new PatternGeneratorItem { Value = item, Exists = exists });
            }
            response.Details = details;
        }

        return response;
    }
}
