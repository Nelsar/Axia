using System.Text.Json;
using Serilog;

namespace Nelsar.Axia.Api.Configuration;

/// <summary>
/// <c>Nelsar.Axia.Api init-settings &lt;path&gt; [Section:Key=value]…</c>
///
/// Creates the settings file an installed instance runs with, or brings an existing one up to
/// date, and exits without starting the API. The Windows installer runs it on every install,
/// upgrade and repair; see <see cref="SettingsFile.Initialize"/> for what it writes and what it
/// leaves alone. Usable by hand as well, to set up a copy started any other way.
///
/// Terse on purpose: the installer's whole command line has to fit the 255 characters Windows
/// Installer allows a custom action.
/// </summary>
internal static class InitSettingsCommand
{
    private const string Name = "init-settings";

    public static bool IsInvoked(string[] args) => args.Length > 0 && args[0] == Name;

    public static int Run(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith('-') || args[1].Contains('='))
            return Usage();

        var path = args[1];
        var settings = new List<KeyValuePair<string, string>>();

        foreach (var setting in args.Skip(2))
        {
            var at = setting.IndexOf('=');
            if (at <= 0)
                return Usage();

            settings.Add(new(setting[..at].Trim(), setting[(at + 1)..].Trim()));
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = SettingsFile.Read(fullPath);

            if (!SettingsFile.Initialize(root, Path.GetDirectoryName(fullPath)!, settings))
            {
                Log.Information("{Path} is up to date.", fullPath);
                return 0;
            }

            SettingsFile.Write(fullPath, root);

            // The keys themselves are never logged; what was set is, by name only.
            Log.Information("Wrote {Path}. Settings given: {Settings}.", fullPath,
                settings.Where(s => s.Value.Length > 0).Select(s => s.Key).DefaultIfEmpty("none"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Log.Error("{Command} could not write the settings file: {Reason}", Name, ex.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Log.Error("Usage: {Command} <path to the settings file> [Section:Key=value]…", Name);
        return 2;
    }
}
