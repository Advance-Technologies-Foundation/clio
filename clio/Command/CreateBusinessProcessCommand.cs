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
/// Options for building a business process from a declarative descriptor via the ProcessDesignService package.
/// Consumed by the MCP <c>create-business-process</c> tool, which sets these properties directly.
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
public sealed class CreateBusinessProcessOptions : EnvironmentOptions {
	/// <summary>Inline JSON process descriptor (name, caption, packageName, elements[], flows[], parameters[], mappings[]).</summary>
	public string DescriptorJson { get; set; } = string.Empty;

	/// <summary>Overrides the target package from the descriptor (the package the process is created in).</summary>
	public string PackageName { get; set; } = string.Empty;
}

/// <summary>
/// Builds a business process on a Creatio environment via the ProcessDesignService package.
/// </summary>
public interface ICreateBusinessProcessService {
	/// <summary>
	/// Builds and saves a business process from a declarative descriptor.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Build request (descriptor JSON content + optional overrides).</param>
	/// <returns>Structured build result with the created schema identity.</returns>
	CreateBusinessProcessResult BuildProcess(string environmentName, CreateBusinessProcessRequest request);
}

/// <summary>
/// Default ProcessDesignService-backed implementation of <see cref="ICreateBusinessProcessService"/>.
/// </summary>
public sealed class CreateBusinessProcessService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: ICreateBusinessProcessService {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public CreateBusinessProcessResult BuildProcess(string environmentName, CreateBusinessProcessRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.DescriptorJson)) {
			throw new ArgumentException("Process descriptor content is required.", nameof(request));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		JsonObject descriptor = ParseDescriptor(request.DescriptorJson);
		if (!string.IsNullOrWhiteSpace(request.PackageNameOverride)) {
			descriptor["packageName"] = request.PackageNameOverride;
		}

		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildProcess, environmentSettings);
		// ProcessDesignService uses BodyStyle=Wrapped: the descriptor is wrapped under a "request" property.
		string requestBody = new JsonObject { ["request"] = descriptor }.ToJsonString();
		logger.WriteInfo($"Building process '{descriptor["name"]}' on '{environmentName}'...");

		string responseBody = client.ExecutePostRequest(url, requestBody);
		// The response is parsed inside a try, for the reason ParseOperations just below parses the REQUEST inside
		// one: a body that is not this envelope makes JsonSerializer throw a message built for a developer — a
		// .NET type name and a byte offset — and this one reaches an agent, which cannot act on it. Worse, it
		// arrives on the one path where the caller most needs to know WHAT HAPPENED: a server-side serialization
		// failure returns a non-envelope body, so the write may or may not have landed and the exception says
		// nothing either way. Every other outcome here is already named (empty body, missing result, success
		// false); this was the one that leaked. Reported by manual testing on ENG-92713, hit by feeding a
		// described approval block back verbatim.
		BuildProcessResponseEnvelope envelope;
		try {
			envelope = JsonSerializer.Deserialize<BuildProcessResponseEnvelope>(responseBody, JsonOptions);
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				"BuildProcess returned a response clio could not read, so whether the process was created is "
				+ "UNKNOWN — re-read it with describe-business-process before deciding what to do. The parser "
				+ "detail is on the inner exception; it names a .NET type and a byte offset, which helps a "
				+ "developer reading a stack trace and is noise to the caller reading this.",
				exception);
		}

		envelope = envelope
			?? throw new InvalidOperationException("BuildProcess returned an empty response.");
		BuildProcessResultDto result = envelope.Result
			?? throw new InvalidOperationException("BuildProcess returned an unexpected response shape.");
		if (!result.Success) {
			throw new InvalidOperationException(result.ErrorMessage ?? "BuildProcess failed.");
		}

		return new CreateBusinessProcessResult(result.SchemaName, result.SchemaUId, result.Warnings);
	}

	private static JsonObject ParseDescriptor(string descriptorJson) {
		JsonNode? node;
		try {
			node = JsonNode.Parse(descriptorJson);
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				$"Process descriptor is not valid JSON: {exception.Message}", exception);
		}

		return node as JsonObject
			?? throw new InvalidOperationException("Process descriptor must be a JSON object.");
	}

	#region DTOs (wire shape)

	private sealed class BuildProcessResponseEnvelope {
		[JsonPropertyName("BuildProcessResult")]
		public BuildProcessResultDto? Result { get; set; }
	}

	private sealed class BuildProcessResultDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("schemaUId")]
		public string? SchemaUId { get; set; }

		[JsonPropertyName("schemaName")]
		public string? SchemaName { get; set; }

		[JsonPropertyName("errorMessage")]
		public string? ErrorMessage { get; set; }

		[JsonPropertyName("warnings")]
		public List<string>? Warnings { get; set; }
	}

	#endregion
}

