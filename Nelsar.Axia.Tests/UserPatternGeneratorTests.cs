using System.Diagnostics;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Services;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Direct coverage for the pure generation rules. These used to be private static
/// methods on UserService and were only reachable through an async service call with
/// four mocked dependencies; the edge cases below (letter ranges, malformed ranges,
/// invalid regex, nested braces) had no coverage at all before the extraction.
/// </summary>
public class UserPatternGeneratorTests
{
    [Fact]
    public void Generate_ExpandsNumericRange_PreservingZeroPadding()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            Pattern = "node-{01..03}",
            MaxItems = 10
        });

        Assert.Equal(new[] { "node-01", "node-02", "node-03" }, result);
    }

    [Fact]
    public void Generate_ExpandsLetterRange()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            Pattern = "srv-{a..c}",
            MaxItems = 10
        });

        Assert.Equal(new[] { "srv-a", "srv-b", "srv-c" }, result);
    }

    [Fact]
    public void Generate_CapsOutputAtMaxItems()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            Pattern = "node-{01..99}",
            MaxItems = 5
        });

        Assert.Equal(5, result.Count);
        Assert.Equal("node-01", result[0]);
        Assert.Equal("node-05", result[4]);
    }

    [Theory]
    // Not a ".." range at all — left as a literal rather than silently dropped.
    [InlineData("node-{01-03}", "node-{01-03}")]
    // Descending bounds are not a valid range either.
    [InlineData("node-{03..01}", "node-{03..01}")]
    public void Generate_LeavesMalformedRangeAsLiteral(string pattern, string expected)
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            Pattern = pattern,
            MaxItems = 10
        });

        Assert.Equal(expected, Assert.Single(result));
    }

    [Fact]
    public void Generate_ReturnsEmpty_WhenBracesAreNested()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            Pattern = "node-{{01..03}",
            MaxItems = 10
        });

        Assert.Empty(result);
    }

    [Fact]
    public void Generate_ReturnsEmpty_WhenPatternIsBlank()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec { MaxItems = 10 });

        Assert.Empty(result);
    }

    [Fact]
    public void Generate_ReturnsEmpty_WhenRegexIsInvalid()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            RawText = "Ivanov Ivan",
            RegexPattern = "([unclosed",
            MaxItems = 10
        });

        Assert.Empty(result);
    }

    [Fact]
    public void Generate_AppliesTemplateToNamedGroups()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            RawText = "Ivanov Ivan\nPetrov Petr",
            RegexPattern = @"^(?<lastname>\S+)\s+(?<firstname>\S+)$",
            UsernameTemplate = "{firstname}.{lastname}",
            MaxItems = 10
        });

        Assert.Equal(new[] { "ivan.ivanov", "petr.petrov" }, result);
    }

    [Fact]
    public void Generate_FallsBackToFullMatch_WhenTemplateIsOmitted()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            RawText = "Ivanov Ivan",
            RegexPattern = @"^\S+",
            MaxItems = 10
        });

        Assert.Equal("ivanov", Assert.Single(result));
    }

    [Fact]
    public void Generate_DeduplicatesGeneratedValues()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            RawText = "Ivan\nIVAN\nIvan",
            RegexPattern = @"^\S+$",
            MaxItems = 10
        });

        Assert.Equal("ivan", Assert.Single(result));
    }

    [Theory]
    [InlineData("Иванов", "Ivanov")]
    [InlineData("Щербак", "Shcherbak")]
    // Soft/hard signs map to nothing rather than to a placeholder character.
    [InlineData("Игорь", "Igor")]
    // Kazakh Cyrillic extensions.
    [InlineData("Қанат", "Qanat")]
    [InlineData("Әсел", "Asel")]
    // Latin input passes through untouched.
    [InlineData("Smith", "Smith")]
    public void Transliterate_MapsCyrillicToLatin_PreservingCasing(string input, string expected)
    {
        Assert.Equal(expected, UserPatternGenerator.Transliterate(input));
    }

    [Fact]
    public void Generate_TransliteratesBeforeNormalising()
    {
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            RawText = "Иванов Иван",
            RegexPattern = @"^(?<lastname>\S+)\s+(?<firstname>\S+)$",
            UsernameTemplate = "{firstname}.{lastname}",
            Transliterate = true,
            MaxItems = 10
        });

        Assert.Equal("ivan.ivanov", Assert.Single(result));
    }

    [Fact]
    public async Task Generate_Finishes_WhenARangeSpansEveryInt()
    {
        // Counted as an int, {-2147483648..2147483647} holds 2^32 values, which wraps to 0. The
        // range came out empty, the whole product with it, and Take(MaxItems) walked four
        // quintillion combinations of the other two ranges looking for an item: one request held
        // a thread for good. Run elsewhere so that a regression fails here instead of hanging.
        var generating = Task.Run(() => UserPatternGenerator.Generate(new UserPatternSpec
        {
            Pattern = "u{1..2000000000}{1..2000000000}{-2147483648..2147483647}",
            MaxItems = 3
        }));

        var first = await Task.WhenAny(generating, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(first == generating, "The mask was still expanding after 10 seconds.");

        var result = await generating;
        Assert.Equal(3, result.Count);
        Assert.EndsWith("-2147483648", result[0]);
    }

    [Fact]
    public void Generate_ExpandsARangeOneShortOfEveryInt_InsteadOfThrowing()
    {
        // One short of 2^32 wraps to -1, which Enumerable.Range threw on — answered with a 500.
        var result = UserPatternGenerator.Generate(new UserPatternSpec
        {
            Pattern = "{-2147483648..2147483646}",
            MaxItems = 2
        });

        Assert.Equal(["-2147483648", "-2147483647"], result);
    }

    [Fact]
    public void Generate_RefusesARegex_ThatOutrunsItsBudgetOverThePastedText()
    {
        // Each line on its own times out and is skipped. Before the budget that repeated once per
        // line, for as many lines as were pasted: here fifty half-second timeouts.
        var line = new string('a', 40) + "!";
        var text = string.Join('\n', Enumerable.Repeat(line, 50));
        var elapsed = Stopwatch.StartNew();

        Assert.Throws<DomainValidationException>(() => UserPatternGenerator.Generate(new UserPatternSpec
        {
            RawText = text,
            RegexPattern = "^(a+)+$",
            MaxItems = 10
        }));

        // The budget, plus the line already running when it ran out.
        Assert.True(elapsed.Elapsed < UserPatternGenerator.RegexTimeBudget + TimeSpan.FromSeconds(3),
            $"Took {elapsed.Elapsed}.");
    }
}
