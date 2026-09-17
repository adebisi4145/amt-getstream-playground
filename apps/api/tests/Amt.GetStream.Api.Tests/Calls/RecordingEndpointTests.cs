using System.Net;
using System.Net.Http.Json;
using Amt.GetStream.Api.Features.Calls;
using Amt.GetStream.Api.Services.Stream;
using Microsoft.Extensions.DependencyInjection;

namespace Amt.GetStream.Api.Tests.Calls;

public sealed class RecordingEndpointTests
{
    [Fact]
    public async Task Start_and_stop_reach_the_service_and_return_204()
    {
        var recordings = new FakeStreamRecordingService();
        using var client = CreateClient(recordings);

        var started = await client.PostAsync("/api/calls/default/team-standup/recordings/start", content: null, TestContext.Current.CancellationToken);
        var stopped = await client.PostAsync("/api/calls/default/team-standup/recordings/stop", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, started.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
        Assert.Equal([("default", "team-standup")], recordings.Started);
        Assert.Equal([("default", "team-standup")], recordings.Stopped);
    }

    [Fact]
    public async Task List_returns_the_recordings()
    {
        var recordings = new FakeStreamRecordingService();
        using var client = CreateClient(recordings);

        var response = await client.GetAsync("/api/calls/default/team-standup/recordings", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CallRecordingResponse[]>(TestContext.Current.CancellationToken);
        var recording = Assert.Single(body!);
        Assert.Equal(FakeStreamRecordingService.Recording.Filename, recording.Filename);
        Assert.Equal("composite", recording.RecordingType);
    }

    [Theory]
    [InlineData("/api/calls/chat/team-standup/recordings/start")]
    [InlineData("/api/calls/default/bad%20id!/recordings/start")]
    public async Task Invalid_route_returns_400_without_calling_stream(string url)
    {
        var recordings = new FakeStreamRecordingService();
        using var client = CreateClient(recordings);

        var response = await client.PostAsync(url, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(recordings.Started);
    }

    private static HttpClient CreateClient(FakeStreamRecordingService recordings) =>
        ApiFactory.Create(configureServices: services => services.AddSingleton<IStreamRecordingService>(recordings))
            .CreateClient();
}
