using LP.GatewayAPI.Middlewares;

var builder = WebApplication.CreateBuilder(args);

// Load API Gateway Routes from appsettings.json
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Add services to the container.

builder.Services.AddControllers();
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Add Authentication Middleware before API Gateway Middleware
app.UseMiddleware<AuthMiddleware>();

// Use the API Gateway Middleware
app.UseMiddleware<ApiGatewayMiddleware>();

//app.UseHttpsRedirection();

//app.UseAuthorization();

//app.MapControllers();

app.Run();
