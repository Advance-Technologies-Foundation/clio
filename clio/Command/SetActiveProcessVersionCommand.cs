using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Options for making one member of a process version family the ACTUAL one, via the ProcessDesignService
/// package. Consumed by the MCP <c>set-active-business-process-version</c> tool, which sets these properties
/// directly.
/// </summary>
// Same missing-operation floor as ModifyProcessAsNewVersion, and for the same reason: SetActiveProcessVersion
// first exists in the 1.6.1.0 archive, so an environment on any earlier package answers this route with a 404
// rather than a contract error. The literal is what turns that into "your package is behind"; presence-only
// would let the call through to a transport fault that names nothing.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.6.1.0",
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class SetActiveProcessVersionOptions : EnvironmentOptions {
	/// <summary>Schema name (code) of the VERSION to activate. Provide exactly one of <see cref="VersionName"/> or <see cref="VersionUid"/>.</summary>
	public string VersionName { get; set; } = string.Empty;

	/// <summary>Schema UId of the VERSION to activate. Provide exactly one of <see cref="VersionName"/> or <see cref="VersionUid"/>.</summary>
	public string VersionUid { get; set; } = string.Empty;
}

/// <summary>
/// Makes one member of a process version family the actual one via the ProcessDesignService package.
/// </summary>
public interface ISetActiveProcessVersionService {
	/// <summary>
	/// Activates the named version and reports the state read back from the server AFTER the write.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Identity of the version to activate.</param>
	/// <returns>The active version as the server read it back, plus any notices the write raised.</returns>
	SetActiveProcessVersionResult SetActiveVersion(string environmentName, SetActiveProcessVersionRequest request);
}

/// <summary>
/// Default ProcessDesignService-backed implementation of <see cref="ISetActiveProcessVersionService"/>.
/// </summary>
public sealed class SetActiveProcessVersionService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: ISetActiveProcessVersionService {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public SetActiveProcessVersionResult SetActiveVersion(string environmentName,
		SetActiveProcessVersionRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.VersionName) && string.IsNullOrWhiteSpace(request.VersionUid)) {
			throw new ArgumentException("Either a version name or uid is required.", nameof(request));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));

		var requestObject = new JsonObject();
		if (!string.IsNullOrWhiteSpace(request.VersionName)) {
			requestObject["name"] = request.VersionName;
		}
		if (!string.IsNullOrWhiteSpace(request.VersionUid)) {
			requestObject["uid"] = request.VersionUid;
		}

		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SetActiveProcessVersion, environmentSettings);
		// ProcessDesignService uses BodyStyle=Wrapped: the request is wrapped under a "request" property.
		string requestBody = new JsonObject { ["request"] = requestObject }.ToJsonString();
		string versionIdentity = string.IsNullOrWhiteSpace(request.VersionName)
			? request.VersionUid
			: request.VersionName;
		logger.WriteInfo($"Activating version '{versionIdentity}' on '{environmentName}'...");

		string responseBody = client.ExecutePostRequest(url, requestBody);
		// The same guard the in-place sibling added after a real incident (ModifyBusinessProcessCommand,
		// "Reported by manual testing on ENG-92713") and for the same reason: a body that is not this envelope
		// makes JsonSerializer throw about a .NET type and a byte offset, and that reaches an agent. AGENTS.md
		// records that a wrong ProcessDesignService path answers with an HTML error page.
		//
		// On THIS path the unexplained state is which version the environment executes, so the message says what
		// to read rather than inviting a retry: activation re-saves every member of the family, and repeating it
		// blind is not a free probe.
		ResponseEnvelope? envelope;
		try {
			envelope = JsonSerializer.Deserialize<ResponseEnvelope>(responseBody, JsonOptions);
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				"SetActiveProcessVersion returned a response clio could not read, so WHICH version the "
				+ "environment now runs is UNKNOWN - the write may have taken effect. Read the family back with "
				+ "describe-business-process and check isActiveVersion before retrying or reporting this. The "
				+ "parser detail is on the inner exception; it names a .NET type and a byte offset, which helps "
				+ "a developer reading a stack trace and is noise to the caller reading this.",
				exception);
		}

		ResultDto result = (envelope
				?? throw new InvalidOperationException("SetActiveProcessVersion returned an empty response."))
			.Result
			?? throw new InvalidOperationException("SetActiveProcessVersion returned an unexpected response shape.");
		if (!result.Success) {
			throw new InvalidOperationException(BuildFailureMessage(result));
		}

		return new SetActiveProcessVersionResult(result.ActiveVersionName, result.ActiveVersionSchemaUId,
			result.DeactivationFailureCount, result.Warnings);
	}

	// A failed activation must say WHICH version is actually active, because that is the whole point of the
	// read-back: the platform logs and SWALLOWS a failure to deactivate a sibling, so "it did not work" leaves
	// the caller not knowing whether the environment is running the old version, the new one, or two at once.
	private static string BuildFailureMessage(ResultDto result) {
		string message = result.ErrorMessage ?? "SetActiveProcessVersion failed.";
		if (!string.IsNullOrWhiteSpace(result.ActiveVersionName)
			|| !string.IsNullOrWhiteSpace(result.ActiveVersionSchemaUId)) {
			message += $" The version the environment reports as actual is '{result.ActiveVersionName}' "
				+ $"(UId: {result.ActiveVersionSchemaUId}).";
		}
		if (result.DeactivationFailureCount > 0) {
			message += $" {result.DeactivationFailureCount} sibling(s) are ALSO still flagged active, so which "
				+ "one runs is decided by package order rather than by this call.";
		}

		// Relayed on the FAILURE path too, not just on success. warnings[] is a declared response member that the
		// package's own fixtures pin as wire contract, and this throw is where a failed activation leaves clio -
		// so anything not appended here is discarded. The warnings describe what the write DID do (activation
		// re-saves every member of the family in one transaction), and that is not less relevant because the
		// outcome was refused: it is what the caller has to reason about before retrying.
		if (result.Warnings is { Count: > 0 }) {
			message += " " + string.Join(" ", result.Warnings);
		}

		return message;
	}

	#region DTOs (wire shape)

	private sealed class ResponseEnvelope {
		[JsonPropertyName("SetActiveProcessVersionResult")]
		public ResultDto? Result { get; set; }
	}

	private sealed class ResultDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorMessage")]
		public string? ErrorMessage { get; set; }

		[JsonPropertyName("activeVersionSchemaUId")]
		public string? ActiveVersionSchemaUId { get; set; }

		[JsonPropertyName("activeVersionName")]
		public string? ActiveVersionName { get; set; }

		[JsonPropertyName("deactivationFailureCount")]
		public int DeactivationFailureCount { get; set; }

		[JsonPropertyName("warnings")]
		public List<string>? Warnings { get; set; }
	}

	#endregion
}

