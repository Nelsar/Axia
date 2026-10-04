using Nelsar.Axia.Api.Authorization;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;
using Serilog.Sinks.Http.BatchFormatters;
using Serilog.Sinks.Http.TextFormatters;
using Nelsar.Axia.Contracts.Diagnostics;

namespace Nelsar.Axia.Api.Observability;

/// <summary>
/// Stands the log up: which sinks, what goes on every event, and what a finished request leaves
/// behind.
///
/// Serilog replaces the default provider rather than sitting next to it, so <c>ILogger&lt;T&gt;</c>
/// everywhere else in the solution keeps working untouched and none of the layers below learn that
/// Serilog exists. That is the whole point of the arrangement: structured logging is a hosting
/// decision, made once here, and a handler still writes <c>_logger.LogWarning("...{Thing}", thing)</c>
/// exactly as it did before.
/// </summary>
public static class SerilogRegistration
{
    /// <summary>
    /// Binds <see cref="ObservabilityOptions"/> and installs Serilog as the logging provider.
    /// </summary>
    public static WebApplicationBuilder AddAxiaObservability(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(ObservabilityOptions.SectionName);

        // The middleware reads this out of DI; the logger below cannot, because it is built while
        // the container still is.
        builder.Services.Configure<ObservabilityOptions>(section);

        var options = section.Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var logger = Configure(new LoggerConfiguration(), builder.Configuration, builder.Environment, options)
            .CreateLogger();

        // Takes over from the bootstrap logger in Program.cs, so the fatal-error path there
        // reaches the configured sinks rather than only the console it started with.
        Log.Logger = logger;

        // A built logger rather than a callback, and dispose:false, on purpose. The callback form
        // hands the container a reloadable logger that it freezes on first resolve, and freezing
        // is once-per-process while host construction is not: WebApplicationFactory stands up a
        // host per test class, and the second one through fails with "The logger is already
        // frozen". Lifetime stays with Log.CloseAndFlushAsync() in Program.cs, which is where a
        // batched sink gets its last chance to drain anyway.
        builder.Services.AddSerilog(logger, dispose: false);

        return builder;
    }

