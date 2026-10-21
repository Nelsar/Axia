using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Nelsar.Axia.Infrastructure.Persistence;

namespace Nelsar.Axia.Api.Configuration;

/// <summary>
/// A settings file kept somewhere other than next to the binaries, named with
/// <c>--SettingsFile &lt;path&gt;</c>.
///
/// An installed copy needs it: the binaries are in Program Files, which the service cannot write
/// and an upgrade replaces, while the settings — the signing and encryption keys among them —
/// have to outlive both. The Windows installer keeps them in
/// <c>%ProgramData%\Nelsar.Axia\appsettings.json</c> and starts the service with this argument.
/// </summary>
internal static class SettingsFile
{
    public const string Key = "SettingsFile";

    /// <summary>
    /// Reads the file named by <see cref="Key"/>, if one is, as the API's settings. It ranks where
    /// <c>appsettings.json</c> does: above the defaults, below environment variables and the
    /// command line, so that an operator can still override one setting without editing it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file named is not there.</exception>
    public static void AddTo(ConfigurationManager configuration)
    {
        var path = configuration[Key];
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path);

        // Named and missing stops startup. Starting without it would mean starting without the
        // keys and the directory it holds, and failing on those instead, a step away from the cause.
        if (!File.Exists(fullPath))
            throw new InvalidOperationException(
                $"{Key} is '{path}', and there is no file at '{fullPath}'. Create it — "
                + $"`Nelsar.Axia.Api init-settings \"{fullPath}\"` writes one with new keys — "
                + "or correct the path.");

        var source = new JsonConfigurationSource
        {
            FileProvider = new PhysicalFileProvider(Path.GetDirectoryName(fullPath)!),
            Path = Path.GetFileName(fullPath),
            Optional = false,
            ReloadOnChange = false
        };

        var sources = configuration.Sources;
        var environment = sources.ToList().FindIndex(s => s is EnvironmentVariablesConfigurationSource);
        if (environment < 0)
            sources.Add(source);
        else
            sources.Insert(environment, source);
    }

    /// <summary>
    /// Brings a settings file up to what an installed instance needs, and reports whether it
    /// changed anything.
    ///
    /// Missing keys are generated; the database, log file and address get defaults that keep them
    /// beside the settings file; then <paramref name="settings"/> are written over whatever is
    /// there. Nothing already set is touched otherwise, so running this again on an upgrade or a
    /// repair keeps the keys — replacing <c>Encryption:Key</c> would make every stored bind
    /// password unreadable.
    /// </summary>
    /// <param name="root">The file's content; changed in place.</param>
    /// <param name="folder">The folder the settings file is in.</param>
    /// <param name="settings">Settings to write, as <c>Section:Key</c> and a value. Empty values are skipped.</param>
    public static bool Initialize(JsonObject root, string folder, IEnumerable<KeyValuePair<string, string>> settings)
    {
        var changed = false;

        changed |= SetIfMissing(root, "Jwt:Key", () => NewKey(48));
        changed |= SetIfMissing(root, "Encryption:Key", () => NewKey(32));
        changed |= SetIfMissing(root, AxiaDatabase.ProviderKey, () => nameof(DatabaseProvider.Sqlite));
        changed |= SetIfMissing(root, $"ConnectionStrings:{AxiaDatabase.ConnectionStringName}",
            () => $"Data Source={Path.Combine(folder, "nelsar-axia.db")}");
        changed |= SetIfMissing(root, "Observability:File:Path", () => Path.Join(folder, "logs", "axia-.json"));
        changed |= SetIfMissing(root, "Urls", () => "http://+:8080");

        foreach (var (key, value) in settings)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var (parent, name) = Walk(root, key);
            if (parent[name]?.GetValueKind() == JsonValueKind.String && parent[name]!.GetValue<string>() == value)
                continue;

            parent[name] = value;
            changed = true;
        }

        return changed;
    }

    /// <summary>Reads a settings file, comments and all, or an empty one when there is no file.</summary>
    public static JsonObject Read(string path)
    {
        if (!File.Exists(path))
            return [];

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonNode.Parse(File.ReadAllText(path), documentOptions: options) as JsonObject
            ?? throw new InvalidOperationException($"'{path}' does not hold a JSON object.");
    }

    /// <summary>
    /// Writes a settings file whole or not at all: written beside it first, then moved over it, so
    /// that a failure halfway leaves the old file — and its keys — as they were.
    /// </summary>
    public static void Write(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".new";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool SetIfMissing(JsonObject root, string key, Func<string> value)
    {
        var (parent, name) = Walk(root, key);
        if (parent[name] is JsonValue existing
            && existing.GetValueKind() == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(existing.GetValue<string>()))
            return false;

        parent[name] = value();
        return true;
    }

    // The object holding the last segment of a Section:Key path, created on the way as needed.
    // Configuration keys are case-insensitive, so an existing section is found whatever its case.
    private static (JsonObject Parent, string Name) Walk(JsonObject root, string key)
    {
        var segments = key.Split(':');
        var current = root;

        foreach (var name in segments[..^1].Select(segment =>
                     current.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, segment, StringComparison.OrdinalIgnoreCase))
                     ?? segment))
        {
            if (current[name] is not JsonObject next)
            {
                next = [];
                current[name] = next;
            }

            current = next;
        }

        var last = segments[^1];
        return (current, current.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, last, StringComparison.OrdinalIgnoreCase)) ?? last);
    }

    private static string NewKey(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
}
