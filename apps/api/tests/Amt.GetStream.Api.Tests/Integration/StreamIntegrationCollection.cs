namespace Amt.GetStream.Api.Tests.Integration;

/// <summary>
/// Stream integration tests share one real app and its rate limits, so they run one at a time.
/// Running them in parallel got DeleteUsers rate limited during cleanup.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StreamIntegrationCollection
{
    public const string Name = "stream-integration";
}
