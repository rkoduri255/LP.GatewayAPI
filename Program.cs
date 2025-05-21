using LP.GatewayAPI.Logging;
using LP.GatewayAPI.Middlewares;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<APILoggerOptions>(builder.Configuration.GetSection("Logging:Options"));
builder.Services.AddSingleton<IAPILogger, APILogger>();
// Add an http client to bypass SSL validation issues.
builder.Services.AddHttpClient("HttpClientWithSSLUntrusted").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler
    {
        ClientCertificateOptions = ClientCertificateOption.Manual,
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Add an http client to bypass SSL validation issues.
builder.Services.AddHttpClient("HttpClientWithSSLUntrusted").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler
    {
        ClientCertificateOptions = ClientCertificateOption.Manual,
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Register the global error handler first
app.UseMiddleware<ErrorHandlingMiddleware>();

// Add Authentication Middleware before API Gateway Middleware
app.UseMiddleware<AuthMiddleware>();

// Use the API Gateway Middleware
app.UseMiddleware<ApiGatewayMiddleware>();

app.Run();
