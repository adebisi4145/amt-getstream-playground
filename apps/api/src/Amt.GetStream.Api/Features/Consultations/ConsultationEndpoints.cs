using Amt.GetStream.Api.Services.Stream;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Amt.GetStream.Api.Features.Consultations;

/// <summary>
/// The consultation queue: a patient starts a call, triage picks it up from the board, and triage can
/// invite a doctor into the running call.
///
/// Identity is faked here — ids come from the request body and prove nothing. The permission and state
/// rules are still enforced, so the playground shows the real shape of the workflow.
/// </summary>
public static class ConsultationEndpoints
{
    public static IEndpointRouteBuilder MapConsultationEndpoints(this IEndpointRouteBuilder api)
    {
        var consultations = api.MapGroup("/consultations").WithTags("Consultations");

        consultations.MapPost("/", StartAsync)
            .WithName("StartConsultation")
            .WithSummary("Patient starts a consultation and waits for triage")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        consultations.MapGet("/", BoardAsync)
            .WithName("ListConsultations")
            .WithSummary("Dispatch board: consultations by status, waiting by default")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway);

        consultations.MapGet("/{callId}", GetAsync)
            .WithName("GetConsultation")
            .WithSummary("One consultation")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        consultations.MapPost("/{callId}/accept", AcceptAsync)
            .WithName("AcceptConsultation")
            .WithSummary("Triage takes a waiting consultation")
            .WithDescription("Triage only. Accepting again as the same agent succeeds; another agent gets 409.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        consultations.MapPost("/{callId}/invite", InviteAsync)
            .WithName("InviteDoctor")
            .WithSummary("Bring a doctor into an accepted consultation")
            .WithDescription("Adds the doctor as a member, then rings them. A failed ring leaves the doctor added; retry with /ring.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        consultations.MapPost("/{callId}/ring", RingAsync)
            .WithName("RingDoctor")
            .WithSummary("Ring an already-invited doctor again")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        consultations.MapPost("/{callId}/complete", CompleteAsync)
            .WithName("CompleteConsultation")
            .WithSummary("Staff on the call finish it")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        consultations.MapPost("/{callId}/cancel", CancelAsync)
            .WithName("CancelConsultation")
            .WithSummary("Patient gives up waiting")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return api;
    }

    private static async Task<Ok<ConsultationResponse>> StartAsync(
        StartConsultationRequest request,
        IStreamUserService users,
        IStreamConsultationService consultations,
        IOptions<ConsultationOptions> options,
        CancellationToken cancellationToken)
    {
        var patientId = request.PatientId!;

        // The patient may be brand new, and Stream needs the user before they can join.
        await users.EnsureUserAsync(patientId, name: null, image: null, cancellationToken);

        var consultation = await consultations.CreateAsync(
            options.Value.CallType,
            Guid.NewGuid().ToString("N"),
            patientId,
            request.Reason,
            cancellationToken);

        return TypedResults.Ok(ToResponse(consultation));
    }

    private static async Task<Results<Ok<IReadOnlyList<ConsultationResponse>>, ValidationProblem>> BoardAsync(
        IStreamConsultationService consultations,
        IOptions<ConsultationOptions> options,
        CancellationToken cancellationToken,
        string status = ConsultationStatus.Waiting,
        int limit = 25)
    {
        string[] allowed =
            [ConsultationStatus.Waiting, ConsultationStatus.Accepted, ConsultationStatus.Completed, ConsultationStatus.Cancelled];

        if (!allowed.Contains(status))
        {
            return Invalid("status", $"The status field must be one of: {string.Join(", ", allowed)}.");
        }

        if (limit is < 1 or > 100)
        {
            return Invalid("limit", "The limit field must be between 1 and 100.");
        }

        var found = await consultations.QueryByStatusAsync(options.Value.CallType, status, limit, cancellationToken);

        IReadOnlyList<ConsultationResponse> response = [.. found.Select(ToResponse)];

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<ConsultationResponse>, NotFound>> GetAsync(
        string callId,
        IStreamConsultationService consultations,
        IOptions<ConsultationOptions> options,
        CancellationToken cancellationToken)
    {
        var consultation = await consultations.GetAsync(options.Value.CallType, callId, cancellationToken);

        return consultation is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(consultation));
    }

    private static async Task<Results<Ok<ConsultationResponse>, NotFound, ProblemHttpResult, ValidationProblem>> AcceptAsync(
        string callId,
        StaffActionRequest request,
        IStreamUserService users,
        IStreamConsultationService consultations,
        IOptions<ConsultationOptions> options,
        CancellationToken cancellationToken)
    {
        var staffId = request.StaffId!;
        var directory = new StaffDirectory(options.Value);

        // Triage only: doctors join through an invite, not from the board.
        if (!directory.IsTriage(staffId))
        {
            return Forbidden($"'{staffId}' is not a triage agent.");
        }

        var consultation = await consultations.GetAsync(options.Value.CallType, callId, cancellationToken);
        if (consultation is null)
        {
            return TypedResults.NotFound();
        }

        // Re-accepting your own consultation is a double click or a retry, not a conflict.
        if (consultation.Status == ConsultationStatus.Accepted && consultation.AssignedTo == staffId)
        {
            return TypedResults.Ok(ToResponse(consultation));
        }

        if (consultation.Status != ConsultationStatus.Waiting)
        {
            return StatusConflict(consultation, "accepted");
        }

        await users.EnsureUserAsync(staffId, name: null, image: null, cancellationToken);
        var accepted = await consultations.AssignAsync(options.Value.CallType, callId, staffId, cancellationToken);

        return TypedResults.Ok(ToResponse(accepted));
    }

    private static async Task<Results<Ok<ConsultationResponse>, NotFound, ProblemHttpResult, ValidationProblem>> InviteAsync(
        string callId,
        DoctorActionRequest request,
        IStreamUserService users,
        IStreamConsultationService consultations,
        IOptions<ConsultationOptions> options,
        CancellationToken cancellationToken)
    {
        var doctorId = request.DoctorId!;

        if (!new StaffDirectory(options.Value).IsDoctor(doctorId))
        {
            return Forbidden($"'{doctorId}' is not a doctor.");
        }

        var consultation = await consultations.GetAsync(options.Value.CallType, callId, cancellationToken);
        if (consultation is null)
        {
            return TypedResults.NotFound();
        }

        if (consultation.Status != ConsultationStatus.Accepted)
        {
            return StatusConflict(consultation, "have a doctor invited");
        }

        await users.EnsureUserAsync(doctorId, name: null, image: null, cancellationToken);
        var withDoctor = await consultations.AddDoctorAsync(options.Value.CallType, callId, doctorId, cancellationToken);

        // Ringing is best-effort: if it fails the doctor stays a member and /ring can retry,
        // so a notification failure never undoes a correct membership change.
        await consultations.RingAsync(options.Value.CallType, callId, doctorId, cancellationToken);

        return TypedResults.Ok(ToResponse(withDoctor));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult, ValidationProblem>> RingAsync(
        string callId,
        DoctorActionRequest request,
        IStreamConsultationService consultations,
        IOptions<ConsultationOptions> options,
        CancellationToken cancellationToken)
    {
        var doctorId = request.DoctorId!;

        if (!new StaffDirectory(options.Value).IsDoctor(doctorId))
        {
            return Forbidden($"'{doctorId}' is not a doctor.");
        }

        var consultation = await consultations.GetAsync(options.Value.CallType, callId, cancellationToken);
        if (consultation is null)
        {
            return TypedResults.NotFound();
        }

        if (consultation.Status != ConsultationStatus.Accepted)
        {
            return StatusConflict(consultation, "ring a doctor");
        }

        // Without this, /ring could ring any configured doctor about a consultation they were never invited to.
        if (!consultation.MemberIds.Contains(doctorId))
        {
            return Forbidden($"'{doctorId}' has not been invited to this consultation.");
        }

        await consultations.RingAsync(options.Value.CallType, callId, doctorId, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ConsultationResponse>, NotFound, ProblemHttpResult, ValidationProblem>> CompleteAsync(
        string callId,
        StaffActionRequest request,
        IStreamConsultationService consultations,
        IOptions<ConsultationOptions> options,
        CancellationToken cancellationToken)
    {
        var staffId = request.StaffId!;
        var directory = new StaffDirectory(options.Value);

        if (!directory.IsTriage(staffId) && !directory.IsDoctor(staffId))
        {
            return Forbidden($"'{staffId}' is not staff.");
        }

        var consultation = await consultations.GetAsync(options.Value.CallType, callId, cancellationToken);
        if (consultation is null)
        {
            return TypedResults.NotFound();
        }

        // Any staff on this consultation may close it: triage often leaves after handing over to the doctor.
        if (!consultation.MemberIds.Contains(staffId))
        {
            return Forbidden($"'{staffId}' is not on this consultation.");
        }

        if (consultation.Status != ConsultationStatus.Accepted)
        {
            return StatusConflict(consultation, "completed");
        }

        var completed = await consultations.CloseAsync(
            options.Value.CallType, callId, ConsultationStatus.Completed, cancellationToken);

        return TypedResults.Ok(ToResponse(completed));
    }

    private static async Task<Results<Ok<ConsultationResponse>, NotFound, ProblemHttpResult, ValidationProblem>> CancelAsync(
        string callId,
        PatientActionRequest request,
        IStreamConsultationService consultations,
        IOptions<ConsultationOptions> options,
        CancellationToken cancellationToken)
    {
        var patientId = request.PatientId!;

        var consultation = await consultations.GetAsync(options.Value.CallType, callId, cancellationToken);
        if (consultation is null)
        {
            return TypedResults.NotFound();
        }

        // One patient must not be able to cancel another patient's consultation.
        if (consultation.PatientId != patientId)
        {
            return Forbidden("This consultation belongs to another patient.");
        }

        if (consultation.Status != ConsultationStatus.Waiting)
        {
            return StatusConflict(consultation, "cancelled");
        }

        var cancelled = await consultations.CloseAsync(
            options.Value.CallType, callId, ConsultationStatus.Cancelled, cancellationToken);

        return TypedResults.Ok(ToResponse(cancelled));
    }

    private static ProblemHttpResult Forbidden(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status403Forbidden, title: "Not allowed");

    private static ProblemHttpResult StatusConflict(Consultation consultation, string action) =>
        TypedResults.Problem(
            $"A consultation with status '{consultation.Status}' cannot be {action}.",
            statusCode: StatusCodes.Status409Conflict,
            title: "Consultation is in the wrong state");

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static ConsultationResponse ToResponse(Consultation consultation) =>
        new(
            consultation.CallType,
            consultation.CallId,
            consultation.Cid,
            consultation.PatientId,
            consultation.Reason,
            consultation.Status,
            consultation.AssignedTo,
            consultation.RequestedAt,
            consultation.AcceptedAt,
            consultation.EndedAt,
            consultation.MemberIds);
}
