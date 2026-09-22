using Amt.GetStream.Api.Features.Calls;
using Amt.GetStream.Api.Features.Consultations;
using Amt.GetStream.Api.Features.Tokens;
using Amt.GetStream.Api.Features.Webhooks;
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

// Bound when first used, like the CORS origins, so configuration added by tests is included.
builder.Services
    .AddOptions<CallOptions>()
    .Configure<IConfiguration>((options, configuration) =>
        configuration.GetSection(CallOptions.SectionName).Bind(options))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ConsultationOptions>()
    .Configure<IConfiguration>((options, configuration) =>
        configuration.GetSection(ConsultationOptions.SectionName).Bind(options))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Closes consultations nobody finished, so the board reflects reality.
builder.Services.AddHostedService<StaleConsultationSweeper>();

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
api.MapCallEndpoints();
api.MapRecordingEndpoints();
api.MapConsultationEndpoints();

// Stream posts webhooks server-to-server, so this route stays outside the CORS policy.
app.MapWebhookEndpoints();

app.Run();
