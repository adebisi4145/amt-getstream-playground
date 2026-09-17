using Amt.GetStream.Api.Services.Stream;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Amt.GetStream.Api.Tests;

/// <summary>
/// Hosts the API in memory with test Stream credentials. Settings are added last, so they override appsettings files.
/// </summary>
internal static class ApiFactory
{
    public const string TestApiKey = "test-api-key";
    public const string TestApiSecret = "test-api-secret-that-is-long-enough-for-hmac-sha256";

    public static WebApplicationFactory<Program> Create(
        IStreamUserService? userService = null,
        IDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Stream:ApiKey"] = TestApiKey,
            ["Stream:ApiSecret"] = TestApiSecret,
        };

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            configuration[key] = value;
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));

            builder.ConfigureTestServices(services =>
            {
                if (userService is not null)
                {
                    services.AddSingleton(userService);
                }

                configureServices?.Invoke(services);
            });
        });
    }
}
