using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Options for saving an edited business process as a NEW VERSION via the ProcessDesignService package.
/// Consumed by the MCP <c>modify-business-process-as-new-version</c> tool, which sets these properties directly.
/// </summary>
// The floor is a MISSING OPERATION, not a changed input form — a stricter case than the sibling literals on
// Create/Modify, and the reason this one cannot be presence-only. ModifyProcessAsNewVersion first exists in the
// 1.6.1.0 archive; an environment on any earlier package answers the route with a 404 rather than a contract
// error, so without the literal the caller would see a transport failure instead of "your package is behind".
// The guard fixture asserts the shipped archive satisfies this literal, so clio can never demand a version it
// does not itself carry.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.6.1.0",
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class ModifyProcessAsNewVersionOptions : EnvironmentOptions {
	/// <summary>Process code (schema Name) of the SOURCE. Provide exactly one of <see cref="ProcessName"/> or <see cref="ProcessUid"/>.</summary>
	public string ProcessName { get; set; } = string.Empty;

	/// <summary>Process schema UId of the SOURCE. Provide exactly one of <see cref="ProcessName"/> or <see cref="ProcessUid"/>.</summary>
	public string ProcessUid { get; set; } = string.Empty;

	/// <summary>
	/// Package the new version is saved into. Optional — absent lets the platform choose, which is the SOURCE's
	/// package when the caller may edit it and the design package otherwise. An INPUT rather than something
	/// derived, because a version does not inherit the root's package: cross-package families exist.
	/// </summary>
	public string PackageName { get; set; } = string.Empty;

	/// <summary>The SAME inline JSON operations array <c>modify-business-process</c> takes; empty is legal and yields a plain snapshot.</summary>
	public string OperationsJson { get; set; } = string.Empty;
}

/// <summary>
/// Saves an edited copy of an existing business process as a new version via the ProcessDesignService package.
/// </summary>
public interface IModifyProcessAsNewVersionService {
	/// <summary>
	/// Applies the given operations to a CLONE of the source process and saves that clone as a new version.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Source identity, optional target package, and the operations JSON.</param>
	/// <returns>Structured result describing the created version.</returns>
	ModifyProcessAsNewVersionResult ModifyAsNewVersion(string environmentName,
		ModifyProcessAsNewVersionRequest request);
}

