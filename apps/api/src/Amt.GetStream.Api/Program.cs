using Amt.GetStream.Api.Features.Tokens;
using Amt.GetStream.Api.Services.Stream;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Scalar.AspNetCore;

const string WebCorsPolicy = "Web";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<StreamExceptionHandler>();
builder.Services.AddValidation();
builder.Services.AddStream(builder.Configuration);

// Origins are read when the options are first used, so configuration added by tests is included.
builder.Services.AddCors();
builder.Services
    .AddOptions<CorsOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        options.AddPolicy(WebCorsPolicy, policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod());
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

var api = app.MapGroup("/api").RequireCors(WebCorsPolicy);
api.MapTokenEndpoints(app.Configuration);

app.Run();
