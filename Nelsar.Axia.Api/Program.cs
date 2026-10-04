using System.Threading.RateLimiting;
using Nelsar.Axia.Api.Resources;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Localization;
using Nelsar.Axia.Api.Authorization;
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

    // CORS for the separately-hosted frontend (frontend/ — static files, its own origin/port).
    // Auth is Bearer-token-based (Authorization header, no cookies), so AllowCredentials is not
    // needed and AllowAnyOrigin is safe here.
    const string FrontendCorsPolicy = "Frontend";
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(FrontendCorsPolicy, policy =>
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod()
                // Response headers are hidden from browser script unless named here. Without it
                // the frontend cannot read the correlation id off a failed call, and the id stops
                // being something a user can quote when they report the failure.
                .WithExposedHeaders("X-Correlation-ID");
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

        options.AddSecurityDefinition("Basic", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "basic",
            In = ParameterLocation.Header,
            Description = "LDAP authorization using Basic scheme. Example: Basic base64(username:password)"
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

    // Correlation id and request logging, both outside the exception handler. The id is ambient
    // for the rest of the pipeline, and GlobalExceptionHandler logs from inside that scope, so
    // the log line explaining a 500 carries the same id the caller was handed back.
    app.UseAxiaObservability();

    app.UseExceptionHandler();

    // Must precede anything that writes a message a client will read — that includes the rate
    // limiter's OnRejected below, not just the controllers.
    //
    // It sits after UseExceptionHandler so that a failure in localisation itself is still turned
    // into a clean 500. Bodies written by GlobalExceptionHandler are localised all the same, even
    // though it runs while an exception unwinds back past this middleware: the request culture is
    // scoped to the request, not to the frame that set it. That is verified rather than assumed —
    // see UsersControllerIntegrationTests.ErrorBody_IsLocalisedFromAcceptLanguage, which fails if
    // this stops holding.
    app.UseRequestLocalization();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    //app.UseHttpsRedirection();

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