    /// <summary>
    /// Puts the request pipeline's logging in place. Call before <c>UseExceptionHandler</c>:
    /// see the ordering note inside.
    /// </summary>
    public static WebApplication UseAxiaObservability(this WebApplication app)
    {
        // Outermost, and ahead of the exception handler on purpose. The correlation id is pushed
        // onto an ambient scope that ends when this middleware returns, so anything that runs
        // outside it — including GlobalExceptionHandler, which logs the failures that matter most
        // — would log without an id to trace it by.
        app.UseMiddleware<CorrelationIdMiddleware>();

        // One event per request, instead of the two-per-request the default ASP.NET Core provider
        // writes and the dozen it writes at Debug. It sits outside UseExceptionHandler so the
        // status code it records is the one actually sent: from the inside it would see the
        // exception rather than the 500 the handler turns it into, and log both.
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = LevelFor;
            options.EnrichDiagnosticContext = Describe;
        });

        return app;
    }

    /// <summary>
    /// A completed request is Information; a failed one is Error so it alerts; a refused one is
    /// Warning, because a burst of 401s and 403s is the shape of an attack and ought to be
    /// findable without being paged for.
    /// </summary>
    private static LogEventLevel LevelFor(HttpContext context, double elapsedMs, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            return LogEventLevel.Error;

        return context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => LogEventLevel.Warning,
            StatusCodes.Status403Forbidden => LogEventLevel.Warning,
            StatusCodes.Status429TooManyRequests => LogEventLevel.Warning,
            _ => LogEventLevel.Information
        };
    }

    /// <summary>
    /// What gets attached to the request-completion event beyond method, path, status and duration.
    ///
    /// The caller is named through <see cref="LogMask"/> and the client address is left out
    /// entirely. Both are deliberate, and for the same reason the mask exists at all: a diagnostic
    /// log from an identity service is otherwise a standing record of who signed in and from
    /// where, kept for as long as whichever system it shipped to keeps things. The audit trail
    /// records the actor in full — that is its job — and this is not the audit trail.
    /// </summary>
    private static void Describe(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        diagnosticContext.Set("RequestHost", context.Request.Host.Value);
        diagnosticContext.Set("Subject", LogMask.Login(RequestPrincipal.Subject(context.User)));

        var providerId = RequestPrincipal.ProviderId(context);
        if (providerId is not null)
            diagnosticContext.Set("LdapProviderId", providerId);

        var endpoint = context.GetEndpoint()?.DisplayName;
        if (endpoint is not null)
            diagnosticContext.Set("Endpoint", endpoint);
    }

    /// <summary>
    /// Builds the logger. Internal rather than private so the sink-selection rules can be tested
    /// as rules, without standing up a host to find out which sinks it chose.
    /// </summary>
    internal static LoggerConfiguration Configure(
        LoggerConfiguration logger,
        IConfiguration configuration,
        IHostEnvironment environment,
        ObservabilityOptions options)
    {
        logger
            // Levels, per-namespace overrides and any filters come from the standard `Serilog`
            // section, which is where an operator expects to find them and where every Serilog
            // reference documents them. Sinks do not: those are the `Observability` section
            // above, so that switching a destination is a boolean rather than an edit to an array.
            // A `Serilog:WriteTo` entry is still honoured for anything this does not cover, but
            // it stacks on top of the sinks below rather than replacing them.
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Application", options.ApplicationName)
            .Enrich.WithProperty("Environment", environment.EnvironmentName)
            // Last, so that it sees properties contributed by everything above it — including the
            // ambient ones pushed onto LogContext during a request.
            .Enrich.With(new SensitivePropertyRedactor());

        if (options.Console.Enabled)
        {
            if (options.Console.Json)
                logger.WriteTo.Console(new CompactJsonFormatter());
            else
                logger.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}");
        }

        if (options.File.Enabled)
        {
            logger.WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: options.File.Path,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: options.File.FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: options.File.RetainedFileCountLimit,
                // A log write must never block the request that caused it.
                buffered: false,
                shared: false);
        }

        if (options.Logstash.Enabled)
        {
            // Enabled with nowhere to send to is a configuration mistake, and it is worth failing
            // at startup over: the alternative is a service that looks healthy while its audit-
            // adjacent log goes nowhere, discovered during the incident it was meant to explain.
            var uri = Required(options.Logstash.Uri, "Observability:Logstash:Uri");

            logger.WriteTo.Http(
                requestUri: uri,
                queueLimitBytes: options.Logstash.QueueLimitBytes,
                logEventsInBatchLimit: options.Logstash.BatchPostingLimit,
                batchSizeLimitBytes: options.Logstash.BatchSizeLimitBytes,
                period: TimeSpan.FromSeconds(options.Logstash.PeriodSeconds),
                // Compact JSON per event, posted as a plain JSON array — which is what a Logstash
                // `http` input with `codec => json` splits into one event each. The sink's default
                // wraps the batch in {"events":[...]} and Logstash would store that envelope as a
                // single document.
                textFormatter: new CompactTextFormatter(),
                batchFormatter: new ArrayBatchFormatter());
        }

        if (options.Loki.Enabled)
        {
            var uri = Required(options.Loki.Uri, "Observability:Loki:Uri");

            var labels = options.Loki.Labels
                .Select(label => new LokiLabel { Key = label.Key, Value = label.Value })
                .ToArray();

            LokiCredentials? credentials = string.IsNullOrWhiteSpace(options.Loki.Username)
                ? null
                : new LokiCredentials
                {
                    Login = options.Loki.Username,
                    Password = options.Loki.Password ?? string.Empty
                };

            logger.WriteTo.GrafanaLoki(
                uri: uri,
                labels: labels,
                credentials: credentials!,
                tenant: options.Loki.Tenant!);
        }

        return logger;
    }

    private static string Required(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"{key} must be set when the sink is enabled.")
            : value;
}
