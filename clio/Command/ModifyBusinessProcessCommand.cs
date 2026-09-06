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
// older server does not have. Today that is the element-level performer block and the
// reference-existence guard behind it (bare-Guid Lookup values, performer contact/role), shipped in
// the 1.3.1.1 archive: an older server has no performer member and silently discards the block while
// answering success, and a pre-guard server stores a dead id instead of refusing it. Presence alone
// cannot express either — the email block's 1.2.0.1 floor set this precedent and is subsumed by this
// literal. Raised to 1.4.0.37 by ENG-95891: a build's `mappings[]` may carry an `expression` source, and
// the formula validator behind it is a TIGHTENED VALIDATOR — a server older than 1.4.0.0 stores such a
// mapping with no check at all, so the same descriptor that is refused on a current environment silently
// persists a broken formula on an older one, to fail at run time. The article is explicit that a tightened
// validator takes a literal rather than being left to convergence, because convergence only warns.
//
// Raised again to 1.4.0.44, and this time the reason runs the other way. 1.4.0.41 is the version that STOPPED
// validating formulas in the package, because the platform's own pre-save gate was already doing it — for a
// mapped expression AND for a flow condition, measured with both package guards built out and installed
// (spec/eng-95891-formula-expressions/eng-95891-formula-expressions-save-gate-probe.md). What the floor buys
// is therefore the MESSAGE contract these descriptions promise, not the existence of a refusal: below .41 a
// bad formula is still refused, but by the package's own wording and its own reference pre-check, and the
// serialised-error rewrite (PlatformValidationMessage) is not there — so an unresolvable parameter reference
// comes back as `{ErrorType:2,ErrorData:{ParameterUId:"…"}}`.
//
// The floor is above .41 because the review round after .41 corrected the rewrite these descriptions
// promise: every serialised error in one message rather than the first, and an element-scoped reference named
// as such rather than called a process parameter. It is .44 specifically because .44 is the first archive
// carrying BOTH that and the lookup-constant contract the ENG-96325 merge brought in (see below), so it is
// the lowest version that satisfies everything these descriptions say. Do not re-derive it from whatever
// this clio happens to bundle: the bundled archive moves on every rebundle, the fixture only asserts the
// floor is SATISFIABLE by it, and a floor that tracks the bundle demands an upgrade of environments that
// already work.
//
// The floor is NOT lowered back to .37 on the grounds that .37 also refuses. It does, with different text, and
// a description that names what a refusal says is only true from .41. Nor is it a tightened validator any
// more: .41 checks strictly LESS than .37 did, so an environment between the two refuses at least as much.
// The superseded .37 rationale, kept because it is the reason not to go below it either way: each archive on
// the ENG-95891 branch was decompressed and grepped for the marker of every refusal the descriptions promised,
// and the activity-result guard (which SURVIVES the collapse) landed in .32, the platform-grammar element
// segment in .35, the element-retarget refusal in .37.
// The guard fixture asserts the shipped archive satisfies the literal, so clio can never
// demand a version it does not itself carry.
//
// TWO reasons stand behind this floor now, and the merge of ENG-96325 added the second. Its
// lookup-constant contract shipped in the 1.4.0.40 archive: a mappings[] 'value' on a Lookup target
// may carry an already-composed macro, and an older server rejects it outright as "not a bare Guid"
// - the same "server starts accepting an input form an older one refuses" shape that produced the
// 1.3.1.1 literal. The number below satisfies both that and the message contract described above.
//
// ===== two requirement lines met in the ENG-92713 merge, and NO released archive carries both =====
// Master's line above stops at 1.4.0.44; the ENG-92713 line below stops at 1.4.11.0, which was cut
// BEFORE this branch merged main and therefore predates every formula/branch behaviour master needs.
// The first archive carrying both is the one cut from the merged package source — the literal below.
//
// The version literal states what THIS command needs — the newest server behaviour it depends on that an
// older one does not have. Two independent lines of that requirement met in this merge, and NO released
// version carries both, which is why the floor is the version this clio bundles rather than either of them.
//
// From ENG-96325 (master, first in 1.4.0.40): the lookup-constant input contract. A mappings[] 'value' on a
// Lookup target may carry an already-composed [#Lookup.{objectUId}.{recordId}#], which that server decodes
// to the bare record id while every earlier server rejects it outright as "not a bare Guid". It is NOT a
// security floor: the raw-Select display-name read it replaced with a rights-aware entity read never
// shipped in a released archive.
//
// From ENG-92713 (this branch): four shapes of one silent failure, none visible in the response. 1.4.2.0
// added the approval APPROVER, which an older server discards while answering success, leaving an element
// that saves and runs with nobody assigned. 1.4.3.0 added the refusal of a notification switched on with no
// email template, and 1.4.4.0 the refusal of the AUTHOR notification with no recipient: an older server
// ACCEPTS either and produces an element that reports the notification as configured and never sends,
// because the runtime checks neither before sending, ignores email errors by default, and — despite the
// caption — never resolves an author, reading only the address the recipient field writes. 1.4.7.0
// PRESERVES the stored employee across a user<->manager approver switch; clio's guidance now tells agents
// that {"approver":{"type":"manager"}} is how to say "their manager approves instead", and on an older
// server that request overwrites the named employee with the current user, rerouting a real approval to
// whoever ran the modify, self-consistently on read-back. Advertising a route the deployed server turns
// destructive is precisely what a floor exists to stop.
//
// 1.4.11.0 is the newest of them and the reason the floor sits here: describe now distinguishes a WRITTEN
// ignoreEmailErrors from the schema-level default the platform copies onto every Approval element. An older
// server cannot, so it answers ignoreEmailErrors:true on an element nobody configured — and since one
// reported field is enough to count as configured, it reports a block there at all. clio's describe tool
// tells agents that absence of that field means "not written, never off"; on an older server that promise is
// false, and a caller acting on it writes a value nobody chose. Same rule as the approver above: a floor
// moves when clio starts ADVERTISING behaviour the deployed server may not have.
//
// 1.4.0.40 predates every ENG-92713 behaviour and 1.4.7.0 predates the merge that brought ENG-96325 in, so
// the first archive carrying both is the one cut from the merged package source. Presence alone cannot
// express any of it. The approval block (1.4.1.0), the performer block (1.3.1.1) and the email block
// (1.2.0.1) set the precedent and are subsumed, as do 1.4.5.0, 1.4.6.0, 1.4.8.0 and 1.4.10.0, which no
// released clio ever bundled. The guard fixture asserts the shipped archive SATISFIES the literal — not that
// it equals it: a rebundle that changes only documentation moves the bundle and must not move the floor,
// since a floor tracks behaviour clio depends on rather than the version it happens to ship. 1.4.10.0 was
// exactly such a case — it corrected two contract doc comments and did not move this literal; 1.4.11.0
// changed what the server REPORTS, so it did.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.4.12.0",
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
		// The response is parsed inside a try, for the reason ParseOperations just below parses the REQUEST inside
		// one: a body that is not this envelope makes JsonSerializer throw a message built for a developer — a
		// .NET type name and a byte offset — and this one reaches an agent, which cannot act on it. Worse, it
		// arrives on the one path where the caller most needs to know WHAT HAPPENED: a server-side serialization
		// failure returns a non-envelope body, so the write may or may not have landed and the exception says
		// nothing either way. Every other outcome here is already named (empty body, missing result, success
		// false); this was the one that leaked. Reported by manual testing on ENG-92713, hit by feeding a
		// described approval block back verbatim.
		ModifyProcessResponseEnvelope envelope;
		try {
			envelope = JsonSerializer.Deserialize<ModifyProcessResponseEnvelope>(responseBody, JsonOptions);
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				"ModifyProcess returned a response clio could not read, so whether the edit was applied is "
				+ "UNKNOWN — re-read the process with describe-business-process before deciding what to do. "
				+ "The parser detail is on the inner exception; it names a .NET type and a byte offset, which "
				+ "helps a developer reading a stack trace and is noise to the caller reading this.",
				exception);
		}

		envelope = envelope
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
		IReadOnlyList<ApprovalBlockExpectation.ApprovalExpectation> expectedApproval = ApprovalBlockExpectation.FromOperations(options.OperationsJson);
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