/// <summary>
/// Default ProcessDesignService-backed implementation of <see cref="IModifyProcessAsNewVersionService"/>.
/// </summary>
public sealed class ModifyProcessAsNewVersionService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	IProcessPageFactsChecker pageFactsChecker,
	ILogger logger)
	: IModifyProcessAsNewVersionService {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ModifyProcessAsNewVersionResult ModifyAsNewVersion(string environmentName,
		ModifyProcessAsNewVersionRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.ProcessName) && string.IsNullOrWhiteSpace(request.ProcessUid)) {
			throw new ArgumentException("Either a process name or uid is required.", nameof(request));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));

		var requestObject = new JsonObject();
		if (!string.IsNullOrWhiteSpace(request.ProcessName)) {
			requestObject["name"] = request.ProcessName;
		}
		if (!string.IsNullOrWhiteSpace(request.ProcessUid)) {
			requestObject["uid"] = request.ProcessUid;
		}
		if (!string.IsNullOrWhiteSpace(request.PackageName)) {
			requestObject["packageName"] = request.PackageName;
		}
		// An ABSENT operations array is a legal request — a version that is a pure snapshot of the source — so
		// the empty case sends an empty array rather than being refused the way the in-place edit refuses it.
		requestObject["operations"] = ParseOperations(request.OperationsJson);

		// The SAME pre-check both sibling write paths run, and skipping it here was not a decision anyone made:
		// this path takes the identical operations vocabulary, so an invented button or data-source name survives
		// every server-side check on a version exactly as it does on an in-place edit, and shows itself only at
		// run time as a step that never completes.
		//
		// It matters MORE here, not less. An in-place edit that produces an unfinishable step can be edited
		// again; a version cannot be deleted, so the artifact carrying the bad reference is permanent, and the
		// caller is likely to activate it precisely because it looked like a clean snapshot.
		ProcessPageCheckResult pageCheck =
			pageFactsChecker.CheckPreconfiguredPages(environmentName, requestObject["operations"]);
		if (!string.IsNullOrWhiteSpace(pageCheck?.Error)) {
			throw new InvalidOperationException(pageCheck.Error);
		}
		foreach (string pageWarning in pageCheck?.Warnings ?? []) {
			logger.WriteWarning(pageWarning);
		}

		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ModifyProcessAsNewVersion, environmentSettings);
		// ProcessDesignService uses BodyStyle=Wrapped: the request is wrapped under a "request" property.
		string requestBody = new JsonObject { ["request"] = requestObject }.ToJsonString();
		string processIdentity = string.IsNullOrWhiteSpace(request.ProcessName) ? request.ProcessUid : request.ProcessName;
		logger.WriteInfo($"Saving a new version of process '{processIdentity}' on '{environmentName}'...");

		string responseBody = client.ExecutePostRequest(url, requestBody);
		// Parsed inside a try, for the reason the in-place sibling added the same guard after a real incident
		// (ModifyBusinessProcessCommand, "Reported by manual testing on ENG-92713"): a body that is not this
		// envelope makes JsonSerializer throw a message built for a developer - a .NET type name and a byte
		// offset - and this one reaches an agent, which cannot act on it. AGENTS.md records that a wrong
		// ProcessDesignService path returns an HTML error page, so `'<' is an invalid start of a value` is a
		// reachable outcome, not a hypothetical.
		//
		// The stakes are HIGHER here than on the in-place path. There the unexplained artifact is an edit that
		// may or may not have landed; here it may be a VERSION that is already persisted and that the platform
		// offers no way to delete. So the message says that explicitly rather than only "unknown".
		ResponseEnvelope? envelope;
		try {
			envelope = JsonSerializer.Deserialize<ResponseEnvelope>(responseBody, JsonOptions);
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				"ModifyProcessAsNewVersion returned a response clio could not read, so whether a version was "
				+ "created is UNKNOWN - and a version that DID persist cannot be deleted. Re-read the process "
				+ "with describe-business-process and check its versions[] before retrying, because a retry "
				+ "would allocate a second version rather than replace the first. The parser detail is on the "
				+ "inner exception; it names a .NET type and a byte offset, which helps a developer reading a "
				+ "stack trace and is noise to the caller reading this.",
				exception);
		}

		ResultDto result = (envelope
				?? throw new InvalidOperationException("ModifyProcessAsNewVersion returned an empty response."))
			.Result
			?? throw new InvalidOperationException("ModifyProcessAsNewVersion returned an unexpected response shape.");
		if (!result.Success) {
			throw new InvalidOperationException(BuildFailureMessage(result));
		}

		return new ModifyProcessAsNewVersionResult(result.VersionName, result.VersionSchemaUId, result.Version,
			result.IsActiveVersion, result.VersionRootSchemaUId, result.AppliedOperations, result.Warnings);
	}

	// A failure that still names a version is NOT a failed create: the version exists and the platform offers no
	// way to delete one, so dropping the name here would leave the caller unable to address something that is
	// really on their environment. Both cases travel on errorMessage, and only the identity tells them apart.
	//
	// This throw is where a refused version leaves clio, so anything not appended here is discarded. That is why
	// the operation diagnosis is relayed too: the server writes appliedOperations AND failedOperationIndex from
	// one Failure helper precisely so the two tell one story, and the tool's own [Description] points the caller
	// at modify-business-process's description, which promises the index. Dropping them left a 40-operation batch
	// refused at index 17 answering with a bare sentence, and the agent bisecting against a live environment -
	// which is the work splitting the field was meant to remove.
	private static string BuildFailureMessage(ResultDto result) {
		string message = result.ErrorMessage ?? "ModifyProcessAsNewVersion failed.";
		// Only when an operation actually refused. Absent means the failure was not an operation's - an identity
		// that did not resolve, a read-only package, the save itself - and inventing index 0 there would name a
		// descriptor that was never the problem.
		if (result.FailedOperationIndex.HasValue) {
			message += $" The operation at index {result.FailedOperationIndex.Value} is the one that refused.";
		}

		// The count is the recovery route for a caller that gets NO index, including one talking to a server too
		// old to send it, so it is reported whenever the server sent operations at all.
		if (result.AppliedOperations > 0) {
			message += $" {result.AppliedOperations} operation(s) had been applied to the version draft.";
		}

		if (!string.IsNullOrWhiteSpace(result.VersionName) || !string.IsNullOrWhiteSpace(result.VersionSchemaUId)) {
			message += $" The version '{result.VersionName}' (UId: {result.VersionSchemaUId}) WAS created and "
				+ "still exists — a version cannot be deleted.";
		}

		// Relayed on the failure path too. warnings[] is a declared response member the package's own fixtures
		// pin as wire contract, and this throw is where a refused create leaves clio - so a warning not appended
		// here is discarded. It matters most on exactly the exits that name a version: the artifact exists, and
		// what the server has to say about it is part of what the caller must act on.
		if (result.Warnings is { Count: > 0 }) {
			message += " " + string.Join(" ", result.Warnings);
		}

		return message;
	}

	private static JsonArray ParseOperations(string operationsJson) {
		if (string.IsNullOrWhiteSpace(operationsJson)) {
			return [];
		}

		JsonNode? node;
		try {
			node = JsonNode.Parse(operationsJson);
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				$"Operations content is not valid JSON: {exception.Message}", exception);
		}

		return node as JsonArray
			?? throw new InvalidOperationException("Operations content must be a JSON array of operations.");
	}

	#region DTOs (wire shape)

	private sealed class ResponseEnvelope {
		[JsonPropertyName("ModifyProcessAsNewVersionResult")]
		public ResultDto? Result { get; set; }
	}

	private sealed class ResultDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorMessage")]
		public string? ErrorMessage { get; set; }

		[JsonPropertyName("versionSchemaUId")]
		public string? VersionSchemaUId { get; set; }

		[JsonPropertyName("versionName")]
		public string? VersionName { get; set; }

		// Nullable so an OMITTED field stays absent instead of arriving as 0 / false. The read half of this
		// feature made three view columns nullable to hold exactly that line — 0 is a real answer there and
		// means "family root" — and the command below turns both of these into affirmative statements about
		// what the environment runs, which silence must not be allowed to make.
		[JsonPropertyName("version")]
		public int? Version { get; set; }

		[JsonPropertyName("isActiveVersion")]
		public bool? IsActiveVersion { get; set; }

		[JsonPropertyName("versionRootSchemaUId")]
		public string? VersionRootSchemaUId { get; set; }

		[JsonPropertyName("appliedOperations")]
		public int AppliedOperations { get; set; }

		// Nullable, and declared for the same reason the two above are: System.Text.Json discards an undeclared
		// member SILENTLY, so leaving this off the DTO dropped the diagnosis the server pays to produce at all
		// eleven of its refusal sites. int? rather than int because ABSENT and "index 0" are different answers -
		// 0 names the first descriptor, and a failure that was not an operation's sends nothing.
		[JsonPropertyName("failedOperationIndex")]
		public int? FailedOperationIndex { get; set; }

		[JsonPropertyName("warnings")]
		public List<string>? Warnings { get; set; }
	}

	#endregion
}

