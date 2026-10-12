using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Domain.Services;

/// <summary>
/// Builds a pool of candidate logins from either a mask ("node-{01..99}") or a regex
/// applied to pasted raw text, then normalises the result into directory-safe form.
///
/// Pure domain logic: no directory access, no persistence, no configuration. That makes
/// every rule here — range expansion, transliteration, normalisation — directly testable
/// and reusable outside the user-creation flow it currently serves.
/// </summary>
public static class UserPatternGenerator
{
    private static readonly IReadOnlyDictionary<char, string> CyrillicToLatinMap = new Dictionary<char, string>
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch",
        ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        // Kazakh Cyrillic extensions
        ['ә'] = "a", ['ғ'] = "g", ['қ'] = "q", ['ң'] = "n", ['ө'] = "o", ['ұ'] = "u", ['ү'] = "u", ['һ'] = "h", ['і'] = "i"
    };

    /// <summary>
    /// How long a regex may spend on the whole of the pasted text. Each line already has its own
    /// timeout, but a line that times out is only skipped, so a pattern that backtracks on every
    /// line took that timeout once per line — hours, for a paste of a few thousand lines.
    /// </summary>
    public static readonly TimeSpan RegexTimeBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Expands the spec into normalised candidate logins: trimmed, lower-cased,
    /// de-duplicated and capped at <see cref="UserPatternSpec.MaxItems"/>.
    /// Returns an empty list for input the generator cannot make sense of
    /// (blank mask, invalid regex) rather than throwing.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// The regex took longer than <see cref="RegexTimeBudget"/> over the pasted text.
    /// </exception>
    public static IReadOnlyList<string> Generate(UserPatternSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        IEnumerable<string> items = !string.IsNullOrWhiteSpace(spec.RawText) && !string.IsNullOrWhiteSpace(spec.RegexPattern)
            ? ExpandFromRegex(spec.RawText, spec.RegexPattern, spec.UsernameTemplate, spec.MaxItems)
            : ExpandPattern(spec.Pattern, spec.MaxItems);

        if (spec.Transliterate)
            items = items.Select(Transliterate);

        return items
            .Select(i => i.Trim().ToLowerInvariant())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct()
            .Take(spec.MaxItems)
            .ToArray();
    }

    /// <summary>Maps Cyrillic (including Kazakh extensions) to Latin, preserving casing.</summary>
    public static string Transliterate(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var lower = char.ToLowerInvariant(ch);
            if (CyrillicToLatinMap.TryGetValue(lower, out var mapped))
            {
                sb.Append(char.IsUpper(ch) && mapped.Length > 0
                    ? char.ToUpperInvariant(mapped[0]) + mapped[1..]
                    : mapped);
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<string> ExpandFromRegex(string rawText, string regexPattern, string? usernameTemplate, int maxItems)
    {
        Regex regex;
        try
        {
            regex = new Regex(regexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(500));
        }
        catch (ArgumentException)
        {
            return Array.Empty<string>();
        }

        var groupNames = regex.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToArray();
        var results = new List<string>();
        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var elapsed = Stopwatch.StartNew();

        foreach (var line in lines)
        {
            if (results.Count >= maxItems)
                break;

            // Refused rather than cut short: a preview that silently stopped halfway would look
            // like the whole list, and it is read to decide what to create.
            if (elapsed.Elapsed > RegexTimeBudget)
                throw new DomainValidationException(DomainMessages.PatternRegexTooSlow());

            Match match;
            try
            {
                match = regex.Match(line);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!match.Success)
                continue;

            if (!string.IsNullOrWhiteSpace(usernameTemplate))
            {
                var value = usernameTemplate;
                foreach (var groupName in groupNames)
                    value = value.Replace("{" + groupName + "}", match.Groups[groupName].Value);
                results.Add(value);
            }
            else
            {
                results.Add(match.Value);
            }
        }

        return results;
    }

    private static IEnumerable<string> ExpandPattern(string pattern, int maxItems)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return Array.Empty<string>();

        var tokens = new List<IEnumerable<string>>();
        var current = new StringBuilder();
        var activeRange = false;
        var rangeBuilder = new StringBuilder();

        foreach (var ch in pattern)
        {
            if (ch == '{')
            {
                if (activeRange)
                    return Array.Empty<string>();

                if (current.Length > 0)
                {
                    tokens.Add(new[] { current.ToString() });
                    current.Clear();
                }
                activeRange = true;
                rangeBuilder.Clear();
                continue;
            }

            if (ch == '}' && activeRange)
            {
                activeRange = false;
                tokens.Add(ParseRange(rangeBuilder.ToString()));
                continue;
            }

            if (activeRange)
            {
                rangeBuilder.Append(ch);
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(new[] { current.ToString() });

        // Take(maxItems) below is what bounds the work, and it can only do that while every part
        // yields something: one empty part leaves the product empty, and Take then walks every
        // combination of the others looking for an item that never comes. ParseRange does not
        // produce one, and this keeps it that way.
        if (tokens.Any(part => !part.Any()))
            return Array.Empty<string>();

        var combinations = tokens.Aggregate(
            new[] { string.Empty }.AsEnumerable(),
            (seed, part) => seed.SelectMany(prefix => part.Select(value => prefix + value)));

        return combinations.Take(maxItems).ToArray();
    }

    private static IEnumerable<string> ParseRange(string rangeExpression)
    {
        var parts = rangeExpression.Split("..", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return new[] { "{" + rangeExpression + "}" };

        var left = parts[0];
        var right = parts[1];
        if (int.TryParse(left, out var leftNum) && int.TryParse(right, out var rightNum))
        {
            var width = Math.Max(left.Length, right.Length);
            if (leftNum <= rightNum)
            {
                return Numbers(leftNum, rightNum)
                    .Select(value => value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'));
            }
        }

        if (left.Length == 1 && right.Length == 1)
        {
            var start = char.ToUpperInvariant(left[0]);
            var end = char.ToUpperInvariant(right[0]);
            if (start <= end)
            {
                return Enumerable.Range(start, end - start + 1)
                    .Select(value => ((char)value).ToString());
            }
        }

        return new[] { "{" + rangeExpression + "}" };
    }

    /// <summary>
    /// Every whole number from <paramref name="first"/> to <paramref name="last"/>, lazily.
    ///
    /// Counted in long rather than handed to Enumerable.Range, whose count is an int: the count of
    /// {-2147483648..2147483647} is 2^32, which wraps to 0 — an empty range, with the consequence
    /// described in ExpandPattern — and one short of that wraps to -1, which Enumerable.Range
    /// throws on, answering the request with a 500.
    /// </summary>
    private static IEnumerable<long> Numbers(long first, long last)
    {
        for (var value = first; value <= last; value++)
            yield return value;
    }
}
