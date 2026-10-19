using System.Globalization;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace Nelsar.Axia.Api.Observability;

/// <summary>
/// Writes control characters in log property values as escapes — <c>\r</c>, <c>\n</c>,
/// <c>\t</c>, <c>\u001b</c> — so that a value can never start a line of its own.
///
/// It is for the plain-text console only, which renders a value as it stands. A request path
/// decodes <c>%0D%0A</c> into a real line break, so a request for
/// <c>/x%0D%0A[12:00:00 INF] signed in</c> wrote a line of the caller's choosing into it (OWASP log
/// injection), and an escape character in a value could restyle the terminal reading it. Every
/// other sink writes JSON, which escapes these itself; escaping there too would only turn one real
/// newline in a value into two characters.
///
/// The exception is left alone: its stack trace is meant to span lines.
/// </summary>
public sealed class ControlCharacterEscaper : ILogEventEnricher
{
    /// <summary>As in <see cref="SensitivePropertyRedactor"/>: deep enough for real payloads,
    /// bounded so a self-referential graph cannot spin here.</summary>
    private const int MaxDepth = 6;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Snapshot the names first: AddOrUpdateProperty mutates the dictionary being read.
        foreach (var name in logEvent.Properties.Keys.ToArray())
        {
            var value = logEvent.Properties[name];
            var escaped = Escape(value, depth: 0);
            if (!ReferenceEquals(escaped, value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, escaped));
        }
    }

    /// <summary>The value with every string in it escaped, or the very same instance when there was
    /// nothing to escape.</summary>
    private static LogEventPropertyValue Escape(LogEventPropertyValue value, int depth)
    {
        if (depth >= MaxDepth)
            return value;

        switch (value)
        {
            case ScalarValue { Value: string text }:
                var escapedText = Escape(text);
                return ReferenceEquals(escapedText, text) ? value : new ScalarValue(escapedText);

            case StructureValue structure:
            {
                var properties = structure.Properties
                    .Select(p => new LogEventProperty(p.Name, Escape(p.Value, depth + 1)))
                    .ToList();
                return properties.Zip(structure.Properties).All(pair => ReferenceEquals(pair.First.Value, pair.Second.Value))
                    ? value
                    : new StructureValue(properties, structure.TypeTag);
            }

            case SequenceValue sequence:
            {
                var elements = sequence.Elements.Select(e => Escape(e, depth + 1)).ToList();
                return elements.Zip(sequence.Elements).All(pair => ReferenceEquals(pair.First, pair.Second))
                    ? value
                    : new SequenceValue(elements);
            }

            case DictionaryValue dictionary:
            {
                var entries = dictionary.Elements
                    .Select(e => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        (ScalarValue)Escape(e.Key, depth + 1), Escape(e.Value, depth + 1)))
                    .ToList();
                return entries.Zip(dictionary.Elements).All(pair =>
                        ReferenceEquals(pair.First.Key, pair.Second.Key) && ReferenceEquals(pair.First.Value, pair.Second.Value))
                    ? value
                    : new DictionaryValue(entries);
            }

            default:
                return value;
        }
    }

    /// <summary>The text with its control characters escaped, or the same string when it has
    /// none.</summary>
    internal static string Escape(string text)
    {
        if (!text.Any(char.IsControl))
            return text;

        var escaped = new StringBuilder(text.Length + 8);
        foreach (var character in text)
        {
            switch (character)
            {
                case '\r': escaped.Append(@"\r"); break;
                case '\n': escaped.Append(@"\n"); break;
                case '\t': escaped.Append(@"\t"); break;
                default:
                    if (char.IsControl(character))
                        escaped.Append(@"\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        escaped.Append(character);
                    break;
            }
        }

        return escaped.ToString();
    }
}
