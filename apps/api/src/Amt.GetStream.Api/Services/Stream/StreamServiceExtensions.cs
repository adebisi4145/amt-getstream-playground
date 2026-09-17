using GetStream;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SdkStreamOptions = GetStream.StreamOptions;

namespace Amt.GetStream.Api.Services.Stream;

public static class StreamServiceExtensions
{
    public static IServiceCollection AddStream(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<StreamOptions>()
            .Bind(configuration.GetSection(StreamOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // getstream-net builds its own connection pool per client instance,
        // so the whole app shares one StreamClient and the SDK manages HTTP.
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<StreamOptions>>().Value;

            return new StreamClient(new SdkStreamOptions
            {
                ApiKey = options.ApiKey,
                ApiSecret = options.ApiSecret,
                Logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("GetStream"),
            });
        });

        services.AddSingleton(serviceProvider => new VideoClient(serviceProvider.GetRequiredService<StreamClient>()));

        services.AddSingleton<IStreamUserService, StreamUserService>();
        services.AddSingleton<IStreamCallService, StreamCallService>();
        services.AddSingleton<IStreamRecordingService, StreamRecordingService>();
        services.AddSingleton<IStreamWebhookVerifier, StreamWebhookVerifier>();
        services.AddSingleton<IStreamConsultationService, StreamConsultationService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
