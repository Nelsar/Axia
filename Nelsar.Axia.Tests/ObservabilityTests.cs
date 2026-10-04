using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Nelsar.Axia.Api.Observability;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace Nelsar.Axia.Tests;

/// <summary>
/// What the log is allowed to contain, and what a caller is allowed to put in it.
///
/// These are not tests about logging working — they are tests about two things that only ever
/// fail quietly. A secret reaching a sink is discovered when someone reads Kibana, and a
/// correlation id that turns out to be caller-controlled is discovered when a trail cannot be
/// trusted. Both are cheap to assert and expensive to find out about later.
/// </summary>
public class ObservabilityTests
{
    // ---- The correlation id a caller may supply ----------------------------------------------

    [Theory]
    [InlineData("7f3a1c2e9b5d4a6f")]
    [InlineData("0af7651916cd43dd8448eb211c80319c")]      // a W3C trace id
    [InlineData("gateway-req-42_a.b")]
    [InlineData("  padded-id  ", "padded-id")]
    public void AWellFormedCorrelationId_IsAccepted(string supplied, string? expected = null)
    {
        Assert.Equal(expected ?? supplied, CorrelationIdMiddleware.Sanitise(supplied));
    }

    [Theory]
    // The whole reason this function exists: a newline forges log entries in any sink that
    // writes a line per event, and the forged entry is indistinguishable from a real one.
    [InlineData("abc\r\n2026-08-16 ERROR Administrator deleted every user")]
    [InlineData("abc\ndef")]
    [InlineData("abc\0def")]
    // Free storage in every system the log reaches, on every line, forever.
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    // Not obviously dangerous, but not an identifier either — and anything permitted here has to
    // survive being pasted into a LogQL or Lucene query by someone chasing an incident.
    [InlineData("id with spaces")]
    [InlineData("{\"injected\":\"json\"}")]
    [InlineData("../../etc/passwd")]
    [InlineData("   ")]
    [InlineData("")]
    public void AMalformedCorrelationId_IsRefused_SoAFreshOneIsMinted(string supplied)
    {
        Assert.Null(CorrelationIdMiddleware.Sanitise(supplied));
    }

    [Fact]
    public void TheLengthLimit_AdmitsAGuidAndATraceId()
    {
        // Both of the things this application itself generates have to pass its own filter.
        Assert.NotNull(CorrelationIdMiddleware.Sanitise(Guid.NewGuid().ToString("N")));
        Assert.NotNull(CorrelationIdMiddleware.Sanitise(Guid.NewGuid().ToString()));
        Assert.True(CorrelationIdMiddleware.MaxLength >= 36);
    }

