using System;
using System.Collections.Generic;
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
// older server does not have. Today that is the approval APPROVER, shipped in the 1.4.2.0 archive: an older
// server has no approver member and silently discards it while answering success — the element saves and
// runs with nobody assigned. The approval block itself (1.4.1.0), the performer block
// (1.3.1.1) and the email block (1.2.0.1) set this precedent and are subsumed by this literal. Presence
// alone cannot express any of them. The guard fixture asserts the shipped archive satisfies the literal,
// so clio can never demand a version it does not itself carry.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.4.2.0",
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

		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
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
			WarnOnDiscardedConfigurationBlocks(options, result.SchemaName);
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	// Same silent-drop guard as the build path: a server predating sendEmail discards an email block and still
	// answers success, so an edit can report an applied operation whose email configuration never landed. Read the
	// process back and say so. Only runs when the operations actually carried a block; a failed read-back is never
	// escalated, since it is not evidence of a drop. See EmailBlockExpectation for why this is not version-based.
	private void WarnOnDiscardedConfigurationBlocks(ModifyBusinessProcessOptions options, string? schemaName) {
		IReadOnlyList<string> expected = EmailBlockExpectation.FromOperations(options.OperationsJson);
		// The Approval element has the same silent-drop failure, so it is verified from the SAME read-back rather
		// than a second one — the describe below is the expensive part.
		IReadOnlyList<string> expectedApproval = ApprovalBlockExpectation.FromOperations(options.OperationsJson);
		if (expected.Count == 0 && expectedApproval.Count == 0) {
			return;
		}

		string identity = string.IsNullOrWhiteSpace(schemaName) ? options.ProcessName : schemaName;
		if (string.IsNullOrWhiteSpace(identity)) {
			return;
		}

		ErrorOr<DescribeProcessResult> described =
			processDescriber.Describe(new ProcessIdentity(identity, null, null), null);
		if (described.IsError) {
			return;
		}

		string? warning = EmailBlockExpectation.BuildWarning(
			EmailBlockExpectation.Missing(described.Value, expected));
		if (warning is not null) {
			logger.WriteWarning(warning);
		}

		string? approvalWarning = ApprovalBlockExpectation.BuildWarning(
			ApprovalBlockExpectation.Missing(described.Value, expectedApproval));
		if (approvalWarning is not null) {
			logger.WriteWarning(approvalWarning);
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
