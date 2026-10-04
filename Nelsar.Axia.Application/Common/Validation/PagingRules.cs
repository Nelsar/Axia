using FluentValidation;
using Nelsar.Axia.Application.Resources;

namespace Nelsar.Axia.Application.Common.Validation;

/// <summary>
/// The bound on a caller-supplied page size, in one place, for every request that pages.
///
/// It was missing entirely: <c>pageSize</c> arrives from the query string, is defaulted by the
/// controller and then handed to <see cref="PagedProjection"/> and to the audit repository
/// without anything in between ever looking at it. Three separate things go wrong with that.
///
/// <c>pageSize=0</c> pages forever. <c>Take(0)</c> returns nothing while <c>HasMore</c> stays
/// true and the next token is the offset the caller already had, so a client looping "until
/// HasMore is false" never stops and never sees a row — a self-inflicted denial of service that
/// takes one typo in a client to trigger.
///
/// <c>pageSize=-1</c> reaches <c>Take(-1)</c>, which throws ArgumentOutOfRangeException. That
/// leaves as a 500: an ordinary bad parameter reported as a server fault, in the log as an error
/// worth waking somebody for.
///
/// <c>pageSize=2147483647</c> is the one that costs something. It defeats paging altogether —
/// every entry the directory returned is serialised into one response — and <c>offset +
/// pageSize</c> silently overflows to a negative offset on the way to building the next token.
/// This is OWASP API4:2023, Unrestricted Resource Consumption, and the cheapest possible request
/// to send.
///
/// Enforcing it as a validator rather than as a clamp in each handler is deliberate: a clamp
/// quietly serves a different page than the one asked for, and it has to be remembered in every
/// handler that pages. A rule on the request runs in <see cref="Behaviours.ValidationBehaviour{TRequest,TResponse}"/>
/// before any handler is reached, tells the caller what was wrong, and cannot be forgotten by the
/// next paged query — provided its validator says <c>.PageSize()</c>, which is why this is one
/// call and not five copies of a number.
/// </summary>
internal static class PagingRules
{
    public const int MinimumPageSize = 1;

    /// <summary>
    /// 200, because that is the number <c>openapi.yaml</c> has been publishing as the maximum for
    /// every paged endpoint since before this rule existed. The bound was documented and simply
    /// never enforced — clients were told 200 and handed whatever they asked for — so this is the
    /// implementation catching up with its own contract rather than a new limit being imposed. If
    /// it ever moves, it moves in both places.
    /// </summary>
    public const int MaximumPageSize = 200;

    public static IRuleBuilderOptions<TRequest, int> PageSize<TRequest>(
        this IRuleBuilder<TRequest, int> rule) =>
        rule.InclusiveBetween(MinimumPageSize, MaximumPageSize)
            .WithMessage(_ => ApplicationMessages.PageSizeOutOfRange(MinimumPageSize, MaximumPageSize));
}