    [Fact]
    public async Task EveryResponse_CarriesACorrelationId_IncludingTheRefusedOnes()
    {
        using var factory = new ApiTestFactory(_ => { });
        var client = factory.CreateClient();

        // No token: this is a 401 written by the authentication stack, not by a controller. The
        // id has to be there anyway — a caller reporting "I keep getting 401" is exactly who
        // needs one to quote.
        var response = await client.GetAsync("/api/v1/rate-limit-settings");

        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        var correlationId = Assert.Single(values!);
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public async Task ACallersOwnCorrelationId_IsKept_SoATrailCrossesServices()
    {
        using var factory = new ApiTestFactory(_ => { });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/rate-limit-settings");
        request.Headers.Add("X-Correlation-ID", "upstream-gateway-1234");

        var response = await client.SendAsync(request);

        Assert.Equal("upstream-gateway-1234", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task AnInjectedCorrelationId_IsReplaced_NotEchoed()
    {
        using var factory = new ApiTestFactory(_ => { });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/rate-limit-settings");
        // HttpClient itself refuses a literal CR/LF in a header, so the case that reaches a real
        // deployment is the one below: legal as a header, useless as an identifier.
        request.Headers.Add("X-Correlation-ID", "id with spaces and \"quotes\"");

        var response = await client.SendAsync(request);

        var echoed = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.DoesNotContain(" ", echoed);
        Assert.DoesNotContain("\"", echoed);
    }

    // ---- What never reaches a sink -----------------------------------------------------------

    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("BindPassword")]
    [InlineData("AdminPassword")]
    [InlineData("ClientSecret")]
    [InlineData("AccessToken")]
    [InlineData("ApiKey")]
    [InlineData("SigningKey")]
    [InlineData("Authorization")]
    [InlineData("ConnectionString")]
    public void APropertyThatNamesASecret_NeverReachesTheSink(string propertyName)
    {
        var captured = WriteAndCapture(logger =>
            logger.Information("bind attempted with {" + propertyName + "}", "not-a-real-password"));

        Assert.Equal(SensitivePropertyRedactor.Redacted, Scalar(captured, propertyName));
    }

    [Theory]
    [InlineData("UserName")]
    [InlineData("DistinguishedName")]
    [InlineData("ElapsedMs")]
    [InlineData("Request")]
    public void AnOrdinaryProperty_IsLeftAlone(string propertyName)
    {
        var captured = WriteAndCapture(logger =>
            logger.Information("did a thing with {" + propertyName + "}", "cn=ivan"));

        Assert.Equal("cn=ivan", Scalar(captured, propertyName));
    }

    [Fact]
    public void ASecretNestedInsideADestructuredObject_IsRedactedToo()
    {
        // The case a top-level name check misses entirely: nobody logs {Password}, they log the
        // whole settings object during an outage and the bind password rides along inside it.
        var settings = new { Server = "ldap.example.kz", AdminUsername = "svc-bind", AdminPassword = "not-a-real-password" };

        var captured = WriteAndCapture(logger => logger.Information("connecting with {@Settings}", settings));

        var structure = Assert.IsType<StructureValue>(captured.Properties["Settings"]);
        var members = structure.Properties.ToDictionary(p => p.Name, p => ((ScalarValue)p.Value).Value?.ToString());

        Assert.Equal(SensitivePropertyRedactor.Redacted, members["AdminPassword"]);
        // The rest of the object still has to be worth logging.
        Assert.Equal("ldap.example.kz", members["Server"]);
        Assert.Equal("svc-bind", members["AdminUsername"]);
    }

    [Fact]
    public void TheCorrelationId_IsAttachedToEveryEventInsideTheRequestScope()
    {
        // The property is pushed by middleware and read by nothing in between — this is the
        // assertion that the Application layer's handlers get it for free.
        var captured = WriteAndCapture(logger =>
        {
            using (LogContext.PushProperty(CorrelationIdMiddleware.PropertyName, "corr-1234"))
            {
                logger.Information("AuthenticateCommand handled in {ElapsedMs}ms", 12);
            }
        });

        Assert.Equal("corr-1234", Scalar(captured, CorrelationIdMiddleware.PropertyName));
    }

    [Fact]
    public void EveryEvent_NamesTheApplicationAndEnvironment_SoOneIndexCanHoldMoreThanOne()
    {
        var captured = WriteAndCapture(logger => logger.Information("anything"));

        Assert.Equal("Nelsar.Axia.Api", Scalar(captured, "Application"));
        Assert.Equal("Testing", Scalar(captured, "Environment"));
    }

    // ---- Which sinks get built ---------------------------------------------------------------

    [Fact]
    public void TheDefaultConfiguration_NeedsNoConfigurationAtAll()
    {
        // A host handed nothing still has to leave a trace of itself.
        var options = new ObservabilityOptions();

        Assert.True(options.Console.Enabled);
        Assert.True(options.File.Enabled);
        Assert.False(options.Logstash.Enabled);
        Assert.False(options.Loki.Enabled);
    }

    [Fact]
    public void SinkSwitches_ComeFromConfiguration()
    {
        var options = Configuration(new Dictionary<string, string?>
        {
            ["Observability:Console:Enabled"] = "false",
            ["Observability:Loki:Enabled"] = "true",
            ["Observability:Loki:Uri"] = "http://loki:3100",
            ["Observability:Loki:Labels:app"] = "nelsar-axia",
            ["Observability:Logstash:Enabled"] = "true",
            ["Observability:Logstash:Uri"] = "http://logstash:8080"
        }).GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>();

        Assert.NotNull(options);
        Assert.False(options.Console.Enabled);
        Assert.True(options.Loki.Enabled);
        Assert.Equal("http://loki:3100", options.Loki.Uri);
        Assert.Equal("nelsar-axia", options.Loki.Labels["app"]);
        Assert.True(options.Logstash.Enabled);
    }

    [Theory]
    [InlineData("Logstash")]
    [InlineData("Loki")]
    public void AShipperEnabledWithNoAddress_FailsAtStartup(string sink)
    {
        // Rather than at 3am, when the log that was meant to explain the incident turns out to
        // have been going nowhere since the deploy.
        var configuration = Configuration(new Dictionary<string, string?>
        {
            [$"Observability:{sink}:Enabled"] = "true"
        });

        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()!;

        var error = Assert.Throws<InvalidOperationException>(() =>
            SerilogRegistration.Configure(new LoggerConfiguration(), configuration, Environment(), options));

        Assert.Contains($"Observability:{sink}:Uri", error.Message);
    }

    [Fact]
    public void EnabledShippers_Build_WhenGivenAnAddress()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Observability:Console:Enabled"] = "false",
            ["Observability:File:Enabled"] = "false",
            ["Observability:Logstash:Enabled"] = "true",
            ["Observability:Logstash:Uri"] = "http://logstash:8080",
            ["Observability:Loki:Enabled"] = "true",
            ["Observability:Loki:Uri"] = "http://loki:3100",
            ["Observability:Loki:Labels:app"] = "nelsar-axia"
        });

        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()!;

