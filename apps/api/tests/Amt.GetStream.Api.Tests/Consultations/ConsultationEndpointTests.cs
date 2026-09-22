using System.Net;
using System.Net.Http.Json;
using Amt.GetStream.Api.Features.Consultations;
using Amt.GetStream.Api.Services.Stream;
using Amt.GetStream.Api.Tests.Tokens;
using Microsoft.Extensions.DependencyInjection;

namespace Amt.GetStream.Api.Tests.Consultations;

public sealed class ConsultationEndpointTests
{
    private const string Triage = "triage-001";
    private const string OtherTriage = "triage-002";
    private const string Doctor = "doctor-001";
    private const string Patient = FakeStreamConsultationService.PatientId;

    [Theory]
    [InlineData(ConsultationModality.Audio)]
    [InlineData(ConsultationModality.Video)]
    public async Task Patient_starts_a_consultation_and_it_waits(string modality)
    {
        var consultations = new FakeStreamConsultationService();
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations",
            new { patientId = Patient, patientName = "Daniel Okafor", modality, reason = "sore throat" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Modality and name must reach Stream: triage reads both off the board, and shows the
        // name rather than the id.
        var created = Assert.Single(consultations.Created);
        Assert.Equal(Patient, created.PatientId);
        Assert.Equal("Daniel Okafor", created.PatientName);
        Assert.Equal(modality, created.Modality);

        var body = await response.Content.ReadFromJsonAsync<ConsultationResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ConsultationStatus.Waiting, body.Status);
        Assert.Equal(Patient, body.PatientId);
        Assert.Equal("Daniel Okafor", body.PatientName);
        Assert.Equal(modality, body.Modality);
        Assert.Equal("sore throat", body.Reason);
        Assert.Null(body.AssignedTo);
    }

    [Theory]
    [InlineData("""{ "patientId": "patient-001" }""")]
    [InlineData("""{ "patientId": "patient-001", "modality": "" }""")]
    [InlineData("""{ "patientId": "patient-001", "modality": "chat" }""")]
    [InlineData("""{ "patientId": "patient-001", "modality": "Video" }""")]
    public async Task Missing_or_unknown_modality_returns_400(string json)
    {
        var consultations = new FakeStreamConsultationService();
        using var client = CreateClient(consultations);

        var response = await client.PostAsync(
            "/api/consultations",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(consultations.Created);
    }

    [Fact]
    public async Task Board_can_be_filtered_to_one_patient()
    {
        // The patient screen uses this to find the consultation it dropped out of.
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("mine", ConsultationStatus.Accepted, Triage, patientId: Patient);
        consultations.Seed("theirs", ConsultationStatus.Accepted, Triage, patientId: "patient-999");
        using var client = CreateClient(consultations);

        var response = await client.GetAsync(
            $"/api/consultations?status=accepted&patientId={Patient}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsultationResponse[]>(TestContext.Current.CancellationToken);
        Assert.Equal("mine", Assert.Single(body!).CallId);

        // The filter must reach Stream, not be applied after fetching everyone's consultations.
        Assert.Equal([("accepted", Patient)], consultations.Queries);
    }

    [Fact]
    public async Task Board_shows_waiting_consultations_only()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("waiting-1", ConsultationStatus.Waiting);
        consultations.Seed("accepted-1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.GetAsync("/api/consultations", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var board = await response.Content.ReadFromJsonAsync<ConsultationResponse[]>(TestContext.Current.CancellationToken);
        Assert.Equal("waiting-1", Assert.Single(board!).CallId);
    }

    [Fact]
    public async Task Triage_accepts_a_waiting_consultation()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Waiting);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/accept", new { staffId = Triage }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([("c1", Triage)], consultations.Assigned);

        var body = await response.Content.ReadFromJsonAsync<ConsultationResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(ConsultationStatus.Accepted, body!.Status);
        Assert.Equal(Triage, body.AssignedTo);
    }

