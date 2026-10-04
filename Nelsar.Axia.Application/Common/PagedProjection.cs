using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Application.Common;

/// <summary>
/// Cuts an already-ordered, already-filtered list into the page a caller asked for.
///
/// The four directory searches did this identically and separately. It is not incidental
/// duplication: the offset, the next token and <c>HasMore</c> have to agree with each other, and
/// four copies of that arithmetic are four chances for one of them to drift — the kind of bug
/// that shows up as an entry appearing on two pages, or on none.
/// </summary>
internal static class PagedProjection
{
    public static PagedResponse<T> Page<T>(
        IReadOnlyList<T> all, int pageSize, string? pageToken, bool isComplete)
    {
        var offset = OffsetPageToken.Parse(pageToken);
        var nextOffset = offset + pageSize;
        var hasMore = all.Count > nextOffset;

        return new PagedResponse<T>
        {
            Items = all.Skip(offset).Take(pageSize).ToList(),
            PageSize = pageSize,
            PageToken = hasMore ? OffsetPageToken.Build(nextOffset) : null,
            HasMore = hasMore,
            // Paging happens over whatever the directory managed to return, so an incomplete
            // enumeration makes every page count provisional. Pass that on rather than
            // presenting a partial set as the answer.
            IsComplete = isComplete
        };
    }
}