/// <summary>
/// Activates one version of a business process and prints the state read back after the write.
/// </summary>
public class SetActiveProcessVersionCommand(
	ISetActiveProcessVersionService setActiveProcessVersionService,
	ILogger logger)
	: Command<SetActiveProcessVersionOptions> {
	/// <inheritdoc />
	public override int Execute(SetActiveProcessVersionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			bool hasName = !string.IsNullOrWhiteSpace(options.VersionName);
			bool hasUid = !string.IsNullOrWhiteSpace(options.VersionUid);
			if (hasName == hasUid) {
				throw new InvalidOperationException(hasName
					? "Provide only one of --name or --uid, not both."
					: "One of --name or --uid is required.");
			}

			SetActiveProcessVersionResult result = setActiveProcessVersionService.SetActiveVersion(
				options.Environment,
				new SetActiveProcessVersionRequest(options.VersionName, options.VersionUid));
			logger.WriteInfo(
				$"Version '{result.ActiveVersionName}' is now the actual one (UId: {result.ActiveVersionSchemaUId}).");
			// Stated on every success because it is the half of "activated" that callers assume wrongly: the
			// switch reaches NEW instances only, and instances already running finish on the version they started.
			logger.WriteInfo(
				"New process instances will start on this version. Instances already running stay on the version "
				+ "they started with — activation never migrates them.");
			WarnOnPartialActivation(options, result);
			foreach (string warning in result.Warnings ?? []) {
				logger.WriteWarning(warning);
			}
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Reports the two ways a reported SUCCESS is still not what the caller asked for.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The read-back exists to expose exactly these, and until now the success path discarded both. A non-zero
	/// deactivation count is the platform having logged and SWALLOWED a failure to deactivate a sibling (ADR
	/// choice 4): two members stay flagged active and package order — not this call — decides which one runs,
	/// which is the state PRD SM-03's counter-metric forbids reporting as a plain success. A read-back naming
	/// a DIFFERENT member than the request is the second: it is read from the environment rather than echoed
	/// precisely so a caller can see that, and printing "Version X is now the actual one" with exit 0 for some
	/// other X hides it.
	/// </para>
	/// <para>
	/// Warnings and not a failure: the write DID happen and the environment answered about its own state, so
	/// exit 1 would tell an operator to retry something that already took effect. What they need is to be told
	/// which version actually runs.
	/// </para>
	/// </remarks>
	private void WarnOnPartialActivation(SetActiveProcessVersionOptions options,
		SetActiveProcessVersionResult result) {
		if (result.DeactivationFailureCount > 0) {
			logger.WriteWarning(
				$"{result.DeactivationFailureCount} sibling(s) are ALSO still flagged active, so which one runs "
				+ "is decided by package order rather than by this call. Read the family back with "
				+ "describe-business-process before reporting the rollback as done.");
		}
		// Three states, not two, and the third used to be reported as a clean success. The read-back IS the
		// operation here - the platform's own write reports nothing usable - so a response that establishes
		// NOTHING about which version is actual has not confirmed the thing this command exists to confirm.
		// Treating a blank read-back as "matches" made silence indistinguishable from agreement.
		if (!ReadBackEstablished(options, result)) {
			logger.WriteWarning(
				"The activation was accepted, but the environment did not report which version is actual, so "
				+ "clio could NOT confirm the change took effect. Read the family back with "
				+ "describe-business-process and check isActiveVersion before reporting this as done.");
		} else if (!ReadBackMatchesRequest(options, result)) {
			logger.WriteWarning(
				$"The environment reports '{result.ActiveVersionName}' (UId: {result.ActiveVersionSchemaUId}) as "
				+ "the actual version, which is NOT the version this call asked for. The activation was accepted "
				+ "but the version that runs is not the one requested.");
		}
	}

	// Whether the server came back with the identity the caller actually supplied. Asked on that identity alone
	// because the other one is a value clio never sent: a response that omits it is not evidence of anything,
	// and a response that omits the one that WAS sent leaves the outcome unestablished rather than agreed.
	private static bool ReadBackEstablished(SetActiveProcessVersionOptions options,
		SetActiveProcessVersionResult result) =>
		!string.IsNullOrWhiteSpace(options.VersionName)
			? !string.IsNullOrWhiteSpace(result.ActiveVersionName)
			: Guid.TryParse(result.ActiveVersionSchemaUId, out _);

	// Compared on whichever identity the caller supplied, because the other one is a value clio never sent and
	// therefore has nothing to disagree with. UIds are parsed rather than string-compared: the server is free
	// to render a GUID in a different case or format than the caller typed, and that is not a mismatch.
	// Only ever reached once ReadBackEstablished has said the caller's own identity came back, so neither branch
	// needs to decide what an ABSENT read-back means - that question has an answer of its own now, and it is not
	// "matches". A caller's unparseable --uid still answers true: that is a malformed request rather than a
	// disagreement about which version runs, and the option parsing owns it.
	private static bool ReadBackMatchesRequest(SetActiveProcessVersionOptions options,
		SetActiveProcessVersionResult result) {
		if (!string.IsNullOrWhiteSpace(options.VersionName)) {
			return string.Equals(result.ActiveVersionName, options.VersionName.Trim(),
				StringComparison.OrdinalIgnoreCase);
		}
		if (!Guid.TryParse(options.VersionUid, out Guid requested)
			|| !Guid.TryParse(result.ActiveVersionSchemaUId, out Guid reported)) {
			return true;
		}
		return requested == reported;
	}
}

/// <summary>
/// Request payload for activating one version of a business process.
/// </summary>
/// <param name="VersionName">Schema name (code) of the version to activate.</param>
/// <param name="VersionUid">Schema UId of the version to activate.</param>
public sealed record SetActiveProcessVersionRequest(string VersionName, string VersionUid);

/// <summary>
/// Structured result of an activation, read back from the server AFTER the write.
/// </summary>
/// <param name="ActiveVersionName">Name of the version the environment reports as actual after the write.</param>
/// <param name="ActiveVersionSchemaUId">UId of that version, never echoed from the request.</param>
/// <param name="DeactivationFailureCount">
/// Siblings still flagged active after the write. Zero on a success; non-zero means the platform swallowed a
/// deactivation failure and package order — not this call — decides which version runs.
/// </param>
/// <param name="Warnings">
/// Outcomes that succeeded but the caller has to know about, notably that activation re-saves EVERY member of
/// the family in one transaction — a write nobody asked for by name.
/// </param>
public sealed record SetActiveProcessVersionResult(string? ActiveVersionName, string? ActiveVersionSchemaUId,
	int DeactivationFailureCount, IReadOnlyList<string>? Warnings = null);
