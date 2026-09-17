using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for native enrollment of an explicit bounded contact selection.</summary>
[Verb("enroll-sequence-participants", HelpText = "Enroll explicit contacts through Creatio's native sequence service")]
public sealed class SequenceEnrollmentOptions : RemoteCommandOptions {
	/// <summary>Target sequence definition.</summary>
	[Option("sequence-id", Required = true, HelpText = "Target sequence UUID; does not activate the sequence")]
	public Guid SequenceId { get; set; }
	/// <summary>Explicit contacts to submit to native eligibility checks.</summary>
	[Option("contact-ids", Required = true, Separator = ',', HelpText = "Comma-separated contact UUIDs (1–100 unique IDs)")]
	public IEnumerable<Guid> ContactIds { get; set; } = [];
}

/// <summary>Native enrollment result, keeping submission certainty separate from status readback.</summary>
public sealed record SequenceEnrollmentResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("completion")] string Completion,
	[property: JsonPropertyName("platform-success")] bool? PlatformSuccess,
	[property: JsonPropertyName("added-count")] int? AddedCount,
	[property: JsonPropertyName("failed-count")] int? FailedCount,
	[property: JsonPropertyName("errors")] IReadOnlyList<string> Errors,
	[property: JsonPropertyName("readback")] SequenceEnrollmentReadback Readback,
	[property: JsonPropertyName("next-step")] string NextStep) {
	/// <summary>Observed submission boundary and bounded retry context.</summary>
	[JsonPropertyName("diagnostic"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public DataWriteDiagnostic Diagnostic { get; init; }
}

/// <summary>Bounded current participant snapshot; does not attribute records to this submission.</summary>
public sealed record SequenceEnrollmentReadback(
	[property: JsonPropertyName("state")] string State,
	[property: JsonPropertyName("participants")] IReadOnlyList<JsonElement> Participants,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>Submits a selection once and reads its resulting participant statuses.</summary>
public interface ISequenceEnrollmentService {
	/// <summary>Enrolls explicit contacts without activating the sequence or replaying the write.</summary>
	SequenceEnrollmentResult Enroll(Guid sequenceId, IReadOnlyCollection<Guid> contactIds);
}

/// <summary>Shared CLI and MCP entry point for native sequence enrollment.</summary>
public sealed class SequenceEnrollmentCommand(ISequenceEnrollmentService service, ILogger logger)
	: Command<SequenceEnrollmentOptions> {
	/// <summary>Executes native enrollment and returns structured completion and readback.</summary>
	public SequenceEnrollmentResult Enroll(SequenceEnrollmentOptions options) =>
		service.Enroll(options.SequenceId, options.ContactIds?.ToArray() ?? []);
	/// <inheritdoc />
	public override int Execute(SequenceEnrollmentOptions options) {
		try {
			SequenceEnrollmentResult result = Enroll(options);
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return result.Success ? 0 : 1;
		} catch (Exception exception) {
			logger.WriteError(SensitiveErrorTextRedactor.RedactUntrustedOrNull(exception.Message));
			return 1;
		}
	}
}

/// <summary>Uses the native enrollment engine and read-only DataService status inspection.</summary>
public sealed class SequenceEnrollmentService(IApplicationClient client, IServiceUrlBuilder urls)
	: ISequenceEnrollmentService {
	internal const int MaximumContacts = 100;
	private const string CompleteState = "complete";
	private const int MaximumResponseBytes = 200_000;
	private const string ReadbackAdvice = "Added means enrolled, not necessarily Active. Inspect the status snapshot; existing records may predate this call. The native engine can change statuses after this read. Do not repeat enrollment solely because readback failed.";
	private const string UncertainAdvice = "Submission may have committed. No retry was made. Inspect participants and activities before deciding whether to submit any remaining contacts.";

	/// <inheritdoc />
	public SequenceEnrollmentResult Enroll(Guid sequenceId, IReadOnlyCollection<Guid> contactIds) {
		if (sequenceId == Guid.Empty || contactIds is null || contactIds.Count is < 1 or > MaximumContacts
			|| contactIds.Contains(Guid.Empty) || contactIds.Distinct().Count() != contactIds.Count) {
			throw new ArgumentException("Provide a non-empty sequence-id and 1–100 unique, non-empty contact-ids.");
		}
		string body = JsonSerializer.Serialize(new { request = new {
			sequenceId, filter = ContactFilter("Id", contactIds).ToJsonString()
		}});
		bool platformSuccess;
		int added;
		int failed;
		List<string> errors;
		bool responseReceived = false;
		try {
			string response = client.ExecuteNonReplayablePostRequest(
				urls.Build(ServiceUrlBuilder.KnownRoute.SequenceParticipantBulkAdd), body, 30_000, 1, 1);
			responseReceived = true;
			using JsonDocument document = ParseBounded(response);
			JsonElement root = document.RootElement;
			platformSuccess = root.GetProperty("success").GetBoolean();
			added = root.GetProperty("addedCount").GetInt32();
			failed = root.GetProperty("failedCount").GetInt32();
			if (added < 0 || failed < 0 || added > contactIds.Count || failed > contactIds.Count || added + failed > contactIds.Count) {
				throw new InvalidOperationException("Native enrollment returned incompatible counts.");
			}
			errors = ReadErrors(root);
		} catch (Exception exception) {
			return new(false, "uncertain", null, null, null,
				[SensitiveErrorTextRedactor.RedactUntrustedOrNull(exception.Message) ?? "Enrollment response unavailable."],
				ReadParticipants(sequenceId, contactIds), UncertainAdvice) {
				Diagnostic = DataWriteDiagnostic.Create("enroll", "SequenceParticipant", null, true,
					responseReceived, false, exception.Message)
			};
		}
		SequenceEnrollmentReadback readback = ReadParticipants(sequenceId, contactIds);
		if (added > readback.Participants.Count && readback.State == CompleteState) {
			readback = readback with { State = "incomplete", Error = "Fewer visible participants than the native added count; verify permissions and current records before resubmission." };
		}
		return new(platformSuccess && failed == 0 && errors.Count == 0 && readback.State == CompleteState,
			"completed", platformSuccess, added, failed, errors, readback, ReadbackAdvice) {
			Diagnostic = DataWriteDiagnostic.Create("enroll", "SequenceParticipant", null, true, true,
				platformSuccess && failed == 0 && errors.Count == 0, errors.FirstOrDefault())
		};
	}

	private static JsonDocument ParseBounded(string response) {
		if (Encoding.UTF8.GetByteCount(response) > MaximumResponseBytes) {
			throw new InvalidOperationException("Response exceeded the 200000-byte limit.");
		}
		return JsonDocument.Parse(response);
	}

	private static List<string> ReadErrors(JsonElement root) {
		List<string> errors = [];
		if (root.TryGetProperty("errorMessages", out JsonElement messages) && messages.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement message in messages.EnumerateArray().Take(20)) {
				if (message.ValueKind == JsonValueKind.String) {
					errors.Add(SensitiveErrorTextRedactor.RedactUntrustedOrNull(message.GetString()) ?? "Native enrollment error.");
				}
			}
		}
		if (root.TryGetProperty("errorInfo", out JsonElement info) && info.ValueKind == JsonValueKind.Object
			&& info.TryGetProperty("message", out JsonElement detail) && detail.ValueKind == JsonValueKind.String) {
			errors.Add(SensitiveErrorTextRedactor.RedactUntrustedOrNull(detail.GetString()) ?? "Native enrollment error.");
		}
		return errors;
	}

	private static JsonObject ContactFilter(string column, IReadOnlyCollection<Guid> contactIds) {
		object query = SelectQueryHelper.BuildSelectQueryWithOrFilter("Contact", [], column,
			contactIds.Select(id => id.ToString()).ToArray(), 0, MaximumContacts);
		JsonObject filter = JsonSerializer.SerializeToNode(query)["filters"].DeepClone().AsObject();
		filter["rootSchemaName"] = "Contact";
		return filter;
	}

	private SequenceEnrollmentReadback ReadParticipants(Guid sequenceId, IReadOnlyCollection<Guid> contactIds) {
		try {
			object query = SelectQueryHelper.BuildSelectQuery("SequenceParticipant", [
				new("Id", "id"), new("Participant.Id", "contact-id"), new("Status.Id", "status-id"),
				new("Status.Name", "status-name"), new("Stage.Name", "stage-name")
			], [new("Sequence", sequenceId, 0)], rowCount: MaximumContacts + 1);
			JsonNode request = JsonSerializer.SerializeToNode(query);
			JsonObject audience = ContactFilter("Participant", contactIds);
			audience.Remove("rootSchemaName");
			request["filters"]["items"]["audience"] = audience;
			string response = client.ExecutePostRequest(urls.Build(ServiceUrlBuilder.KnownRoute.Select),
				request.ToJsonString(), 10_000, 1, 1);
			using JsonDocument document = ParseBounded(response);
			JsonElement root = document.RootElement;
			if (root.GetProperty("success").ValueKind != JsonValueKind.True) {
				return new("failed", [], "Participant readback failed; check DataService read permissions.");
			}
			JsonElement rows = root.GetProperty("rows");
			JsonElement[] participants = rows.EnumerateArray().Take(MaximumContacts).Select(row => row.Clone()).ToArray();
			if (participants.Any(row => !row.TryGetProperty("id", out _) || !row.TryGetProperty("contact-id", out _)
				|| !row.TryGetProperty("status-id", out _) || !row.TryGetProperty("status-name", out _))) {
				return new("failed", [], "Participant readback omitted required fields.");
			}
			return new(rows.GetArrayLength() > MaximumContacts ? "truncated" : CompleteState, participants);
		} catch (Exception exception) {
			return new("failed", [], SensitiveErrorTextRedactor.RedactUntrustedOrNull(exception.Message));
		}
	}
}