    [Theory]
    [InlineData(Patient)]        // patients can't take consultations
    [InlineData(Doctor)]         // doctors join by invite, not from the board
    [InlineData("stranger-001")] // not staff at all
    public async Task Only_triage_can_accept(string staffId)
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Waiting);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/accept", new { staffId }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(consultations.Assigned);
    }

    [Fact]
    public async Task Accepting_again_as_the_same_agent_succeeds()
    {
        // A double click or a retry after a dropped connection must not fail the agent who owns it.
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/accept", new { staffId = Triage }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(consultations.Assigned);
    }

    [Fact]
    public async Task Accepting_someone_elses_consultation_is_a_conflict()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/accept", new { staffId = OtherTriage }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(consultations.Assigned);
    }

    [Theory]
    [InlineData(ConsultationStatus.Completed)]
    [InlineData(ConsultationStatus.Cancelled)]
    public async Task Closed_consultations_cannot_be_accepted(string status)
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", status, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/accept", new { staffId = Triage }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Inviting_a_doctor_adds_them_and_rings_them()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/invite", new { doctorId = Doctor }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([("c1", Doctor)], consultations.DoctorsAdded);
        Assert.Equal([("c1", Doctor)], consultations.Rung);
    }

    [Theory]
    [InlineData(Triage)]
    [InlineData("stranger-001")]
    public async Task Only_doctors_can_be_invited(string doctorId)
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/invite", new { doctorId }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(consultations.DoctorsAdded);
    }

    [Fact]
    public async Task A_waiting_consultation_cannot_have_a_doctor_invited()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Waiting);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/invite", new { doctorId = Doctor }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(consultations.DoctorsAdded);
    }

    [Fact]
    public async Task A_failed_ring_leaves_the_doctor_added()
    {
        // Membership is the durable intent; ringing is a notification. A failed ring must not undo the add.
        var consultations = new FakeStreamConsultationService
        {
            RingFailure = new StreamRequestFailedException("RingAsync", 500, new Exception("boom")),
        };
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/invite", new { doctorId = Doctor }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal([("c1", Doctor)], consultations.DoctorsAdded);
    }

    [Fact]
    public async Task Ringing_a_doctor_who_was_never_invited_is_refused()
    {
        // Otherwise /ring becomes a way to ring any configured doctor about a call they aren't part of.
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/ring", new { doctorId = Doctor }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(consultations.Rung);
    }

    [Fact]
    public async Task Ringing_an_invited_doctor_again_is_allowed()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage, members: [Patient, Triage, Doctor]);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/ring", new { doctorId = Doctor }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([("c1", Doctor)], consultations.Rung);
    }

    [Fact]
    public async Task The_patient_can_cancel_while_waiting()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Waiting);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/cancel", new { patientId = Patient }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [("c1", ConsultationStatus.Cancelled, ConsultationEndReason.PatientCancelled)],
            consultations.Closed);
    }

    [Fact]
    public async Task Another_patient_cannot_cancel_this_consultation()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Waiting);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/cancel", new { patientId = "patient-999" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(consultations.Closed);
    }

    [Fact]
    public async Task An_accepted_consultation_cannot_be_cancelled_by_the_patient()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/cancel", new { patientId = Patient }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(consultations.Closed);
    }

    [Theory]
    [InlineData(Triage)]  // the assigned agent
    [InlineData(Doctor)]  // the invited doctor, after triage has left
    public async Task Staff_on_the_consultation_can_complete_it(string staffId)
    {
        var consultations = new FakeStreamConsultationService { PatientPresent = true };
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage, members: [Patient, Triage, Doctor]);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/complete", new { staffId }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([("c1", ConsultationStatus.Completed, ConsultationEndReason.Finished)], consultations.Closed);
    }

    [Theory]
    [InlineData(true, ConsultationEndReason.Finished)]
    [InlineData(false, ConsultationEndReason.PatientLeft)]
    public async Task Completing_records_whether_the_patient_was_still_there(
        bool patientPresent, string expectedReason)
    {
        // "Completed" alone can't tell a finished consultation from one where the patient dropped
        // and staff closed it. The reason comes from Stream's session, not from the browser.
        var consultations = new FakeStreamConsultationService { PatientPresent = patientPresent };
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage, members: [Patient, Triage]);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/complete", new { staffId = Triage }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([("c1", ConsultationStatus.Completed, expectedReason)], consultations.Closed);

        var body = await response.Content.ReadFromJsonAsync<ConsultationResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(expectedReason, body!.EndReason);
    }

    [Fact]
    public async Task Cancelling_records_that_the_patient_gave_up()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Waiting);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/cancel", new { patientId = Patient }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [("c1", ConsultationStatus.Cancelled, ConsultationEndReason.PatientCancelled)],
            consultations.Closed);
    }

    [Fact]
    public async Task Staff_not_on_the_consultation_cannot_complete_it()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Accepted, Triage);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/complete", new { staffId = OtherTriage }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(consultations.Closed);
    }

    [Fact]
    public async Task A_waiting_consultation_cannot_be_completed()
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Waiting, members: [Patient, Triage]);
        using var client = CreateClient(consultations);

        var response = await client.PostAsJsonAsync(
            "/api/consultations/c1/complete", new { staffId = Triage }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(consultations.Closed);
    }

    [Fact]
    public async Task Unknown_consultation_returns_404()
    {
        using var client = CreateClient(new FakeStreamConsultationService());

        var response = await client.PostAsJsonAsync(
            "/api/consultations/missing/accept", new { staffId = Triage }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/consultations", """{ "patientId": "bad id!", "modality": "video" }""")]
    [InlineData("/api/consultations/c1/accept", """{ "staffId": "" }""")]
    [InlineData("/api/consultations/c1/invite", """{}""")]
    public async Task Invalid_input_returns_400(string url, string json)
    {
        var consultations = new FakeStreamConsultationService();
        consultations.Seed("c1", ConsultationStatus.Waiting);
        using var client = CreateClient(consultations);

        var response = await client.PostAsync(
            url,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient CreateClient(FakeStreamConsultationService consultations) =>
        ApiFactory.Create(
            userService: new FakeStreamUserService(),
            settings: new Dictionary<string, string?>
            {
                ["Consultations:CallType"] = "development",
                ["Consultations:Staff:0:userId"] = Triage,
                ["Consultations:Staff:0:role"] = "triage",
                ["Consultations:Staff:1:userId"] = OtherTriage,
                ["Consultations:Staff:1:role"] = "triage",
                ["Consultations:Staff:2:userId"] = Doctor,
                ["Consultations:Staff:2:role"] = "doctor",
            },
            configureServices: services => services.AddSingleton<IStreamConsultationService>(consultations))
            .CreateClient();
}
