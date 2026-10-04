namespace Nelsar.Axia.Contracts.Responses;

public class PagedResponse<T>
{
    public IEnumerable<T> Items { get; set; } = Array.Empty<T>();
    public int PageSize { get; set; }
    public string? PageToken { get; set; }
    public bool HasMore { get; set; }

    /// <summary>
    /// False when the underlying directory enumeration was cut short. Paging over a truncated
    /// set yields page counts that do not reflect the directory.
    /// </summary>
    public bool IsComplete { get; set; } = true;
}
