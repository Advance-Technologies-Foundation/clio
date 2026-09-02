using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.UserEnvironment;
using ErrorOr;

namespace Clio.Command;

/// <summary>
/// Options for editing an existing business process via the ProcessDesignService package.
/// Consumed by the MCP <c>modify-business-process</c> tool, which sets these properties directly.
/// </summary>
// The version literal states what THIS command's code needs — the newest operation it sends that an
// older server does not have. Today that is the element-level performer block and the
// reference-existence guard behind it (bare-Guid Lookup values, performer contact/role), shipped in
// the 1.3.1.1 archive: an older server has no performer member and silently discards the block while
// answering success, and a pre-guard server stores a dead id instead of refusing it. Presence alone
// cannot express either — the email block's 1.2.0.1 floor set this precedent and is subsumed by this
// literal. The guard fixture asserts the shipped archive satisfies the literal, so clio can never
// demand a version it does not itself carry.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.3.1.1",
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class ModifyBusinessProcessOptions : EnvironmentOptions {
	/// <summary>Process code (schema Name) to edit. Provide exactly one of <see cref="ProcessName"/> or <see cref="ProcessUid"/>.</summary>
	public string ProcessName { get; set; } = string.Empty;

	/// <summary>Process schema UId to edit. Provide exactly one of <see cref="ProcessName"/> or <see cref="ProcessUid"/>.</summary>
	public string ProcessUid { get; set; } = string.Empty;

	/// <summary>Inline JSON operations array ([{op:addElement|removeElement|addFlow|removeFlow, …}]).</summary>
	public string OperationsJson { get; set; } = string.Empty;
}

/// <summary>
/// Edits an existing business process on a Creatio environment via the ProcessDesignService package.
/// </summary>
public interface IModifyBusinessProcessService {
	/// <summary>
	/// Applies the given operations to an existing process and saves it.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Modify request (process identity + operations JSON).</param>
	/// <returns>Structured result with the edited schema identity and applied-operation count.</returns>
	ModifyBusinessProcessResult ModifyProcess(string environmentName, ModifyBusinessProcessRequest request);
}

/// <summary>
/// Default ProcessDesignService-backed implementation of <see cref="IModifyBusinessProcessService"/>.
/// </summary>
public sealed class ModifyBusinessProcessService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: IModifyBusinessProcessService {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ModifyBusinessProcessResult ModifyProcess(string environmentName, ModifyBusinessProcessRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.ProcessName) && string.IsNullOrWhiteSpace(request.ProcessUid)) {
			throw new ArgumentException("Either a process name or uid is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.OperationsJson)) {
			throw new ArgumentException("Operations content is required.", nameof(request));
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
		requestObject["operations"] = ParseOperations(request.OperationsJson);

		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ModifyProcess, environmentSettings);
		// ProcessDesignService uses BodyStyle=Wrapped: the request is wrapped under a "request" property.
		string requestBody = new JsonObject { ["request"] = requestObject }.ToJsonString();
		string processIdentity = string.IsNullOrWhiteSpace(request.ProcessName) ? request.ProcessUid : request.ProcessName;
		logger.WriteInfo($"Editing process '{processIdentity}' on '{environmentName}'...");

		string responseBody = client.ExecutePostRequest(url, requestBody);
		ModifyProcessResponseEnvelope envelope =
			JsonSerializer.Deserialize<ModifyProcessResponseEnvelope>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("ModifyProcess returned an empty response.");
		ModifyProcessResultDto result = envelope.Result
			?? throw new InvalidOperationException("ModifyProcess returned an unexpected response shape.");
		if (!result.Success) {
			throw new InvalidOperationException(result.ErrorMessage ?? "ModifyProcess failed.");
		}

