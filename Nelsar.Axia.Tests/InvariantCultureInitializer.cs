using System.Globalization;
using System.Runtime.CompilerServices;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Pins the whole test run to the invariant culture before any test is discovered. Without it the
/// suite reads the machine's own culture: on a Russian or Kazakh desktop the neutral messages come
/// back translated, so every assertion on English text fails, and every "is this translated?"
/// check compares Russian with Russian and reports the translation missing. CI ran green on Ubuntu
/// while a contributor's first <c>dotnet test</c> ran red.
///
/// Tests that need a particular language still set <see cref="CultureInfo.CurrentUICulture"/>
/// themselves; this only fixes what they start from.
/// </summary>
internal static class InvariantCultureInitializer
{
    // CA2255 warns against module initializers in libraries, where they run inside somebody
    // else's process. A test assembly is the process, and this is exactly the once-per-run hook
    // xUnit v2 does not otherwise offer.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
