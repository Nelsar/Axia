namespace Nelsar.Axia.Api.Observability;

/// <summary>
/// Which sinks the log goes to, and where each one points.
///
/// Serilog's own <c>Serilog:WriteTo</c> array is the canonical way to configure sinks, and it is
/// still honoured (see <see cref="SerilogRegistration"/>) — but it is a list, and a list is a poor
/// switch. Turning Loki on in a container would mean appending an array element through
/// <c>Serilog__WriteTo__3__Name</c>, with the index deciding whether it lands next to the console
/// sink or on top of it. What operations actually wants is a boolean per destination, which is
/// what this is: <c>Observability__Loki__Enabled=true</c> and the address next to it.
///
/// Console and file are on by default because a host that has been given no logging configuration
/// at all must still leave a trace of itself. The two shippers are off by default because an
/// address that has not been supplied cannot be guessed, and a sink pointed at nothing spends
/// every request failing to reach it.
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Names this service in every event, which is what separates its logs from everything else
    /// sharing an index in Kibana or a Loki instance.
    /// </summary>
    public string ApplicationName { get; set; } = "Nelsar.Axia.Api";

    public ConsoleSinkOptions Console { get; set; } = new();

    public FileSinkOptions File { get; set; } = new();

    public LogstashSinkOptions Logstash { get; set; } = new();

    public LokiSinkOptions Loki { get; set; } = new();

    public CorrelationOptions Correlation { get; set; } = new();
}

/// <summary>Standard output. Whatever runs the container collects it.</summary>
public sealed class ConsoleSinkOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Compact JSON rather than a rendered line. On by default because the console is what a
    /// container hands to Docker's json-file driver, Filebeat or Promtail, and a rendered line
    /// arrives there as one unparsed string. Development turns it off — see
    /// appsettings.Development.json.example — because at that point a person is reading it.
    /// </summary>
    public bool Json { get; set; } = true;
}

/// <summary>
/// A rolling file. This is the ELK path that does not need the application to know ELK exists:
/// Filebeat tails the directory and Logstash never hears from the process itself.
/// </summary>
public sealed class FileSinkOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The trailing dash is where the date goes: <c>axia-20260816.json</c>. Relative paths are
    /// resolved against the content root, so a container should mount whatever this points at.
    /// </summary>
    public string Path { get; set; } = "logs/axia-.json";

    public int RetainedFileCountLimit { get; set; } = 14;

    public long FileSizeLimitBytes { get; set; } = 128L * 1024 * 1024;
}

/// <summary>
/// Logstash's HTTP input (<c>input { http { port =&gt; 8080 } }</c>), reached with
/// Serilog.Sinks.Http.
///
/// HTTP rather than TCP because the sink batches, buffers in memory up to
/// <see cref="QueueLimitBytes"/>, and drops the oldest events rather than the request being logged
/// when Logstash is unreachable. A log shipper must never be able to take the API down with it.
/// </summary>
public sealed class LogstashSinkOptions
{
    public bool Enabled { get; set; }

    public string? Uri { get; set; }

    /// <summary>How much is held in memory while Logstash is unreachable. 32 MB, then oldest-out.</summary>
    public long QueueLimitBytes { get; set; } = 32L * 1024 * 1024;

    public long BatchSizeLimitBytes { get; set; } = 5L * 1024 * 1024;

    public int BatchPostingLimit { get; set; } = 1000;

    public int PeriodSeconds { get; set; } = 2;
}

/// <summary>
/// Grafana Loki's push endpoint, reached with Serilog.Sinks.Grafana.Loki.
///
/// Loki indexes labels, not content, so <see cref="Labels"/> stays small and low-cardinality:
/// app, environment, maybe instance. Putting anything per-request in there — a correlation id, a
/// user, a path — is the documented way to bring a Loki instance down, and those belong in the
/// event body where LogQL still finds them.
/// </summary>
public sealed class LokiSinkOptions
{
    public bool Enabled { get; set; }

    public string? Uri { get; set; }

    public Dictionary<string, string> Labels { get; set; } = new();

    /// <summary>
    /// Basic-auth credentials for a Loki behind a gateway. Supply through the environment, never
    /// through a checked-in appsettings file.
    /// </summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Sent as <c>X-Scope-OrgID</c>. Only meaningful on a multi-tenant Loki.</summary>
    public string? Tenant { get; set; }
}

/// <summary>Where the request-scoped correlation id is read from and echoed back.</summary>
public sealed class CorrelationOptions
{
    public string HeaderName { get; set; } = "X-Correlation-ID";
}