		return new ModifyBusinessProcessResult(result.SchemaName, result.SchemaUId, result.AppliedOperations,
			result.Warnings);
	}

	private static JsonArray ParseOperations(string operationsJson) {
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

	private sealed class ModifyProcessResponseEnvelope {
		[JsonPropertyName("ModifyProcessResult")]
		public ModifyProcessResultDto? Result { get; set; }
	}

	private sealed class ModifyProcessResultDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("schemaUId")]
		public string? SchemaUId { get; set; }

		[JsonPropertyName("schemaName")]
		public string? SchemaName { get; set; }

		[JsonPropertyName("appliedOperations")]
		public int AppliedOperations { get; set; }

		[JsonPropertyName("errorMessage")]
		public string? ErrorMessage { get; set; }

		// Declared because an UNDECLARED member is dropped without a trace, and what travels here is precisely
		// the class of outcome a caller cannot otherwise see: a connection written to a column with no registry
		// row (invisible in the designer and to every registry-reading feature), and a cleared binding, which
		// vanishes from the read-back so that "cleared" and "never bound" become indistinguishable afterwards.
		// Absent on an older CrtProcessBuilder, which is why it stays nullable rather than defaulting to empty.
		[JsonPropertyName("warnings")]
		public List<string>? Warnings { get; set; }
	}

	#endregion
}

