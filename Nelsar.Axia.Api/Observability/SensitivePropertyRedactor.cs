using Serilog.Core;
using Serilog.Events;

namespace Nelsar.Axia.Api.Observability;

/// <summary>
/// Replaces the value of any log property whose name reads like a secret with <c>***</c>, on the
/// way out to every sink.
///
/// Nothing in this codebase logs a password today — <c>LoggingBehaviour</c> records only a request's
/// type name for exactly that reason, and <see cref="Nelsar.Axia.Contracts.Diagnostics.LogMask"/>
/// exists so that logins and DNs arrive already elided. This is the layer under those, and it is here because of
/// what a log sink is: once an event has reached Logstash or Loki it is in a system with its own
/// retention, its own access list and its own backups, and there is no recalling it. A single
/// <c>_logger.LogDebug("binding as {User}/{Password}", ...)</c> added during an outage at 3am is
/// all it takes, and the person who adds it will not be thinking about GDPR or about who can read
/// Kibana.
///
/// Matching is by property name, substring, case-insensitive. That deliberately over-matches: a
/// property innocently called <c>Key</c> loses its value in the log, which costs one diagnostic
/// line, where under-matching costs a credential. Structured values are walked too, so
/// <c>{@Provider}</c> cannot smuggle a bind password out inside an object.
///
/// It is not a substitute for not logging the secret in the first place. It cannot see a secret
/// that was interpolated into the message template itself ($"...{password}") — there is no
/// property left to redact by then, which is one more reason message templates are used
/// throughout rather than interpolated strings.
/// </summary>
public sealed class SensitivePropertyRedactor : ILogEventEnricher
{
    public const string Redacted = "***";

    /// <summary>How far into a destructured object to keep looking. Deep enough for real payloads,
    /// bounded so a self-referential graph cannot spin here.</summary>
    private const int MaxDepth = 6;

    private static readonly string[] SensitiveNameFragments =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "credential",
        "apikey",
        "api_key",
        "authorization",
        "auth_header",
        "signingkey",
        "privatekey",
        "connectionstring",
        // Bare "key" is last and broadest: Jwt:Key and Encryption:Key are both signing material,
        // and neither is worth a chance of leaking to keep a property called "Key" readable.
        "key"
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Snapshot the names first: AddOrUpdateProperty mutates the dictionary being read.
        foreach (var name in logEvent.Properties.Keys.ToArray())
        {
            var value = logEvent.Properties[name];

            if (IsSensitive(name))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(Redacted)));
                continue;
            }

            var scrubbed = Scrub(value, depth: 0);
            if (!ReferenceEquals(scrubbed, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, scrubbed));
            }
        }
    }

    internal static bool IsSensitive(string propertyName) =>
        SensitiveNameFragments.Any(fragment =>
            propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the value with any sensitively-named member redacted, or the very same instance
    /// when there was nothing to change — the reference check is what keeps an untouched event
    /// from being rebuilt property by property.
    /// </summary>
    private static LogEventPropertyValue Scrub(LogEventPropertyValue value, int depth)
    {
        if (depth >= MaxDepth)
            return value;

        switch (value)
        {
            case StructureValue structure:
            {
                var properties = new List<LogEventProperty>(structure.Properties.Count);
                var changed = false;

                foreach (var property in structure.Properties)
                {
                    if (IsSensitive(property.Name))
                    {
                        properties.Add(new LogEventProperty(property.Name, new ScalarValue(Redacted)));
                        changed = true;
                        continue;
                    }

                    var scrubbed = Scrub(property.Value, depth + 1);
                    changed |= !ReferenceEquals(scrubbed, property.Value);
                    properties.Add(new LogEventProperty(property.Name, scrubbed));
                }

                return changed ? new StructureValue(properties, structure.TypeTag) : value;
            }

            case DictionaryValue dictionary:
            {
                var entries = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
                var changed = false;

                foreach (var entry in dictionary.Elements)
                {
                    if (entry.Key.Value is string key && IsSensitive(key))
                    {
                        entries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                            entry.Key, new ScalarValue(Redacted)));
                        changed = true;
                        continue;
                    }

                    var scrubbed = Scrub(entry.Value, depth + 1);
                    changed |= !ReferenceEquals(scrubbed, entry.Value);
                    entries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(entry.Key, scrubbed));
                }

                return changed ? new DictionaryValue(entries) : value;
            }

            case SequenceValue sequence:
            {
                var elements = new List<LogEventPropertyValue>(sequence.Elements.Count);
                var changed = false;

                foreach (var element in sequence.Elements)
                {
                    var scrubbed = Scrub(element, depth + 1);
                    changed |= !ReferenceEquals(scrubbed, element);
                    elements.Add(scrubbed);
                }

                return changed ? new SequenceValue(elements) : value;
            }

            default:
                return value;
        }
    }
}
