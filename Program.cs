using LP.GatewayAPI.Logging;
using LP.GatewayAPI.Middlewares;
using LP.GatewayAPI.Utilities;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration sections
builder.Services.Configure<CryptographyOptions>(builder.Configuration.GetSection("Cryptography"));
builder.Services.Configure<APILoggerOptions>(builder.Configuration.GetSection("Logging:Options"));

// RouteVersions/*.json (one file per app) is the gateway's only source of routing data (no
// separate routes.json), keyed per calling app. Loaded and watched directly by
// RouteVersionsRepository, not through IConfiguration.
builder.Services.AddSingleton<RouteVersionsRepository>();
builder.Services.AddSingleton<RouteVersionResolver>();

var gatewayOptions = builder.Configuration.GetSection("Gateway").Get<GatewayOptions>() ?? new GatewayOptions();

// Application services
builder.Services.AddSingleton<Cryptography>();
builder.Services.AddSingleton<IAPILogger, APILogger>();

// Health checks
builder.Services.AddHealthChecks();

// Rate limiting — fixed window per client IP
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 20
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// HTTP client — configurable SSL, explicit timeout, circuit breaker
builder.Services.AddHttpClient("HttpClientWithSSLUntrusted")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (gatewayOptions.DisableSslValidation)
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        return handler;
    })
    .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan) // resilience handler manages timeouts
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(gatewayOptions.RequestTimeoutSeconds);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Middleware pipeline (order matters)
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<AuthMiddleware>();
app.UseMiddleware<ApiGatewayMiddleware>();

app.MapHealthChecks("/health");

app.Run();