/// <summary>
/// Edits an existing business process from an inline JSON operations array and prints the result.
/// </summary>
public class ModifyBusinessProcessCommand(
	IModifyBusinessProcessService modifyBusinessProcessService,
	IProcessDescriber processDescriber,
	ILogger logger)
	: Command<ModifyBusinessProcessOptions> {
	/// <inheritdoc />
	public override int Execute(ModifyBusinessProcessOptions options) {
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

			if (string.IsNullOrWhiteSpace(options.OperationsJson)) {
				throw new InvalidOperationException("An operations array is required.");
			}

			ModifyBusinessProcessResult result = modifyBusinessProcessService.ModifyProcess(
				options.Environment,
				new ModifyBusinessProcessRequest(options.ProcessName, options.ProcessUid, options.OperationsJson));
			logger.WriteInfo(
				$"Process '{result.SchemaName}' edited ({result.AppliedOperations} operation(s) applied; UId: {result.SchemaUId}).");
			// Written as WARNINGS on a SUCCESSFUL edit, deliberately. These are outcomes that applied and are not
			// what a caller would assume, so folding them into the success line (or dropping them, which is what
			// happened before) leaves the caller believing something that is not quite true.
			foreach (string warning in result.Warnings ?? []) {
				logger.WriteWarning(warning);
			}
			WarnOnDiscardedBlocks(options, result.SchemaName);
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	// The access-rights guard must not report "could not check" the same way it reports "verified": it is the
	// only automated evidence that a grant or revoke landed, on an element with no output parameters. The
	// command still succeeds — an unreadable description is not evidence of a drop — but the caller is told the
	// verification did not happen, so an unapplied revoke cannot pass as an applied one.
	private void WarnAccessRightsUnverified(IReadOnlyList<string> expectedRights, string reason) {
		string? warning = BuildUnverifiedWarning(expectedRights, reason);
		if (warning is not null) {
			logger.WriteWarning(warning);
		}
	}

	// One wording for every "the check did not happen" outcome, so they cannot drift apart.
	private static string? BuildUnverifiedWarning(IReadOnlyList<string> expectedRights, string reason) {
		if (expectedRights.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", expectedRights);
		string subject = expectedRights.Count == 1 ? "element" : "elements";
		return $"Could not verify that the 'accessRights' configuration for the {subject} '{elements}' landed: "
			+ $"{reason}. The operation itself succeeded, but this check is the only signal that the permissions "
			+ "were actually written — the element has no output parameters. Re-read the process with "
			+ "describe-business-process before reporting a grant or revoke as applied.";
	}

	// Same silent-drop guard as the build path, for every block an edit can carry: a server predating a
	// feature discards its block and still answers success, so an edit can report an applied operation whose
	// configuration never landed. Read the process back ONCE and check both. Only runs when the operations
	// actually carried a block. See EmailBlockExpectation / AccessRightsBlockExpectation.
	private void WarnOnDiscardedBlocks(ModifyBusinessProcessOptions options, string? schemaName) {
		IReadOnlyList<string> expectedEmail = EmailBlockExpectation.FromOperations(options.OperationsJson);
		IReadOnlyList<string> expectedRights = AccessRightsBlockExpectation.FromOperations(options.OperationsJson);

		// An accessRights block on addElement is dropped by the server (it applies only email/performer).
		// That is by design, but the outcome the caller lives with is the same unconfigured element as a
		// silent drop, so say so rather than leaving them to discover it at run time.
		// EXCEPT when a setElement in the same array configures that element - which is precisely what the
		// warning tells the caller to do. Warning about it there would assert something false about a payload
		// this code recommends, and a warning that is wrong in the recommended workflow teaches callers to
		// ignore the whole family, including the true ones below.
		string? ignoredOnAdd = AccessRightsBlockExpectation.BuildAddElementWarning(
			[.. AccessRightsBlockExpectation.IgnoredOnAddElement(options.OperationsJson)
				.Except(expectedRights, StringComparer.OrdinalIgnoreCase)]);
		if (ignoredOnAdd is not null) {
			logger.WriteWarning(ignoredOnAdd);
		}
		if (expectedEmail.Count == 0 && expectedRights.Count == 0) {
			return;
		}

		string identity = string.IsNullOrWhiteSpace(schemaName) ? options.ProcessName : schemaName;
		if (string.IsNullOrWhiteSpace(identity)) {
			// Nothing to read back against; silence would be indistinguishable from a verified success.
			WarnAccessRightsUnverified(expectedRights, "the edit returned no process identity to read back");
			return;
		}

		ErrorOr<DescribeProcessResult> described =
			processDescriber.Describe(new ProcessIdentity(identity, null, null), null);
		if (described.IsError) {
			WarnAccessRightsUnverified(expectedRights, described.FirstError.Description);
			return;
		}

		string? unresolvedRights = BuildUnverifiedWarning(
			AccessRightsBlockExpectation.Unresolved(described.Value, expectedRights),
			"the saved process does not report an element with that name or UId");
		if (unresolvedRights is not null) {
			logger.WriteWarning(unresolvedRights);
		}

		string? noFilter = AccessRightsBlockExpectation.BuildNoFilterWarning(
			AccessRightsBlockExpectation.WithoutRecordFilter(described.Value, expectedRights));
		if (noFilter is not null) {
			logger.WriteWarning(noFilter);
		}

		string? rightsWarning = AccessRightsBlockExpectation.BuildWarning(
			AccessRightsBlockExpectation.Missing(described.Value, expectedRights));
		if (rightsWarning is not null) {
			logger.WriteWarning(rightsWarning);
		}

		string? warning = EmailBlockExpectation.BuildWarning(
			EmailBlockExpectation.Missing(described.Value, expectedEmail));
		if (warning is not null) {
			logger.WriteWarning(warning);
		}
	}
}

/// <summary>
/// Request payload for editing a business process.
/// </summary>
/// <param name="ProcessName">Process code (schema Name) to edit.</param>
/// <param name="ProcessUid">Process schema UId to edit.</param>
/// <param name="OperationsJson">The JSON operations array content.</param>
public sealed record ModifyBusinessProcessRequest(string ProcessName, string ProcessUid, string OperationsJson);

/// <summary>
/// Structured result of a business-process edit.
/// </summary>
/// <param name="SchemaName">Name (code) of the edited process schema.</param>
/// <param name="SchemaUId">UId of the edited process schema.</param>
/// <param name="AppliedOperations">Number of operations applied.</param>
/// <param name="Warnings">
/// Notices the applied operations raised — outcomes that SUCCEEDED but the caller has to know about, so a
/// successful edit is never silently different from what was asked for. <c>null</c> when there are none, or when
/// the target environment carries a package that does not report them.
/// </param>
public sealed record ModifyBusinessProcessResult(string? SchemaName, string? SchemaUId, int AppliedOperations,
	IReadOnlyList<string>? Warnings = null);
