using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Contracts.Responses;

public class PatternGeneratorResponse
{
    public IEnumerable<string> Items { get; set; } = Array.Empty<string>();
    public int Count { get; set; }

    /// <summary>
    /// Populated only when the request set CheckConflicts=true: one entry per generated
    /// item, flagging whether it already exists in LDAP.
    /// </summary>
    public IEnumerable<PatternGeneratorItem>? Details { get; set; }
}
