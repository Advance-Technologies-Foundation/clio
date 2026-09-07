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
// older server does not have. Today that is `setFlowCondition` plus the formula validator behind the
// `expression` mapping source, both introduced by ENG-95891 and both first shipping in 1.4.0.0. They need
// the literal for the two distinct reasons the bundled-packages article names. `setFlowCondition` is an
// operation an older server does not carry at all: the token would be rejected by the server's own dispatch
// registry with a "supported operations are …" message, which reads as a clio bug rather than a stale
// environment.
//
// The literal is 1.4.0.44, and three versions are in play. 1.4.0.41 stopped validating formulas IN THE
// PACKAGE, because the
// platform's own pre-save gate was already validating them — a mapped expression and a flow condition alike,
// measured with both package guards built out and installed
// (spec/eng-95891-formula-expressions/eng-95891-formula-expressions-save-gate-probe.md). So the formula half
// of this floor is no longer a tightened validator; .41 checks strictly LESS than .37 did, and an environment
// between them refuses at least as much. It is above .41 because .42 corrected the rewrite this description
// promises — every serialised error in one message, and an element-scoped reference named as such — and .44
// specifically because .44 is the first archive carrying both that and the lookup-constant contract the
// ENG-96325 merge brought in (see below). Not because .44 is what this clio bundles: the bundle moves on
// every rebundle and the fixture only asserts the floor is satisfiable by it, so a floor that tracks the
// bundle would demand an upgrade of environments that already work. What the floor buys is the MESSAGE contract this tool's description
// promises: below .41 a bad formula is refused in the package's own wording, with its own reference
// pre-check, and without the serialised-error rewrite — so an unresolvable parameter reference comes back as
// `{ErrorType:2,ErrorData:{ParameterUId:"…"}}` rather than as a sentence.
//
// Do not lower it to .37 on the grounds that .37 refuses too. It does, with different text. And do not lower
// it below .37 on any grounds: the refusals that SURVIVE the collapse were themselves measured one archive
// at a time — the activity-result guard in .32 and the element-retarget refusal in .37. (The
// platform-grammar element segment, which .35 added and which earlier revisions of this comment counted
// as a third, turns out not to have been load-bearing: the strict RegexParameterValue it mirrored is used
// only for data-source filter map paths, while a parameter VALUE is parsed by the brace-optional
// GeneratorUtilities patterns, so the element scoping survived the looser form anyway. Do not cite it as
// one of the refusals this floor protects; the other two are.) Against an environment at .3, `setFlowCondition` on a result-driven
// branch is NOT refused: the condition is stored and then ignored at run time, the exact silent failure the
// description offers protection from.
// This subsumes the previous 1.3.1.1 floor (the element-level performer block and its reference-existence
// guard), which subsumed the email block's 1.2.0.1 floor before it. The guard fixture asserts the shipped
// archive satisfies the literal, so clio can never demand a version it does not itself carry.
//
// TWO reasons stand behind this floor now, and the merge of ENG-96325 added the second. Its
// lookup-constant contract shipped in the 1.4.0.40 archive: a mappings[] 'value' on a Lookup target
// may carry an already-composed macro, and an older server rejects it outright as "not a bare Guid"
// - the same "server starts accepting an input form an older one refuses" shape that produced the
// 1.3.1.1 literal. The number below satisfies both that and the message contract described above.
//
// AND AN ACCESS-CONTROL PROPERTY, which a review caught me deleting along with it. Below 1.4.0.40 the
// display-name resolution used a raw Select rather than a rights-aware entity read, and a raw Select
// bypasses row-level permissions - so the referenced record's name was resolved and stored into the
// parameter's display value whether or not the acting user may see that record. The old comment
// called this "NOT a security floor" because no RELEASED archive carried the wider read. That premise
// is weaker now, not stronger: this branch cut roughly thirty archives between .20 and .52, and its
// own manual runs installed .18 and .37 - both below the rights-aware read. So do not lower this
// floor below 1.4.0.40 on message-contract grounds alone; the floor is also what keeps a
// pre-rights-aware read off an environment clio installs onto.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.4.0.44",
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
			// Surfaced HERE or nowhere. The success record below cannot carry it (it is only built on success),
			// and this throw is the only way a refused operation leaves clio — so discarding the index would
			// leave the caller doing on the client what the server split the field to stop them doing:
			// bisecting the batch against a live environment. Appended rather than woven in because the
			// server's own sentence is the primary message and several guards name neither endpoint.
			string refusedBy = result.FailedOperationIndex.HasValue
				? $" The operation at index {result.FailedOperationIndex.Value} is the one that refused."
				: string.Empty;
			throw new InvalidOperationException(
				(result.ErrorMessage ?? "ModifyProcess failed.") + refusedBy);
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

		// Zero-based index of the operation that refused, when a single one did. Declared for the same reason
		// as warnings below — an undeclared member is dropped without a trace — and it matters more here,
		// because this is the only field on a FAILED edit that says which operation to look at. Nullable, and
		// the null carries meaning: the server sends none when the failure came after the operation loop (the
		// pre-save gate judging the whole schema, which is now the common failure), and an older
		// CrtProcessBuilder does not send the field at all. Both cases mean "no operation is to blame", which
		// is exactly why the server stopped overloading appliedOperations to answer this.
		[JsonPropertyName("failedOperationIndex")]
		public int? FailedOperationIndex { get; set; }

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
			WarnOnDiscardedEmailBlocks(options, result.SchemaName);
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
	private void WarnOnDiscardedEmailBlocks(ModifyBusinessProcessOptions options, string? schemaName) {
		IReadOnlyList<string> expected = EmailBlockExpectation.FromOperations(options.OperationsJson);
		if (expected.Count == 0) {
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
