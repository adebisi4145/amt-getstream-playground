using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Amt.GetStream.Api.Tests.Integration;

/// <summary>
/// Hosts the API with the machine's real Stream credentials, taken from the API project's user secrets
/// or the Stream__* environment variables, so the secret never has to be typed into a shell or a test.
/// <see cref="TryCreate"/> returns null when no secret is configured, which is how integration tests skip.
/// </summary>
internal sealed class StreamIntegrationHost : IAsyncDisposable
{
    public const string SkipReason =
        "Set Stream:ApiSecret (user secrets) or Stream__ApiSecret to run Stream integration tests.";

    private static readonly IConfiguration LocalConfiguration = new ConfigurationBuilder()
        .AddUserSecrets<Program>(optional: true)
        .AddEnvironmentVariables()
        .Build();

    private readonly WebApplicationFactory<Program> _factory;

    private StreamIntegrationHost(WebApplicationFactory<Program> factory) => _factory = factory;

    public IServiceProvider Services => _factory.Services;

    public static StreamIntegrationHost? TryCreate()
    {
        if (string.IsNullOrEmpty(LocalConfiguration["Stream:ApiSecret"]))
        {
            return null;
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                LocalConfiguration.AsEnumerable().Where(pair => pair.Key.StartsWith("Stream:", StringComparison.Ordinal))));
        });

        return new StreamIntegrationHost(factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