/// <summary>
/// Builds a business process from an inline JSON descriptor and prints the structured result.
/// </summary>
public class CreateBusinessProcessCommand(
	ICreateBusinessProcessService createBusinessProcessService,
	IProcessDescriber processDescriber,
	ILogger logger)
	: Command<CreateBusinessProcessOptions> {
	/// <inheritdoc />
	public override int Execute(CreateBusinessProcessOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			if (string.IsNullOrWhiteSpace(options.DescriptorJson)) {
				throw new InvalidOperationException("A process descriptor is required.");
			}

			CreateBusinessProcessResult result = createBusinessProcessService.BuildProcess(
				options.Environment,
				new CreateBusinessProcessRequest(options.DescriptorJson, options.PackageName));
			logger.WriteInfo($"Process '{result.SchemaName}' created (UId: {result.SchemaUId}).");
			// Written as WARNINGS on a SUCCESSFUL build, the same way the modify path reports its own: these are
			// outcomes that applied and are not what a caller would assume, so dropping them would leave the caller
			// believing the formula was checked when it was not.
			foreach (string warning in result.Warnings ?? []) {
				logger.WriteWarning(warning);
			}
			// Renamed from WarnOnDiscardedEmailBlocks when it grew to cover the approval block too: both are
			// discarded the same silent way by a server that predates them, so they share one read-back.
			WarnOnDiscardedConfigurationBlocks(options, result.SchemaName);
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	// A server that predates sendEmail DISCARDS an email block and still answers success:true, so a build can
	// report a configured email element that is in fact empty. Read the saved process back and say so when the
	// block did not land. Only runs when the descriptor actually carried a block, so the ordinary path pays
	// nothing; a failure to verify is never escalated, because an unreadable description is not evidence of a
	// dropped block. See EmailBlockExpectation for why this is behavioural rather than version-based.
	private void WarnOnDiscardedConfigurationBlocks(CreateBusinessProcessOptions options, string? schemaName) {
		IReadOnlyList<string> expected = EmailBlockExpectation.FromDescriptor(options.DescriptorJson);
		// The Approval element has exactly the same silent-drop failure, so it is verified from the SAME read-back
		// rather than a second one — the describe below is the expensive part, and one of the two blocks being
		// absent is no reason to skip the other's check.
		IReadOnlyList<ApprovalBlockExpectation.ApprovalExpectation> expectedApproval = ApprovalBlockExpectation.FromDescriptor(options.DescriptorJson);
		// BOTH must be empty to skip. An || here would stop verifying approval on every payload without an email
		// block, which is most of them, and nothing downstream would notice — pinned by
		// Execute_ShouldStillVerifyApproval_WhenTheDescriptorCarriesNoEmailBlock.
		if ((expected.Count == 0 && expectedApproval.Count == 0) || string.IsNullOrWhiteSpace(schemaName)) {
			return;
		}

		ErrorOr<DescribeProcessResult> described =
			processDescriber.Describe(new ProcessIdentity(schemaName, null, null), null);
		if (described.IsError) {
			return;
		}

		string? dropped = EmailBlockExpectation.BuildWarning(
			EmailBlockExpectation.Missing(described.Value, expected));
		if (dropped is not null) {
			logger.WriteWarning(dropped);
		}

		string? droppedApproval = ApprovalBlockExpectation.BuildWarning(
			ApprovalBlockExpectation.Missing(described.Value, expectedApproval));
		if (droppedApproval is not null) {
			logger.WriteWarning(droppedApproval);
		}

		// A package that predates the body-macro feature stores the [[…]] placeholders verbatim and still answers
		// success, so the read-back is the only place the un-resolved body surfaces — the element reports a body but
		// describe returns none (a healthy build decodes the tokens back into a non-null [[…]] body, so the presence
		// of [[ in the read-back is NOT the signal). Reuses the description already pulled above.
		string? unresolved = EmailBlockExpectation.BuildMacroWarning(
			EmailBlockExpectation.UnresolvedBodyMacros(
				described.Value, EmailBlockExpectation.MacroBodyElements(options.DescriptorJson)));
		if (unresolved is not null) {
			logger.WriteWarning(unresolved);
		}
	}
}

/// <summary>
/// Request payload for building a business process.
/// </summary>
/// <param name="DescriptorJson">The JSON descriptor content (BuildProcessRequest shape).</param>
/// <param name="PackageNameOverride">Optional package name that overrides the descriptor's <c>packageName</c>.</param>
public sealed record CreateBusinessProcessRequest(string DescriptorJson, string? PackageNameOverride = null);

/// <summary>
/// Structured result of a business-process build.
/// </summary>
/// <param name="SchemaName">Final schema name of the created process.</param>
/// <param name="SchemaUId">UId of the created process schema.</param>
/// <param name="Warnings">
/// Caveats about a build that SUCCEEDED. The two cases this used to name — an unrecognised macro family, and
/// an expression whose macros could not be resolved so its result type went unchecked — are GONE: they were the
/// package's own formula notices and went with its validator, and an unrecognised family is now REFUSED by the
/// platform's pre-save gate rather than warned about. What still arrives here is the connection notices
/// (a column that is not registered, one resolved from a system setting, one CLEARED by a retarget) and the
/// pre-configured-page sync notices. <c>null</c> or empty when there are none, and always <c>null</c> against a
/// server predating the member — which is why the two tests on this member assert the absent case separately.
/// </param>
public sealed record CreateBusinessProcessResult(string? SchemaName, string? SchemaUId,
	IReadOnlyList<string>? Warnings = null);
