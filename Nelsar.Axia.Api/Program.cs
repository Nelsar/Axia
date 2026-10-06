using System.Threading.RateLimiting;
using Nelsar.Axia.Api.Resources;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Localization;
using Nelsar.Axia.Api.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Nelsar.Axia.Api.Configuration;
using Nelsar.Axia.Api.Controllers;
using Nelsar.Axia.Api.Middleware;
using Nelsar.Axia.Api.Observability;
using Nelsar.Axia.Api.Swagger;
using Nelsar.Axia.Infrastructure;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Infrastructure.RateLimiting;
using Serilog;

// A logger for the window before configuration has been read — long enough to fail in, and
// until now failures there went to whatever the runtime happened to print. AddAxiaObservability
// replaces it with the configured one as soon as there is configuration to read.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Structured logging, and which sinks it reaches: console and a rolling JSON file by default,
    // Logstash and Loki on a switch. See Observability/SerilogRegistration.cs.
    builder.AddAxiaObservability();

    // Self-registration is off unless a deployment turns it on; see AuthOptions.
    builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

    var proxyOptions = builder.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()
        ?? new ReverseProxyOptions();
    var httpsOptions = builder.Configuration.GetSection(HttpsOptions.SectionName).Get<HttpsOptions>()
        ?? new HttpsOptions();

    if (proxyOptions.IsConfigured)
    {
        // Read before the host is built, so a malformed entry stops startup here rather than
        // surfacing as a quietly untrusted proxy once traffic is arriving.
        var knownProxies = proxyOptions.ParseKnownProxies();
        var knownNetworks = proxyOptions.ParseKnownNetworks();

        builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // The defaults trust any loopback proxy. Cleared, because the whole point of naming
            // proxies is that only those are believed — leaving the defaults in place would mean
            // anything reaching us over loopback could rewrite the client address the rate limiter
            // partitions by.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in knownProxies)
                options.KnownProxies.Add(proxy);

            foreach (var network in knownNetworks)
                options.KnownIPNetworks.Add(network);
        });
    }

    // Add Infrastructure Services (Authentication, DI, etc.)
    builder.Services.AddInfrastructureServices(builder.Configuration);

    // Authorisation runs on permissions, resolved per request (see IPermissionResolver). Roles
    // still exist — they are what LDAP groups map to — but no endpoint checks a role name any more.
    builder.Services.AddPermissionAuthorization();

    // Global exception handling: turns any unhandled exception into a generic 500
    // instead of leaking exception.Message to the client. See GlobalExceptionHandler.
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    // Response messages come from .resx (ApiMessages / ApplicationMessages / DomainMessages) and are
    // picked per request from Accept-Language. English is the neutral fallback, so a client that
    // sends nothing, or asks for a language we do not have, still gets a readable message.
    builder.Services.Configure<RequestLocalizationOptions>(options =>
    {
        options.SetDefaultCulture(SupportedCultures.Neutral)
            .AddSupportedCultures(SupportedCultures.All)
            .AddSupportedUICultures(SupportedCultures.All);

        // Only the Accept-Language header decides. The cookie and query-string providers that ship
        // on by default let any caller pin the culture of a shared endpoint through a URL, which is
        // not something an administrative API should offer.
        options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
    });

    // Rate limiting: partitioned per client IP, limits are read from IRateLimitSettingsProvider
    // (DB-backed, cached) each time a new partition/window is created, so an admin can retune
    // via /api/v1/rate-limit-settings without a redeploy or restart.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, token) =>
        {
            context.HttpContext.Response.ContentType = "application/json";
            return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
                new { message = ApiMessages.TooManyRequests() }, token));
        };

        // Two policies, one shape. Factored rather than copied: the difference between them is
        // the name and the limits behind it, and a second hand-written partitioner is how one of
        // them quietly stops reading its settings from the database.
        options.AddPolicy(ProvidersController.SelectableProvidersPolicy,
            httpContext => PerClientAddress(httpContext, ProvidersController.SelectableProvidersPolicy));

        options.AddPolicy("auth-login", httpContext => PerClientAddress(httpContext, "auth-login"));
    });

    // Whether an Origin header names this machine. Used only when no origins are configured, to
    // keep local development working without opening the API to the web.
    static bool IsLoopback(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    // Partitions by client address and reads its limits from IRateLimitSettingsProvider, so an
    // admin retunes either policy through /api/v1/rate-limit-settings without a redeploy.
    static RateLimitPartition<string> PerClientAddress(HttpContext httpContext, string policyName)
    {
        var settings = httpContext.RequestServices
            .GetRequiredService<IRateLimitSettingsProvider>()
            .GetSettings(policyName);

        if (!settings.Enabled)
            return RateLimitPartition.GetNoLimiter<string>($"{policyName}:disabled");

        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"{policyName}:{partitionKey}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.PermitLimit,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            QueueLimit = settings.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    }

    // CORS for the separately-hosted console (frontend/ — its own origin and port). See
    // CorsOptions for why this is a list rather than AllowAnyOrigin.
    const string FrontendCorsPolicy = "Frontend";
    var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
        ?? new CorsOptions();

    // Read before the host is built, for the same reason as the proxy list: an entry that can
    // never match stops startup here instead of surfacing as a console that cannot reach the API.
    var allowedOrigins = corsOptions.ParseAllowedOrigins();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(FrontendCorsPolicy, policy =>
        {
            if (corsOptions.AllowAnyOrigin)
                policy.AllowAnyOrigin();
            else if (allowedOrigins.Count > 0)
                policy.WithOrigins([.. allowedOrigins]);
            else
                // Nothing configured: the console talking to a locally-run API, on whichever port
                // Vite picked. A predicate rather than a fixed list because the port moves and an
                // operator should not have to edit configuration to run the thing locally.
                policy.SetIsOriginAllowed(IsLoopback);

            policy.AllowAnyHeader()
                .AllowAnyMethod()
                // Response headers are hidden from browser script unless named here. Without it
                // the frontend cannot read the correlation id off a failed call, and the id stops
                // being something a user can quote when they report the failure.
                .WithExposedHeaders("X-Correlation-ID")
                // Every console call carries Authorization, so every one is preflighted. Without
                // a max age the browser keeps a preflight for about five seconds and nearly every
                // request went out twice.
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        });
    });

    // Add Controllers
    builder.Services.AddControllers();

    // Add API documentation
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT authorization using Bearer scheme. Example: Bearer {token}"
        });

        options.OperationFilter<AuthorizeOperationFilter>();
        options.OperationFilter<ProviderHeaderOperationFilter>();

        // Surfaces XML /// <summary> comments in Swagger UI. Added for the EXPERIMENTAL/STUB markers
        // on GroupsController/OrganizationalUnitsController; both are implemented now and the markers
        // are gone, but the rest of the controller docs are worth showing on their own.
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        }
    });

    var app = builder.Build();

    if (!app.Environment.IsEnvironment("Testing"))
    {
        await DbInitializer.InitializeAsync(app.Services);
    }

    // Configure the HTTP request pipeline

    // Before anything that reads the client address or the scheme — which is the rate limiter,
    // the request log and the redirect below. Registered only when a proxy has been named; see
    // ReverseProxyOptions for why an unnamed proxy is not trusted.
    if (proxyOptions.KnownProxies.Length > 0 || proxyOptions.KnownNetworks.Length > 0)
        app.UseForwardedHeaders();

    // Correlation id and request logging, both outside the exception handler. The id is ambient
    // for the rest of the pipeline, and GlobalExceptionHandler logs from inside that scope, so
    // the log line explaining a 500 carries the same id the caller was handed back.
    app.UseAxiaObservability();

    app.UseExceptionHandler();

    // Must precede anything that writes a message a client will read — that includes the rate
    // limiter's OnRejected below, not just the controllers.
    //
    // It sits after UseExceptionHandler so that a failure in localisation itself is still turned
    // into a clean 500. The culture this sets does not flow back out to GlobalExceptionHandler,
    // which runs after the exception has unwound past here. Messages carried by an exception were
    // resolved where it was thrown, inside the request, so they are localised regardless; the one
    // message the handler resolves itself is looked up in the request's culture explicitly. Both
    // are verified rather than assumed — see ErrorBody_IsLocalisedFromAcceptLanguage and
    // UnexpectedErrorBody_IsLocalisedFromAcceptLanguage in UsersControllerIntegrationTests.
    app.UseRequestLocalization();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    if (!app.Environment.IsDevelopment())
    {
        // Harmless where TLS is terminated upstream — a browser ignores Strict-Transport-Security
        // arriving over plain HTTP — and the right header to be sending everywhere else.
        app.UseHsts();
    }

    if (httpsOptions.RedirectToHttps)
        app.UseHttpsRedirection();

    app.UseCors(FrontendCorsPolicy);
    app.UseRateLimiter();
    app.UseAuthentication();
    // Capture optional provider header and expose it via HttpContext.Items["LdapProviderId"]
    app.UseMiddleware<ProviderHeaderMiddleware>();
    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Startup failures used to leave nothing behind but a console line on a machine nobody was
    // watching. This one reaches every configured sink.
    Log.Fatal(ex, "Nelsar.Axia.Api terminated unexpectedly.");
    throw;
}
finally
{
    // The Logstash and Loki sinks batch, so whatever is still queued at shutdown is only written
    // because of this. Without it, the last events before a crash — the ones worth having — are
    // the ones lost.
    await Log.CloseAndFlushAsync();
}