/// <summary>
/// Saves an edited copy of a business process as a new version and prints the result.
/// </summary>
public class ModifyProcessAsNewVersionCommand(
	IModifyProcessAsNewVersionService modifyProcessAsNewVersionService,
	ILogger logger)
	: Command<ModifyProcessAsNewVersionOptions> {
	/// <inheritdoc />
	public override int Execute(ModifyProcessAsNewVersionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			bool hasName = !string.IsNullOrWhiteSpace(options.ProcessName);
			bool hasUid = !string.IsNullOrWhiteSpace(options.ProcessUid);
			if (hasName == hasUid) {
				throw new InvalidOperationException(hasName
					? "Provide only one of --name or --uid, not both."
					: "One of --name or --uid is required.");
			}

			ModifyProcessAsNewVersionResult result = modifyProcessAsNewVersionService.ModifyAsNewVersion(
				options.Environment,
				new ModifyProcessAsNewVersionRequest(options.ProcessName, options.ProcessUid, options.PackageName,
					options.OperationsJson));
			// The number is printed only when the environment reported one: in this feature's vocabulary 0 means
			// the schema is a family ROOT, so a defaulted 0 standing in for an omitted field would state the
			// opposite of what a new version is.
			string version = result.Version is null ? "A new version" : $"Version {result.Version}";
			logger.WriteInfo(
				$"{version} '{result.VersionName}' created ({result.AppliedOperations} operation(s) "
				+ $"applied; UId: {result.VersionSchemaUId}; family root: {result.VersionRootSchemaUId}).");
			// Said on EVERY success, not only when it is surprising: the caller asked to save a version, and what
			// the environment RUNS is the one thing that did not change. Leaving it implicit is how an agent
			// concludes the edit is live and stops. An UNREPORTED flag gets a neutral sentence rather than the
			// reassuring one, because "the source still runs" is a claim about the environment that nothing
			// established.
			logger.WriteInfo(result.IsActiveVersion switch {
				true => "This version is reported ACTIVE — unexpected for a create; verify with "
					+ "describe-business-process.",
				false => "The source version is still the actual one and keeps running. Use "
					+ "set-active-business-process-version to switch, if that is what the user asked for.",
				null => "The environment did not report which version is actual — verify with "
					+ "describe-business-process before reporting what runs."
			});
			foreach (string warning in result.Warnings ?? []) {
				logger.WriteWarning(warning);
			}
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Request payload for saving an edited process as a new version.
/// </summary>
/// <param name="ProcessName">Process code (schema Name) of the source.</param>
/// <param name="ProcessUid">Process schema UId of the source.</param>
/// <param name="PackageName">Package the version is saved into; empty lets the platform choose.</param>
/// <param name="OperationsJson">The JSON operations array content; empty yields a plain snapshot.</param>
public sealed record ModifyProcessAsNewVersionRequest(string ProcessName, string ProcessUid, string PackageName,
	string OperationsJson);

/// <summary>
/// Structured result of saving a process as a new version.
/// </summary>
/// <param name="VersionName">Name the PLATFORM composed (root + package + number), not one the caller chose.</param>
/// <param name="VersionSchemaUId">UId of the created version — how the caller addresses it later.</param>
/// <param name="Version">
/// The number the platform allocated (max in the target package + 1), or <c>null</c> when the environment did
/// not report one — never 0, which in this feature means the schema is a family root.
/// </param>
/// <param name="IsActiveVersion">
/// False on a successful create, or <c>null</c> when the environment did not report it. Reported rather than
/// assumed, so a caller can see that creating a version did not change what the environment executes — and
/// absent rather than false when nothing said so.
/// </param>
/// <param name="VersionRootSchemaUId">UId of the family ROOT. The family is FLAT — a version of a version still points at the root.</param>
/// <param name="AppliedOperations">Number of operations applied to the clone.</param>
/// <param name="Warnings">Outcomes that applied but are not what the caller would assume; <c>null</c> when there are none.</param>
public sealed record ModifyProcessAsNewVersionResult(string? VersionName, string? VersionSchemaUId, int? Version,
	bool? IsActiveVersion, string? VersionRootSchemaUId, int AppliedOperations,
	IReadOnlyList<string>? Warnings = null);