        // Nothing is posted here — the sinks batch, and this disposes before a period elapses.
        // What is under test is that the configuration binds to the sinks' actual signatures.
        using var logger = SerilogRegistration
            .Configure(new LoggerConfiguration(), configuration, Environment(), options)
            .CreateLogger();

        logger.Information("built");
    }

    [Fact]
    public void MinimumLevels_StillComeFromTheStandardSerilogSection()
    {
        // The Observability section owns destinations; levels stay where every Serilog reference
        // says they are, so an operator's existing knowledge keeps working.
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Observability:Console:Enabled"] = "false",
            ["Observability:File:Enabled"] = "false"
        });

        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()!;
        var sink = new CapturingSink();

        using var logger = SerilogRegistration
            .Configure(new LoggerConfiguration(), configuration, Environment(), options)
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("dropped");
        logger.Warning("kept");

        Assert.Equal("kept", Assert.Single(sink.Events).MessageTemplate.Text);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="write"/> through a logger built exactly the way the host builds one —
    /// same enrichers, same redaction — with a capturing sink in place of the real ones.
    /// </summary>
    private static LogEvent WriteAndCapture(Action<Serilog.ILogger> write)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Observability:Console:Enabled"] = "false",
            ["Observability:File:Enabled"] = "false"
        });

        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()!;
        var sink = new CapturingSink();

        using (var logger = SerilogRegistration
                   .Configure(new LoggerConfiguration(), configuration, Environment(), options)
                   .WriteTo.Sink(sink)
                   .CreateLogger())
        {
            write(logger);
        }

        return Assert.Single(sink.Events);
    }

    private static string? Scalar(LogEvent logEvent, string propertyName) =>
        ((ScalarValue)logEvent.Properties[propertyName]).Value?.ToString();

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Environment() => new TestHostEnvironment();

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get { lock (_events) return _events.ToList(); }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events) _events.Add(logEvent);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Nelsar.Axia.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
