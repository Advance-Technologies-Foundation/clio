using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.CreatioModel;
using ErrorOr;

namespace Clio.Command.ProcessModel;

/// <summary>Identifies a process by exactly one of code (Name), UId, or caption.</summary>
/// <param name="Code">Process code (schema Name), e.g. <c>UsrProcess_493d4c9</c>.</param>
/// <param name="UId">Process UId (GUID string).</param>
/// <param name="Caption">Process caption (display name).</param>
public sealed record ProcessIdentity(string Code, string UId, string Caption);

/// <summary>
/// Reads an existing process into a structured graph via the server-side <c>ProcessDesignService</c> package.
/// Element typing comes from the real object model (incl. the specific user-task schema name and parameter
/// value sources), so it is universal — no client-side GUID taxonomy. Requires the <c>CrtProcessBuilder</c>
/// package on the target environment.
/// </summary>
public interface IProcessDescriber {
	/// <summary>
	/// Resolves the process by the supplied identity and returns its server-built structured description.
	/// </summary>
	/// <param name="identity">The process identity (exactly one of code/uid/caption populated).</param>
	/// <param name="culture">Optional culture used to resolve localized captions.</param>
	/// <param name="includeVersionFacts">
	/// Whether to overlay the process library's version facts. Opt-OUT rather than opt-in because the
	/// describe command, whose output publishes them, must never be able to lose them by omission; the two
	/// read-back callers that consume <c>elements[]</c> alone pass <c>false</c>, since for them the facts cost
	/// an ATF session plus two DataService round-trips on a WRITE path and are then discarded.
	/// </param>
	/// <param name="bestEffort">
	/// Whether this read is a VERIFICATION of a write that already landed, in which case it gets one
	/// attempt and a short timeout instead of the full retry budget. A verification runs on the SUCCESS
	/// path - the caller treats an error as a caveat, never as a failure, because the write is committed
	/// either way - so spending three attempts at ten seconds stalls the common case to establish
	/// something the caller will report as unverified anyway. The population that pays the whole budget
	/// is the one these guards target: an environment whose DescribeProcess route is failing.
	/// </param>
	/// <returns>The structured description, or an error (not found / unreachable / server failure).</returns>
	ErrorOr<DescribeProcessResult> Describe(ProcessIdentity identity, string culture,
		bool includeVersionFacts = true, bool bestEffort = false);
}

/// <inheritdoc cref="IProcessDescriber" />
public sealed class ServerProcessDescriber(
	IApplicationClient applicationClient,
	IDataProvider dataProvider,
	IServiceUrlBuilder serviceUrlBuilder,
	IProcessVersionLibReader versionLibReader) : IProcessDescriber {

	private const string DescribeErrorCode = "DescribeProcess";

	/// <summary>
	/// Upper bound on the caption candidates fetched for ranking.
	/// </summary>
	/// <remarks>
	/// The same number as <see cref="ProcessVersionLibReader.FamilyCap"/> and for the same stated reason: a
	/// caption belongs to a whole version family, so this read has the same unbounded-response risk on the
	/// same view. Above it the refusal message would also grow with the family, and it is returned into an
	/// MCP response as agent context.
	/// </remarks>
	private const int CaptionCandidateCap = ProcessVersionLibReader.FamilyCap;

	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <summary>
	/// Version facts nobody asked for.
	/// </summary>
	/// <remarks>
	/// Assigned rather than left alone on the opt-out path, so ADR choice 5's invariant survives it: the wire
	/// result deserializes into the same type as the read model, and a newer <c>CrtProcessBuilder</c> that
	/// already returns a <c>version</c> key would otherwise leave a server-supplied value standing while
	/// nothing in clio established it. It carries a warning because the one-directional contract on
	/// <see cref="DescribeProcessResult.VersionReadWarning"/> admits no absent value without one.
	/// </remarks>
	private static readonly ProcessVersionFacts VersionFactsNotRequested = new() {
		Warning = "the version facts were not requested for this read, so they were not established"
	};

	/// <inheritdoc />
	public ErrorOr<DescribeProcessResult> Describe(ProcessIdentity identity, string culture,
		bool includeVersionFacts = true, bool bestEffort = false) {
		ErrorOr<ResolvedIdentity> resolved = BuildIdentityPayload(identity);
		if (resolved.IsError) {
			return resolved.Errors;
		}
		JsonObject requestObject = resolved.Value.Payload;
		if (!string.IsNullOrWhiteSpace(culture)) {
			requestObject["culture"] = culture;
		}

		string body = new JsonObject { ["request"] = requestObject }.ToJsonString();
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DescribeProcess);
		string responseBody;
		try {
			// A verification read-back is paid on the success path of EVERY labelled build and every
			// relabelling edit - the common case, since the create tool tells an agent to label every
			// branch. The full budget is three attempts at ten seconds, so a wedged environment stalls a
			// write that already committed for about half a minute and then reports only "could not
			// verify". One attempt at half the timeout is sound HERE and nowhere else: the caller already
			// treats IsError as a caveat rather than a failure.
			(int timeoutMs, int attempts) = bestEffort ? (5_000, 1) : (10_000, 3);
			responseBody = applicationClient.ExecutePostRequest(url, body, timeoutMs, attempts, 1);
		} catch (Exception e) {
			return Error.Failure(DescribeErrorCode, e.Message);
		}
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return Error.Failure(DescribeErrorCode, "empty response from the server");
		}

		DescribeProcessResultEnvelope envelope;
		try {
			envelope = JsonSerializer.Deserialize<DescribeProcessResultEnvelope>(responseBody, JsonOptions);
		} catch (JsonException e) {
			return Error.Failure(DescribeErrorCode, $"could not parse server response: {e.Message}");
		}

		DescribeProcessWireResult result = envelope?.Result;
		if (result is null) {
			return Error.Failure(DescribeErrorCode, "unexpected server response shape");
		}
		if (!result.Success) {
			return Error.Failure(DescribeErrorCode, result.ErrorMessage ?? "describe-business-process failed on the server");
		}
		// Version facts are overlaid only here, after every refusal path: a described graph without them is
		// still a correct answer, but a version warning attached to an error would be noise.
		ApplyVersionFacts(result, includeVersionFacts
			? ReadVersionFacts(resolved.Value.Row, result.SchemaUId)
			: VersionFactsNotRequested);
		return result;
	}

	/// <summary>
	/// Reads the version facts, reusing the row the caption arm already fetched when it is the same schema.
	/// </summary>
	/// <remarks>
	/// The caption arm has already read this schema's <c>VwProcessLib</c> row, which carries every field the
	/// facts need about the schema itself, so re-fetching it by UId is a round-trip that establishes nothing.
	/// The UId comparison is the guard that makes the reuse safe: describe was asked by the resolved NAME, so
	/// a server that answered for a different schema than the caption resolved to must not have the old row's
	/// version and active-version flag reported against it.
	/// </remarks>
	private ProcessVersionFacts ReadVersionFacts(VwProcessLib resolvedRow, string describedSchemaUId) =>
		resolvedRow is not null
		&& Guid.TryParse(describedSchemaUId, out Guid describedUId)
		&& resolvedRow.UId == describedUId
			? versionLibReader.Read(resolvedRow)
			: versionLibReader.Read(describedSchemaUId);

	/// <summary>
	/// Overlays the process library's version facts onto a described graph.
	/// </summary>
	/// <remarks>
	/// Every member is assigned, the failure path included. The wire result deserializes into this same type,
	/// so a newer <c>CrtProcessBuilder</c> that already returns a <c>version</c> key would otherwise bind it
	/// to the property and leave a server-supplied value standing next to a warning that says the facts were
	/// NOT established. The process library is the authority for now (ADR choice 5), and <c>activeVersionSource</c>
	/// says so in the output. The day the server becomes the authority, this method is what has to change —
	/// and <see cref="DescribedProcessVersion"/> then needs an overflow bag, which it does not need while
	/// clio is the only thing that builds those entries.
	/// </remarks>
	private static void ApplyVersionFacts(DescribeProcessResult result, ProcessVersionFacts facts) {
		result.Version = facts.Version;
		result.IsActiveVersion = facts.IsActiveVersion;
		result.ActiveVersionSchemaUId = facts.ActiveVersionSchemaUId;
		result.ActiveVersionName = facts.ActiveVersionName;
		result.VersionRootSchemaUId = facts.VersionRootSchemaUId;
		result.ActiveVersionSource = facts.ActiveVersionSource;
		result.VersionReadWarning = facts.Warning;
		result.Versions = facts.Versions?.Select(ToDescribedVersion).ToList();
		// Reported as the length actually published rather than as the reader's cap, so the number can never
		// disagree with the list it describes.
		result.VersionsTruncatedAt = facts.FamilyTruncated ? result.Versions?.Count : null;
	}

	private static DescribedProcessVersion ToDescribedVersion(ProcessVersionFamilyMember member) =>
		new() {
			SchemaUId = member.SchemaUId,
			Name = member.Name,
			Caption = member.Caption,
			Version = member.Version,
			IsActiveVersion = member.IsActiveVersion,
			IsRoot = member.IsRoot,
			PackageUId = member.PackageUId,
			Enabled = member.Enabled
		};

	/// <summary>The describe request payload, plus the process-library row the caption arm resolved.</summary>
	/// <param name="Payload">The identity payload the server is asked with.</param>
	/// <param name="Row">The resolved row on the caption path; <c>null</c> on the name and uid paths.</param>
	private sealed record ResolvedIdentity(JsonObject Payload, VwProcessLib Row);

	private ErrorOr<ResolvedIdentity> BuildIdentityPayload(ProcessIdentity identity) {
		if (!string.IsNullOrWhiteSpace(identity.UId)) {
			return new ResolvedIdentity(new JsonObject { ["uid"] = identity.UId.Trim() }, null);
		}
		if (!string.IsNullOrWhiteSpace(identity.Code)) {
			return new ResolvedIdentity(new JsonObject { ["name"] = identity.Code.Trim() }, null);
		}
		if (!string.IsNullOrWhiteSpace(identity.Caption)) {
			return ResolveCaption(identity.Caption);
		}
		return Error.Failure("ResolveId", "no process identity provided (code, uid, or caption)");
	}

	/// <summary>
	/// Resolves a display caption to a process code through the shared <see cref="ProcessLibResolver"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Describe and <c>generate-process-model</c> deliberately share one selection policy: a caption belongs
	/// to a whole version family, because every version is a separate schema with its own name but the SAME
	/// caption. This path previously took the first row the query returned, which on a versioned process is
	/// an arbitrary family member — frequently version 0, which is exactly the graph nobody runs.
	/// </para>
	/// <para>
	/// The resolver's error vocabulary is translated into this surface's own <c>ResolveId</c> code rather than
	/// forwarded, so the not-found message stays what it has always been. The ambiguity error IS new here and
	/// is the deliberate price of no longer answering for an arbitrary member: two processes that genuinely
	/// share a caption now ask the caller for a code instead of silently picking one.
	/// </para>
	/// </remarks>
	private ErrorOr<ResolvedIdentity> ResolveCaption(string caption) =>
		ProcessLibRead.Guarded<ErrorOr<ResolvedIdentity>>(() => {
			IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(dataProvider);
			// All matches, not the first: the policy needs the candidate set to tell one family from two
			// processes. Cheap because the model no longer declares the metadata blob (ENG-94374 story 1).
			//
			// Bounded for the reason ProcessVersionLibReader.FamilyCap states about the family read — same
			// view, same deadline-bounded surface — and one row over the cap so an overflow can be REFUSED
			// rather than ranked out of a set that is silently partial. Above the cap the resolver cannot
			// honestly prove the candidates are one family, which is the same choice BuildFacts makes when
			// the active member falls outside its own cap.
			List<VwProcessLib> byCaption = ctx.Models<VwProcessLib>()
				.Where(p => p.Caption == caption)
				.Take(CaptionCandidateCap + 1)
				.ToList();
			if (byCaption.Count > CaptionCandidateCap) {
				return Error.Failure("ResolveId",
					$"caption '{caption}' matches more than {CaptionCandidateCap} schemas, which is more "
					+ "candidates than can be ranked into one version family. Re-run with the exact process "
					+ "code.");
			}
			ErrorOr<VwProcessLib> resolved = ProcessLibResolver.Resolve(caption, byName: null, byCaption);
			if (resolved.IsError) {
				return resolved.FirstError.Type == ErrorType.NotFound
					? Error.Failure("ResolveId", $"process not found (caption '{caption}')")
					: Error.Failure("ResolveId", resolved.FirstError.Description);
			}
			return new ResolvedIdentity(new JsonObject { ["name"] = resolved.Value.Name }, resolved.Value);
		}, e => Error.Failure("ResolveId", e.Message));

	/// <summary>WCF <c>BodyStyle=Wrapped</c> response envelope (wire-only).</summary>
	private sealed class DescribeProcessResultEnvelope {
		[JsonPropertyName("DescribeProcessResult")]
		public DescribeProcessWireResult Result { get; set; }
	}

	/// <summary>
	/// Wire shape: the public <see cref="DescribeProcessResult"/> graph plus the server-internal success/error
	/// control fields. They are read here to detect failure and are never re-serialized into the command output
	/// (the command serializes the value as <see cref="DescribeProcessResult"/>, so these are dropped).
	/// </summary>
	private sealed class DescribeProcessWireResult : DescribeProcessResult {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorMessage")]
		public string ErrorMessage { get; set; }
	}
}

#region DTOs (server wire shape — DescribeProcessResult is re-serialized verbatim as the command output)

/// <summary>The structured process description returned by the server-side <c>DescribeProcess</c>.</summary>
public class DescribeProcessResult {
	/// <summary>Process schema name (code).</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>Process caption.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>Process schema UId.</summary>
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	/// <summary>
	/// This schema's own version number, or absent when the version facts could not be established.
	/// </summary>
	/// <remarks>
	/// 0 is a real answer, and the implication runs ONE way only: 0 means the schema is a family root,
	/// while a root is NOT obliged to be 0. Measured on core 10.1.448.0 (ENG-94374 story 8, 2026-09-03):
	/// two parentless process schemas carry 2 and 1, and no parented schema carries 0 — the number is a
	/// stamped property, not one derived from family membership. So this field never settles whether a
	/// process HAS versions; the length of <see cref="Versions"/> does. Absence is a third answer entirely:
	/// NOT ESTABLISHED, with <see cref="VersionReadWarning"/> naming the fact that was missing.
	/// </remarks>
	[JsonPropertyName("version")]
	public int? Version { get; set; }

	/// <summary>Whether this schema is the version the process library reports as active.</summary>
	[JsonPropertyName("isActiveVersion")]
	public bool? IsActiveVersion { get; set; }

	/// <summary>Schema UId of the version the process library reports as active.</summary>
	[JsonPropertyName("activeVersionSchemaUId")]
	public string ActiveVersionSchemaUId { get; set; }

	/// <summary>Schema name of the active version, so it can be re-described without a second lookup.</summary>
	[JsonPropertyName("activeVersionName")]
	public string ActiveVersionName { get; set; }

	/// <summary>Schema UId of the version-family root, which is the identity the family is keyed on.</summary>
	[JsonPropertyName("versionRootSchemaUId")]
	public string VersionRootSchemaUId { get; set; }

	/// <summary>
	/// Which authority established the active version — <c>process-library-view</c> whenever the facts were
	/// established, and ABSENT alongside <see cref="VersionReadWarning"/> on every answer that established
	/// none, so its presence is itself the signal that an authority answered. Stated rather than implied,
	/// because the runtime consults the schema manager instead of this view and the two can rank a tied
	/// family differently.
	/// </summary>
	[JsonPropertyName("activeVersionSource")]
	public string ActiveVersionSource { get; set; }

	/// <summary>
	/// The version family, ascending by version. Absent, never empty, when it could not be established:
	/// an empty list reads as "checked, and there are no versions", which is a different claim.
	/// </summary>
	[JsonPropertyName("versions")]
	public List<DescribedProcessVersion> Versions { get; set; }

	/// <summary>
	/// How many members <see cref="Versions"/> was cut to, when the family was longer than the reader's
	/// cap. Absent for a complete family, so its presence is what tells a caller the list is partial.
	/// </summary>
	[JsonPropertyName("versionsTruncatedAt")]
	public int? VersionsTruncatedAt { get; set; }

	/// <summary>
	/// Why a version fact is missing. The invariant is one-directional: a missing value ALWAYS comes with
	/// this warning naming the fact that could not be established, while a successful read carries the
	/// schema's own stamped number and no warning — commonly 0, and 0 does NOT mean the family has no
	/// versions, since the root of a family with versions 1-3 reports 0 as well. Family size is answered by
	/// <see cref="Versions"/>, never by the number.
	/// </summary>
	/// <remarks>
	/// It is NOT limited to a read that failed outright. A read can succeed and establish less than
	/// everything — no member flagged active, two flagged, a NULL column — and then this warning arrives
	/// ALONGSIDE the values that were established. A caller that treats its presence as "no version data"
	/// discards facts it has.
	/// </remarks>
	[JsonPropertyName("versionReadWarning")]
	public string VersionReadWarning { get; set; }

	/// <summary>Process nodes (events, tasks, gateways) — everything except sequence flows.</summary>
	[JsonPropertyName("elements")]
	public List<DescribedElement> Elements { get; set; }

	/// <summary>Sequence flows between nodes.</summary>
	[JsonPropertyName("flows")]
	public List<DescribedFlow> Flows { get; set; }

	/// <summary>Process-level parameters (inputs / variables).</summary>
	[JsonPropertyName("parameters")]
	public List<DescribedParameter> Parameters { get; set; }

	/// <summary>
	/// Captures every other field the server returns at the graph root so the description round-trips
	/// losslessly: a newer <c>CrtProcessBuilder</c> reporting something this build does not declare reaches the
	/// command output verbatim instead of being discarded without a trace.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>One member of the described process's version family.</summary>
/// <remarks>
/// Carries no <c>[JsonExtensionData]</c> bag, unlike the graph types around it, because clio builds every
/// entry itself from the process library — there is no server field here that could be dropped. That stops
/// being true the day the server reports the family: see <c>ApplyVersionFacts</c>.
/// </remarks>
public sealed class DescribedProcessVersion {

	/// <summary>Schema UId of this version.</summary>
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	/// <summary>Schema name. Every version of a process has a different one.</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>Display caption. Every version of a process usually has the SAME one.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>Version number, or absent when the process library could not establish it.</summary>
	[JsonPropertyName("version")]
	public int? Version { get; set; }

	/// <summary>Whether this member is the version the process library reports as active.</summary>
	[JsonPropertyName("isActiveVersion")]
	public bool? IsActiveVersion { get; set; }

	/// <summary>
	/// Whether this member is the family root. The ONLY field that answers that: a root's own version number
	/// is stamped rather than derived, so it is commonly 0 but stock content carries roots numbered 1 and 2,
	/// and <see cref="Version"/> therefore cannot be used to identify one.
	/// </summary>
	[JsonPropertyName("isRoot")]
	public bool IsRoot { get; set; }

	/// <summary>UId of the package this version lives in.</summary>
	[JsonPropertyName("packageUId")]
	public string PackageUId { get; set; }

	/// <summary>
	/// Whether the process is enabled. This is FAMILY state, not per-version state: the platform keys
	/// enable/disable on the root schema, so every member of a family reports the same value.
	/// </summary>
	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; }
}

/// <summary>A process node read back from the schema.</summary>
public sealed class DescribedElement {
	/// <summary>
	/// Element local handle (the schema element <c>Name</c>, a string code) — the value flows
	/// (<c>source</c>/<c>target</c>) and mappings (<c>elementName</c>) reference. Creatio identifies an
	/// element by this <c>Name</c> plus the <c>UId</c> GUID; the platform reserves "Id" for the GUID, so
	/// the handle is <c>name</c>, not <c>id</c>.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>Element UId (the schema element's unique identifier).</summary>
	[JsonPropertyName("uid")]
	public string Uid { get; set; }

	/// <summary>Localized caption.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>Runtime class name (for example <c>ProcessSchemaUserTask</c>, <c>ProcessSchemaStartEvent</c>).</summary>
	[JsonPropertyName("type")]
	public string Type { get; set; }

	/// <summary>
	/// The descriptor <c>type</c> token to feed back into <c>create-business-process</c> / <c>modify-business-process</c>
	/// for this element (for example <c>usertask</c>, <c>endevent</c>, <c>signalstart</c>, <c>startevent</c>) — the
	/// round-trippable counterpart of <see cref="Type"/> (which is the non-consumable .NET class name). For a user
	/// task this is the generic <c>usertask</c> token; the specific task is in <see cref="UserTaskName"/>.
	/// </summary>
	[JsonPropertyName("buildType")]
	public string BuildType { get; set; }

	/// <summary>For user-task elements: the referenced user-task schema name (for example <c>ReadDataUserTask</c>).</summary>
	[JsonPropertyName("userTaskName")]
	public string UserTaskName { get; set; }

	/// <summary>
	/// The palette item identity the designer uses to pick the element's editor. For a dedicated user-task
	/// element (e.g. "Perform task") this equals the user-task schema UId; the generic "User task" container
	/// uses a fixed shared UId.
	/// </summary>
	[JsonPropertyName("managerItemUId")]
	public string ManagerItemUId { get; set; }

	/// <summary>Diagram position "X;Y".</summary>
	[JsonPropertyName("position")]
	public string Position { get; set; }

	/// <summary>
	/// Whether the element runs in background mode — a platform property of EVERY process element, so it is reported
	/// for all of them and round-trips into a <c>create</c>/<c>modify</c> <c>useBackgroundMode</c>. Omitted (null) when
	/// the server (an older <c>CrtProcessBuilder</c>) does not report it. Effective only while the global
	/// <c>UseBackgroundProcessMode</c> application setting is enabled (it is by default).
	/// </summary>
	[JsonPropertyName("useBackgroundMode")]
	public bool? UseBackgroundMode { get; set; }

	/// <summary>The element's value-bearing parameters (mapping / constant / formula).</summary>
	[JsonPropertyName("parameters")]
	public List<DescribedParameter> Parameters { get; set; }

	/// <summary>For a signal start element: the record-event trigger (entity + change type). Null otherwise.</summary>
	[JsonPropertyName("signal")]
	public DescribedSignal Signal { get; set; }

	/// <summary>
	/// The element's data source filter (a signal start's <c>EntityFilters</c> or a data-operation element's
	/// <c>DataSourceFilters</c>), decoded server-side into the high-level shape; <c>null</c> when the element
	/// carries no filter. Round-trips into a <c>create</c>/<c>modify</c> <c>filter</c> descriptor.
	/// </summary>
	[JsonPropertyName("filter")]
	public DescribedFilter Filter { get; set; }

	/// <summary>
	/// For a Send email element (<c>EmailTemplateUserTask</c>): its configuration decoded back into the descriptor
	/// vocabulary. <c>null</c> for other element kinds and when the server (an older <c>CrtProcessBuilder</c>) does
	/// not report it. Round-trips into a <c>create</c>/<c>modify</c> <c>email</c> block with TWO qualifications: a
	/// FORMULA subject is reported for reading only — describe echoes the parameter's stored value whatever its
	/// source, and feeding a <c>[#...#]</c> subject back is refused by the write path's constant guard, so re-enter
	/// it through <c>addMapping</c> with an <c>expression</c> source; and recipients re-enter as MATCH-OR-APPEND
	/// entries (an identical one is a no-op, a new one appends, none can be removed).
	/// <para>The block reports EFFECTIVE values: a field the element inherits untouched from the
	/// <c>EmailTemplateUserTask</c> schema (a platform default such as <c>ignoreErrors</c>) comes back like a
	/// configured one, because it is the value the element will actually use. So the block's PRESENCE is not
	/// evidence that anyone configured this element — do not read "importance is already normal" as a deliberate
	/// choice, and do not use "no block" as the unconfigured signal.</para>
	/// </summary>
	[JsonPropertyName("email")]
	public DescribedEmail Email { get; set; }

	/// <summary>
	/// For an Open edit page element (<c>OpenEditPageUserTask</c>): its configuration decoded back into the
	/// descriptor vocabulary. <c>null</c> for other element kinds, for an element that stores no page (placed but
	/// never configured), and when the server is an older <c>CrtProcessBuilder</c> that does not report it.
	/// Round-trips into a <c>create</c>/<c>modify</c> <c>openEditPage</c> block, with one asymmetry: the write path
	/// refuses pre-filled values together with a record, while this read reports BOTH when the schema carries them —
	/// the runtime applies stored values in either editing mode, so hiding one would hide live configuration. Drop
	/// the one that does not belong to the reported <c>editMode</c> before re-applying. One field is also reshaped
	/// rather than dropped: <c>completionMode</c> is reported FLAT and is written NESTED, so re-apply it as
	/// <c>completion:{mode:…}</c>. Feeding the flat key back leaves the element on its stored mode while its filter
	/// stays — the mismatched pair the write contract warns about.
	/// </summary>
	[JsonPropertyName("openEditPage")]
	public DescribedOpenEditPage OpenEditPage { get; set; }

	/// For a Pre-configured page element (<c>PreconfiguredPageUserTask</c>): the referenced page and the element's
	/// configuration, decoded back into the descriptor vocabulary. <c>null</c> for other element kinds, when the
	/// element references no page yet, and when the server (an older <c>CrtProcessBuilder</c>) does not report it.
	/// <para>The block MUST be declared here even though nothing in clio reads its fields: the describe output is
	/// re-serialized from this model, so a member the model does not declare is dropped on the way to the caller.
	/// That is how the block reached nobody before this property existed.</para>
	/// </summary>
	[JsonPropertyName("preconfiguredPage")]
	public DescribedPreconfiguredPage PreconfiguredPage { get; set; }

	/// <summary>
	/// The element's BOUND host-entity connections ("Connected to") — which records the Activity it creates is
	/// attached to. <c>null</c> when the element has none, and also when the server is an older
	/// <c>CrtProcessBuilder</c> that does not report them.
	/// </summary>
	[JsonPropertyName("connections")]
	public List<DescribedConnection> Connections { get; set; }

	/// <summary>
	/// For a user-task element: whether the referenced user-task schema is RETIRED by the platform. <c>null</c> when
	/// the element is not a user task (or the server does not report it); <c>false</c> means no retirement marker was
	/// found. Reported, never enforced on a read — a legacy process must stay readable.
	/// </summary>
	[JsonPropertyName("deprecated")]
	public bool? Deprecated { get; set; }

	/// <summary>
	/// For a Perform task element (<c>ActivityUserTask</c>): the performer ("Who performs the task?") read back
	/// from its performer-assignment options — <c>user</c> / <c>manager</c> / <c>role</c> with the stored contact
	/// or role formula. Round-trips into a create/modify <c>performer</c> block. <c>null</c> when the element
	/// carries no assignment, for other element kinds, and when the server (an older <c>CrtProcessBuilder</c>)
	/// does not report it. A Send email element reports its manual-mode performer inside <see cref="Email"/>
	/// instead — one platform mechanism, two report sites matching the two write sites.
	/// </summary>
	[JsonPropertyName("performer")]
	public DescribedPerformer Performer { get; set; }

	/// <summary>
	/// For a user-task element: whether connections on THIS element would be written at run time. <c>false</c> is the
	/// answer that matters — it marks a process whose connections persist, compile and run green while writing
	/// nothing — and it has TWO causes with different fixes: the user task's runtime never writes connections
	/// (change the element kind), or this element's activity-creation gate is shut (set <c>CreateActivity</c> to a
	/// constant true — or, on a Send email element, switch it to manual send, which creates the activity
	/// unconditionally and needs no <c>CreateActivity</c> write). <c>null</c> means NOT ESTABLISHED (not a user task, an unresolvable schema, or a user task
	/// outside the supported set), so it is not a licence either. Both <c>false</c> and <c>null</c> mean
	/// <c>setConnections</c> is refused on that element; only <c>true</c> means it is accepted.
	/// </summary>
	[JsonPropertyName("writesConnectionsAtRuntime")]
	public bool? WritesConnectionsAtRuntime { get; set; }

	/// <summary>
	/// For an Approval element (<c>ApprovalUserTask</c>): its configuration decoded back into the descriptor
	/// vocabulary. <c>null</c> for other element kinds and when the server (an older <c>CrtProcessBuilder</c>) does
	/// not report it.
	/// <para>Unlike <see cref="Email"/> this reports what is WRITTEN, not the effective value, so an unconfigured
	/// Approval reports no block at all. <c>ignoreEmailErrors</c> is the one field where that costs the server real
	/// work: <c>ApprovalUserTask</c> DECLARES a schema-level default of <c>true</c>, the platform copies it onto
	/// every element, and it arrives carrying <c>Source = ConstValue</c> — indistinguishable from a written value
	/// unless the reader also compares which schema last modified it. A server that skips that check reports
	/// <c>ignoreEmailErrors: true</c> on an element nobody configured (and, because one reported field is enough to
	/// count as configured, reports a block for it at all). So on a current server absence means "not written",
	/// never "off" — and the element still USES the platform default at run time, which is why the designer's card
	/// shows the box ticked while this reports nothing. Clearing a notification likewise resets its template to no
	/// source, which makes a CLEARED template indistinguishable from one that was never set.</para>
	/// <para>Every VALUE reported here is one a write accepts — an employee macro, a template's lookup macro, a
	/// record id — but the SHAPE is not the write shape and the block does NOT re-apply verbatim. This reports flat
	/// (<c>approverType</c> + <c>approverEmployee</c>; a boolean <c>notifyApprover</c> with
	/// <c>approverEmailTemplate</c> beside it; <c>recordId</c> as a string), while <c>create</c>/<c>modify</c> take
	/// nested (<c>approver: {type, employee}</c>, <c>notifyApprover: {emailTemplate}</c>, <c>recordId</c> as an
	/// object). Flat-with-<c>Display</c>-companions is the house convention every element follows — <c>sender</c> /
	/// <c>senderDisplay</c> on email is the same — so the shape is right; it just has to be TRANSLATED before it is
	/// sent back. Feeding a described block in unchanged binds nothing:
	/// <c>DataContractJsonSerializer</c> drops the flat members and the operation still answers success, which is
	/// why <c>ApprovalBlockExpectation</c> raises a warning of its own for a describe-shaped request.</para>
	/// <para>The APPROVER is reported as <see cref="DescribedApproval.ApproverType"/> plus only the companion field
	/// that type uses. A stored value belonging to the OTHER branch — which an element the designer never re-saved
	/// can still carry — is deliberately not reported, and an unrecognized type code reports no approver at all
	/// rather than a token a write would refuse. The element's outcome stays in <see cref="Parameters"/> as the
	/// <c>ResultParameter</c> output.</para>
	/// </summary>
	[JsonPropertyName("approval")]
	public DescribedApproval Approval { get; set; }

	/// <summary>
	/// Captures every other field the server reports on an element so the description round-trips losslessly:
	/// a newer <c>CrtProcessBuilder</c> reporting a block this build does not declare reaches the command output
	/// verbatim instead of being discarded without a trace.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>
/// The configuration of an Approval element, read back from its parameters in the same vocabulary the
/// <c>approval</c> block accepts.
/// </summary>
public sealed class DescribedApproval {
	/// <summary>"Approval purpose" — the text shown to the approver.</summary>
	[JsonPropertyName("purpose")]
	public string Purpose { get; set; }

	/// <summary>"Approval object" — the resolved object NAME, the field to resubmit as <c>object</c>.</summary>
	[JsonPropertyName("object")]
	public string Object { get; set; }

	/// <summary>The approval object's entity schema UId as stored. Reported for traceability only.</summary>
	[JsonPropertyName("objectUId")]
	public string ObjectUId { get; set; }

	/// <summary>
	/// "Record Id" — the stored value as a STRING, which is not the shape the write contract takes: there
	/// <c>recordId</c> is an OBJECT naming one of <c>recordId</c> / <c>processParameter</c> /
	/// <c>sourceElement</c>+<c>sourceElementParameter</c>. A fixed record comes back as its <c>[#Lookup…#]</c>
	/// macro, which that object's own <c>recordId</c> member accepts verbatim; every other source comes back as
	/// its raw macro and has to be translated back into the member it came from.
	/// </summary>
	[JsonPropertyName("recordId")]
	public string RecordId { get; set; }

	/// <summary>The fixed record's display value, when the platform stored one. Read-only.</summary>
	[JsonPropertyName("recordIdDisplay")]
	public string RecordIdDisplay { get; set; }

	/// <summary>"Approver" — <c>user</c>, <c>manager</c> or <c>role</c>; null when the element has no approver.</summary>
	[JsonPropertyName("approverType")]
	public string ApproverType { get; set; }

	/// <summary>The employee, as stored. Reported for the <c>user</c> and <c>manager</c> types only.</summary>
	[JsonPropertyName("approverEmployee")]
	public string ApproverEmployee { get; set; }

	/// <summary>The employee's display value, when the platform stored one. Read-only.</summary>
	[JsonPropertyName("approverEmployeeDisplay")]
	public string ApproverEmployeeDisplay { get; set; }

	/// <summary>The approving role, as stored. Reported for the <c>role</c> type only.</summary>
	[JsonPropertyName("approverRole")]
	public string ApproverRole { get; set; }

	/// <summary>The role's display value, when the platform stored one. Read-only.</summary>
	[JsonPropertyName("approverRoleDisplay")]
	public string ApproverRoleDisplay { get; set; }

	/// <summary>"Approval may be delegated".</summary>
	[JsonPropertyName("allowDelegation")]
	public bool? AllowDelegation { get; set; }

	/// <summary>"Notify that approval is required".</summary>
	[JsonPropertyName("notifyApprover")]
	public bool? NotifyApprover { get; set; }

	/// <summary>The approver notification's "Email template" — the stored lookup macro.</summary>
	[JsonPropertyName("approverEmailTemplate")]
	public string ApproverEmailTemplate { get; set; }

	/// <summary>The approver template's display value, when the platform stored one. Read-only.</summary>
	[JsonPropertyName("approverEmailTemplateDisplay")]
	public string ApproverEmailTemplateDisplay { get; set; }

	/// <summary>"Notify about the approval result".</summary>
	[JsonPropertyName("notifyAuthor")]
	public bool? NotifyAuthor { get; set; }

	/// <summary>The author notification's "Email template" — the stored lookup macro.</summary>
	[JsonPropertyName("authorEmailTemplate")]
	public string AuthorEmailTemplate { get; set; }

	/// <summary>The author template's display value, when the platform stored one. Read-only.</summary>
	[JsonPropertyName("authorEmailTemplateDisplay")]
	public string AuthorEmailTemplateDisplay { get; set; }

	/// <summary>"Recipient" — the result notification's address.</summary>
	[JsonPropertyName("recipient")]
	public string Recipient { get; set; }

	/// <summary>"Ignore errors on sending" — reported only when the element actually carries the value.</summary>
	[JsonPropertyName("ignoreEmailErrors")]
	public bool? IgnoreEmailErrors { get; set; }

	/// <summary>
	/// The visa schema DERIVED for the approval object. Reported for traceability; it is never accepted as input.
	/// The platform's <c>SysApproval</c> fallback appears here when the object has no registered visa.
	/// </summary>
	[JsonPropertyName("approvalSchemaUId")]
	public string ApprovalSchemaUId { get; set; }

	/// <summary>Captures any field a newer server reports that this build does not declare.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>
/// The configuration of an Open edit page element, read back from its parameters.
/// <para>Declared rather than left to <c>AdditionalData</c> so the block is typed and documented at the tool
/// surface. (The Modify data <c>changeData</c> block is still undeclared and reaches callers through the extension
/// bag — worth aligning, but out of this story's scope.)</para>
/// </summary>
public sealed class DescribedOpenEditPage {
	/// <summary>The page schema NAME the element opens; null when the stored page UId does not resolve here.</summary>
	[JsonPropertyName("page")]
	public string Page { get; set; }

	/// <summary>The page schema UId as stored.</summary>
	[JsonPropertyName("pageSchemaUId")]
	public string PageSchemaUId { get; set; }

	/// <summary>The target object (entity) name, which the designer derives from the page.</summary>
	[JsonPropertyName("object")]
	public string Object { get; set; }

	/// <summary>
	/// The RECORD TYPE the page opens for, when the object is typed (<c>Activity</c> → Task / Call / Email).
	/// <c>null</c> for an untyped object. NOT a Classic-vs-Freedom marker. Feeding it back as <c>recordType</c> is
	/// OPTIONAL and asserts the registration you expect: the designer offers one entry per page, so the type
	/// FOLLOWS the page and omitting it is never ambiguous. A value that disagrees with the page's registered type
	/// is refused naming the registered one.
	/// </summary>
	[JsonPropertyName("pageTypeUId")]
	public string PageTypeUId { get; set; }

	/// <summary>Editing mode — <c>add</c> or <c>edit</c>; null on an element that stores no mode.</summary>
	[JsonPropertyName("editMode")]
	public string EditMode { get; set; }

	/// <summary>
	/// The values pre-filled on the new record (<c>add</c> mode), decoded the same way a Modify data element's
	/// assignments are. Reported whenever STORED — see the asymmetry noted on
	/// <see cref="DescribedElement.OpenEditPage"/>.
	/// </summary>
	[JsonPropertyName("defaultValues")]
	public List<JsonElement> DefaultValues { get; set; }

	/// <summary>Which record the page opens (<c>edit</c> mode), decoded back into its named source where provable.</summary>
	[JsonPropertyName("recordId")]
	public JsonElement? RecordId { get; set; }

	/// <summary>The recommendation shown on the opened page, when stored as a constant.</summary>
	[JsonPropertyName("recommendation")]
	public string Recommendation { get; set; }

	/// <summary>The hint shown behind the page's information button, when stored as a constant.</summary>
	[JsonPropertyName("hint")]
	public string Hint { get; set; }

	/// <summary>
	/// "Create a list of results by column" — the step's outcome as one result per value of a lookup column.
	/// <c>null</c> when the element stores none of it.
	/// </summary>
	[JsonPropertyName("resultsByColumn")]
	public DescribedOpenEditPageResultsByColumn ResultsByColumn { get; set; }

	/// <summary>
	/// "Log activity" and its scheduling fields. <c>null</c> when the element stores none of them — which is not the
	/// same as the designer showing them empty: the panel populates every one of these from schema defaults, so
	/// <c>null</c> means "nothing written", never "shown as blank".
	/// </summary>
	[JsonPropertyName("logActivity")]
	public DescribedOpenEditPageLogActivity LogActivity { get; set; }

	/// <summary>
	/// Who performs the step, and whether the page opens automatically. <c>null</c> on an element that carries no
	/// performer assignment — the designer's own initial state, not an error.
	/// <para><c>null</c> does NOT mean the step runs for nobody: the platform resolves an empty performer to the
	/// CURRENT USER's contact at run time, which is why the designer's card shows "User" and the current user for an
	/// element whose schema stores neither. Report it as "not assigned explicitly", not as a gap to fill.</para>
	/// </summary>
	[JsonPropertyName("performer")]
	public DescribedPerformer Performer { get; set; }

	/// <summary>
	/// Completion mode — <c>onSave</c> or <c>onConditions</c>. Derived from the stored flag, never from a designer
	/// caption: the captions for these two options differ between environments behind a platform feature switch.
	/// </summary>
	[JsonPropertyName("completionMode")]
	public string CompletionMode { get; set; }

	/// <summary>Forward-compatibility bag, so a newer server reporting more fields does not lose them.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>
/// The "Create a list of results by column" configuration of an Open edit page element.
/// <para>Reading it back is the only way to see the result list a step offers. Note the limitation this reflects:
/// clio cannot yet build the CONDITIONAL flows that route those results, so a clio-built process carries the list
/// without branching on it until a human wires the flows in the designer.</para>
/// </summary>
public sealed class DescribedOpenEditPageResultsByColumn {
	/// <summary>Whether the results list is generated; <c>null</c> when the flag is not stored on the element.</summary>
	[JsonPropertyName("enabled")]
	public bool? Enabled { get; set; }

	/// <summary>
	/// The chosen column's NAME — what a caller feeds back as <c>column</c>. <c>null</c> when no column is stored, or
	/// when its UId no longer resolves on this environment (check <see cref="ColumnUId"/> to tell those apart).
	/// </summary>
	[JsonPropertyName("column")]
	public string Column { get; set; }

	/// <summary>The column's UId as stored, so an unresolvable one stays visible rather than reading as "no column".</summary>
	[JsonPropertyName("columnUId")]
	public string ColumnUId { get; set; }

	/// <summary>Forward-compatibility bag, so a newer server reporting more fields does not lose them.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>
/// The "Log activity" configuration of an Open edit page element: whether the step creates an Activity record, and
/// the scheduling fields the designer reveals under that checkbox.
/// </summary>
public sealed class DescribedOpenEditPageLogActivity {
	/// <summary>
	/// Whether the step logs an activity (<c>CreateActivity</c>). <c>null</c> when the element stores no flag of
	/// its own — which does NOT mean it logs nothing. The platform materializes the user-task schema's own default
	/// onto a new element, and that default is VERSION-DEPENDENT: measured ON (with a 5-minute duration) on a
	/// 10.1.628 core, while the 7.8.0 schema ships it off. So never narrate a <c>null</c> as "this step logs no
	/// activity"; report that the element carries no explicit flag and the environment's schema default decides.
	/// </summary>
	[JsonPropertyName("enabled")]
	public bool? Enabled { get; set; }

	/// <summary>"Start in" — the delay before the activity starts.</summary>
	[JsonPropertyName("startIn")]
	public DescribedActivityInterval StartIn { get; set; }

	/// <summary>"Planned duration".</summary>
	[JsonPropertyName("duration")]
	public DescribedActivityInterval Duration { get; set; }

	/// <summary>"Remind in" — the reminder offset.</summary>
	[JsonPropertyName("remindIn")]
	public DescribedActivityInterval RemindIn { get; set; }

	/// <summary>"Show in calendar" — whether the activity appears in the scheduler.</summary>
	[JsonPropertyName("showInCalendar")]
	public bool? ShowInCalendar { get; set; }

	/// <summary>
	/// "Priority" as its lookup NAME — what a caller feeds back. <c>null</c> when no priority is stored or the
	/// stored id no longer resolves; <see cref="PriorityId"/> tells those two apart.
	/// </summary>
	[JsonPropertyName("priority")]
	public string Priority { get; set; }

	/// <summary>The stored priority record id, so an unresolvable one stays visible.</summary>
	[JsonPropertyName("priorityId")]
	public string PriorityId { get; set; }

	/// <summary>Forward-compatibility bag, so a newer server reporting more fields does not lose them.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>
/// One scheduling interval: the stored number together with the unit its separate period parameter selects.
/// <para>The pair travels as one value on purpose. The platform stores the number and the unit in INDEPENDENT
/// parameters, so a number read without its unit means nothing — 30 is thirty minutes or thirty days depending on a
/// second parameter entirely.</para>
/// </summary>
public sealed class DescribedActivityInterval {
	/// <summary>The stored amount.</summary>
	[JsonPropertyName("value")]
	public int? Value { get; set; }

	/// <summary>
	/// The unit token — <c>minutes</c>, <c>hours</c>, <c>days</c>, <c>weeks</c> or <c>months</c>. <c>null</c> when no
	/// period is stored (the runtime then uses the schema default) or the stored one is outside those five.
	/// </summary>
	[JsonPropertyName("unit")]
	public string Unit { get; set; }

	/// <summary>The raw stored period integer, so an unrecognized value stays visible rather than swallowed.</summary>
	[JsonPropertyName("period")]
	public int? Period { get; set; }

	/// <summary>Forward-compatibility bag.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>The email configuration of a Send email element, read back from its parameters.</summary>
public sealed class DescribedEmail {
	/// <summary>Send mode: <c>auto</c> or <c>manual</c>; null when not set on the element.</summary>
	[JsonPropertyName("mode")]
	public string Mode { get; set; }

	/// <summary>The sender formula (<c>[#Lookup.{objectUId}.{mailboxId}#]</c>); null when no sender is set.</summary>
	[JsonPropertyName("sender")]
	public string Sender { get; set; }

	/// <summary>Human-readable sender identity (mailbox name / address) when the schema carries one.</summary>
	[JsonPropertyName("senderDisplay")]
	public string SenderDisplay { get; set; }

	/// <summary>The subject — a plain constant or a formula expression; null when not set.</summary>
	[JsonPropertyName("subject")]
	public string Subject { get; set; }

	/// <summary>
	/// Which message the element sends: <c>custom</c> (<c>BodyTemplateType = "1"</c>) or <c>template</c>
	/// (<c>"0"</c>); null when the element carries no stored mode — which the platform RUNS as template mode, so null
	/// beside a null <see cref="Template"/> is the pre-run signal of the <c>Localizable template not found</c> trap.
	/// Null also from a server that predates template mode (ENG-95986), which reports no such member.
	/// </summary>
	[JsonPropertyName("messageSource")]
	public string MessageSource { get; set; }

	/// <summary>TEMPLATE mode: the stored <c>EmailTemplate</c> record id; null when none is set. Re-appliable through <c>email.template</c>.</summary>
	[JsonPropertyName("template")]
	public string Template { get; set; }

	/// <summary>The template's name as the designer shows it; null when the schema stores no display value.</summary>
	[JsonPropertyName("templateDisplay")]
	public string TemplateDisplay { get; set; }

	/// <summary>
	/// The entity the template's macros resolve against — the macro-source parameter's reference object by name;
	/// null when the element carries none (a template without an object cannot be personalized).
	/// </summary>
	[JsonPropertyName("templateObject")]
	public string TemplateObject { get; set; }

	/// <summary>
	/// TEMPLATE mode: the macro-source record binding (<c>EmailTemplateEntityId</c>) with its source and value,
	/// projected like a recipient; null when unbound. Re-appliable through <c>email.templateEntity</c>.
	/// </summary>
	[JsonPropertyName("templateEntity")]
	public DescribedParameter TemplateEntity { get; set; }

	/// <summary>
	/// True when the element carries a custom-message body. A lightweight presence flag beside <see cref="Body"/>,
	/// for callers that only need to know a body exists without pulling the (possibly large) decoded HTML.
	/// <para>Nullable defensively, NOT because a known server omits it: the flag is a non-nullable <c>bool</c>
	/// DataMember introduced in the same server commit as the email block, so every build that reports the block
	/// reports the flag too. <c>null</c> therefore means the flag was absent, which no shipped server produces —
	/// treat it as unknown rather than <c>false</c> if it ever appears. The degradation that DOES occur is the whole
	/// block arriving null, which is what an older package produces.</para>
	/// </summary>
	[JsonPropertyName("hasBody")]
	public bool? HasBody { get; set; }

	/// <summary>
	/// The custom-message body HTML, with the platform's process-macro image tokens DECODED back into the friendly
	/// authoring placeholders (<c>[[param:Name]]</c> / <c>[[element:Element.Output(.Column)]]</c>) — the round-trip
	/// of <c>email.body</c>, so a caller reads it in the same form it would author and can edit it in place on a
	/// modify — but the decode is NOT a guaranteed inverse of the create/modify resolve: a parameter renamed or
	/// removed since describe read it, or literal <c>[[…]]</c> characters a human typed into the designer's content
	/// editor (which decode echoes untouched), are REJECTED when written back, so a read-modify-write of a body the
	/// caller never semantically changed can still fail the whole operation. <c>null</c> from a server that predates
	/// the body-macro feature (that older package reports only <see cref="HasBody"/>); a macro token whose UIds no
	/// longer resolve is left as the raw <c>&lt;img&gt;</c> token (the server's decode is best-effort). Can be large,
	/// and there is NO opt-out — every <c>describe</c> on a process with email elements carries it — so
	/// <see cref="HasBody"/> stays the cheap presence flag when the HTML itself is not needed.
	/// </summary>
	[JsonPropertyName("body")]
	public string Body { get; set; }

	/// <summary>Importance token (<c>none</c>/<c>normal</c>/<c>high</c>/<c>low</c>); null when not set.</summary>
	[JsonPropertyName("importance")]
	public string Importance { get; set; }

	/// <summary>The ignore-sending-errors flag; null when not set on the element.</summary>
	[JsonPropertyName("ignoreErrors")]
	public bool? IgnoreErrors { get; set; }

	/// <summary>To recipients — the element's dynamic <c>Recipient&lt;N&gt;</c> parameters that carry a value.</summary>
	[JsonPropertyName("to")]
	public List<DescribedParameter> To { get; set; }

	/// <summary>Cc recipients — the dynamic <c>CopyRecipient&lt;N&gt;</c> parameters that carry a value.</summary>
	[JsonPropertyName("cc")]
	public List<DescribedParameter> Cc { get; set; }

	/// <summary>Bcc recipients — the dynamic <c>BlindCopyRecipient&lt;N&gt;</c> parameters that carry a value.</summary>
	[JsonPropertyName("bcc")]
	public List<DescribedParameter> Bcc { get; set; }

	/// <summary>The manual-mode performer; null when the element carries no performer assignment.</summary>
	[JsonPropertyName("performer")]
	public DescribedPerformer Performer { get; set; }

	/// <summary>
	/// Captures every other field the server reports inside the email block so the description round-trips
	/// losslessly: a newer <c>CrtProcessBuilder</c> reporting something this build does not declare — a body format, an
	/// attachment list — reaches the command output verbatim instead of being
	/// discarded without a trace. This block is where the next email feature lands, so it needs the bag most.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>
/// The configuration of a Pre-configured page element, read back from its parameters. Mirrors the server's
/// <c>DescribePreconfiguredPageInfo</c> field for field.
/// </summary>
public sealed class DescribedPreconfiguredPage {
	/// <summary>
	/// The referenced page's client-unit schema name, or the raw UId when the page no longer resolves — describe
	/// never fails over a dangling reference, it reports what the element actually stores.
	/// </summary>
	[JsonPropertyName("page")]
	public string Page { get; set; }

	/// <summary>
	/// The page's UI generation: <c>freedom</c> or <c>classic</c>; null when the page could not be read at all.
	/// It decides which fields the element can carry — completing buttons are Freedom-only, the connected-object
	/// pair is Classic-only.
	/// </summary>
	[JsonPropertyName("pageUiType")]
	public string PageUiType { get; set; }

	/// <summary>"Who performs the task?"; null when the element carries no performer.</summary>
	[JsonPropertyName("performer")]
	public DescribedPreconfiguredPagePerformer Performer { get; set; }

	/// <summary>
	/// The completing buttons, in stored order. Read it together with <see cref="PageUiType"/>: on
	/// <c>freedom</c> an EMPTY list means none are selected — an element that can never finish at run time; on
	/// <c>classic</c> the member is <c>null</c> (NOT APPLICABLE — such a page completes through its own
	/// page-designer buttons, whose model this contract does not carry, so do not report it as broken); on a null
	/// page type an empty list means nothing, because the page could not be read.
	/// </summary>
	[JsonPropertyName("buttons")]
	public List<DescribedPreconfiguredPageButton> Buttons { get; set; }

	/// <summary>"Recommendations for filling in the page" — a single line; null when unset.</summary>
	[JsonPropertyName("recommendation")]
	public string Recommendation { get; set; }

	/// <summary>
	/// The page's data sources, each with the ELEMENT PARAMETER holding the id of the record the page saved.
	/// Empty when the element has none; null from a server that does not report them yet.
	/// <para>The parameter named here is the handle a mapping uses to pass the saved record to a later element —
	/// and it does NOT appear in the element's own parameter list, because the server reports element parameters
	/// only when they are a result, an output, or carry a stored value, and a data-source parameter is none of
	/// those until run time. This is the only place it surfaces.</para>
	/// </summary>
	[JsonPropertyName("dataSources")]
	public List<DescribedPreconfiguredPageDataSource> DataSources { get; set; }

	/// <summary>
	/// CLASSIC UI pages only: the "Connected object" entity schema name. Null for a Freedom UI page, which carries
	/// its object in its own data sources instead.
	/// </summary>
	[JsonPropertyName("connectedObject")]
	public string ConnectedObject { get; set; }

	/// <summary>CLASSIC UI pages only: the "Record of connected object" value. Null for a Freedom UI page.</summary>
	[JsonPropertyName("connectedObjectRecord")]
	public string ConnectedObjectRecord { get; set; }

	/// <summary>
	/// Whether the element's page parameters still match the referenced page. <c>null</c> means the page could not
	/// be read, which is deliberately NOT the same as <c>false</c>. Read-only: describe reports drift and never
	/// fixes it — any <c>setElement</c> touching the element re-synchronizes it.
	/// </summary>
	[JsonPropertyName("inSync")]
	public bool? InSync { get; set; }

	/// <summary>
	/// Page parameters that are NOT on the element and never will be under that name — the name is already taken
	/// by a parameter the element itself owns. Empty in the ordinary case. <c>null</c> means UNKNOWN, from either
	/// of two causes: the page's parameters could not be read (in which case <see cref="InSync"/> is null too —
	/// they are always null together), or the server is an older CrtProcessBuilder that does not report them.
	/// <para>Read it alongside <see cref="InSync"/>, never instead of it. A shadowed parameter is a stable end
	/// state rather than drift, so <c>inSync</c> stays <c>true</c> — it means "nothing left to synchronize", NOT
	/// "the element carries every page parameter". Mapping to one of these names silently targets the element's
	/// own parameter instead of the page's.</para>
	/// </summary>
	[JsonPropertyName("shadowedPageParameters")]
	public List<string> ShadowedPageParameters { get; set; }

	/// <summary>
	/// Captures every field a newer server reports that this build does not declare, so it reaches the printed
	/// JSON instead of being dropped — the failure mode that silenced this whole block twice before it existed.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }}

/// <summary>The performer of a Pre-configured page element ("Who performs the task?").</summary>
public sealed class DescribedPreconfiguredPagePerformer {
	/// <summary>Performer kind: <c>user</c>, <c>manager</c>, or <c>role</c>.</summary>
	[JsonPropertyName("type")]
	public string Type { get; set; }

	/// <summary>For user/manager: the contact formula on the <c>OwnerId</c> parameter; null when unset.</summary>
	[JsonPropertyName("contact")]
	public string Contact { get; set; }

	/// <summary>For role: the role lookup value on the <c>RoleId</c> parameter; null when unset.</summary>
	[JsonPropertyName("role")]
	public string Role { get; set; }

	/// <summary>
	/// "Show page automatically" — reported ONLY for a <c>user</c> performer, because the runtime ignores it when
	/// the task runs for anyone else. Null on a role/manager performer means "not applicable". Null on a
	/// <c>user</c> performer means ENABLED — the task schema's default applies, and a designer-built element
	/// stores no value of its own — so do NOT write <c>showPage: true</c> back to "fix" it.
	/// </summary>
	[JsonPropertyName("showPage")]
	public bool? ShowPage { get; set; }

	/// <summary>
	/// Captures every field a newer server reports that this build does not declare, so it reaches the printed
	/// JSON instead of being dropped — the failure mode that silenced this whole block twice before it existed.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }}

/// <summary>One page data source of a Pre-configured page element, with the parameter carrying its record id.</summary>
public sealed class DescribedPreconfiguredPageDataSource {
	/// <summary>The data source's name on the page (for example <c>PDS</c>).</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>The entity the data source reads and writes; the raw UId when the schema no longer resolves.</summary>
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; set; }

	/// <summary>
	/// The element parameter holding the record id — <c>DataSource_&lt;name&gt;_&lt;primary column&gt;</c>. Use it
	/// as a mapping source to pass the saved record downstream, or set it to pre-open the page on an existing one.
	/// </summary>
	[JsonPropertyName("parameter")]
	public string Parameter { get; set; }

	/// <summary>
	/// Captures every field a newer server reports that this build does not declare, so it reaches the printed
	/// JSON instead of being dropped — the failure mode that silenced this whole block twice before it existed.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }}

/// <summary>One completing button of a Pre-configured page element.</summary>
public sealed class DescribedPreconfiguredPageButton {
	/// <summary>The button's view-element name on the page.</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>The button's caption as stored on the element.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>The page event that completes the step — <c>clicked</c>.</summary>
	[JsonPropertyName("event")]
	public string Event { get; set; }

	/// <summary>Whether pressing it validates the page first. Absent on the element reads as the card default (true).</summary>
	[JsonPropertyName("validate")]
	public bool? Validate { get; set; }

	/// <summary>
	/// Captures every field a newer server reports that this build does not declare, so it reaches the printed
	/// JSON instead of being dropped — the failure mode that silenced this whole block twice before it existed.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }}

/// <summary>
/// The performer of a user-task element ("Who performs the task?"), read back from its performer-assignment
/// options: top-level on a described Perform task, inside the <c>email</c> block on a described Send email element,
/// and inside the <c>openEditPage</c> block on a described Open edit page element.
/// <para>One JSON shape, one type. Rules that differ BETWEEN elements — Send email offers the block only in manual
/// send mode, Open edit page offers it unconditionally — are write-side availability rules and live in the tool
/// descriptions and on each element's own property doc, not here. A second class per element would be two hand-
/// synchronised declarations of the same five fields, which is exactly how they drift.</para>
/// </summary>
public sealed class DescribedPerformer {
	/// <summary>Performer kind: <c>user</c>, <c>manager</c>, or <c>role</c>.</summary>
	[JsonPropertyName("type")]
	public string Type { get; set; }

	/// <summary>For user/manager: the contact formula on the <c>OwnerId</c> parameter; null when unset.</summary>
	[JsonPropertyName("contact")]
	public string Contact { get; set; }

	/// <summary>For role: the role formula on the <c>RoleId</c> parameter; null when unset.</summary>
	[JsonPropertyName("role")]
	public string Role { get; set; }

	/// <summary>Human-readable role name when the schema carries one.</summary>
	[JsonPropertyName("roleDisplay")]
	public string RoleDisplay { get; set; }

	/// <summary>The "open the execution page automatically" flag; null when not set on the element.</summary>
	[JsonPropertyName("showPage")]
	public bool? ShowPage { get; set; }
	/// <summary>
	/// Forward-compatibility bag, so a newer server reporting more performer fields does not lose them. Added when
	/// the Open edit page element started reporting through this type: the duplicate it replaced carried one, and
	/// dropping it would have silently narrowed what a newer server can report.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }

}

/// <summary>
/// One BOUND host-entity connection of an element, as the server decoded it.
/// </summary>
/// <remarks>
/// Hybrid by design: <see cref="Value"/> is the raw persisted macro and exactly one of
/// <see cref="RecordId"/>+<see cref="ReferenceSchema"/> / <see cref="ProcessParameter"/> /
/// <see cref="SourceElement"/>+<see cref="SourceElementParameter"/> / <see cref="Expression"/> is the decoded form,
/// in the same shape <c>setConnections</c> accepts. A macro the server does not recognise arrives as
/// <see cref="Expression"/> carrying the original text, so nothing is lost and a future platform macro degrades
/// instead of breaking the read.
/// <para>Every member is declared here on purpose, but that is NOT a substitute for an overflow bag: a field the
/// server reports and this type does not declare is still discarded without a trace, which is the same silent-loss
/// failure the connections feature exists to remove. Unlike <see cref="DescribeProcessResult"/>,
/// <see cref="DescribedElement"/>, <see cref="DescribedEmail"/> and <see cref="DescribedFlow"/>, this type
/// has no <c>[JsonExtensionData]</c> yet — an accepted gap on the connections ticket's own surface, not something these
/// remarks endorse. Add one here when that ticket is next touched.</para>
/// </remarks>
public sealed class DescribedConnection {
	/// <summary>The host-entity column the connection binds (for example <c>Account</c>).</summary>
	[JsonPropertyName("column")]
	public string Column { get; set; }

	/// <summary>
	/// Whether a connection-registry row registers this column. <c>false</c> does NOT mean the value is unwritten —
	/// it is — but the connection is ignored by the record page's connections detail, Next Steps, email
	/// auto-relation rules and quick-add, and is normally absent from the designer's connections block too.
	/// </summary>
	[JsonPropertyName("registered")]
	public bool Registered { get; set; }

	/// <summary>The platform value source (<c>Script</c> for every designer-authored connection).</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; }

	/// <summary>The raw persisted value, verbatim.</summary>
	[JsonPropertyName("value")]
	public string Value { get; set; }

	/// <summary>Decoded fixed record: the bound record's id. Paired with <see cref="ReferenceSchema"/>.</summary>
	[JsonPropertyName("recordId")]
	public string RecordId { get; set; }

	/// <summary>Decoded fixed record: the referenced entity's NAME, resolved from the UId inside the macro.</summary>
	[JsonPropertyName("referenceSchema")]
	public string ReferenceSchema { get; set; }

	/// <summary>Decoded process-parameter source: the process parameter's name.</summary>
	[JsonPropertyName("processParameter")]
	public string ProcessParameter { get; set; }

	/// <summary>Decoded element-output source: the source element's name.</summary>
	[JsonPropertyName("sourceElement")]
	public string SourceElement { get; set; }

	/// <summary>Decoded element-output source: the parameter name on that element.</summary>
	[JsonPropertyName("sourceElementParameter")]
	public string SourceElementParameter { get; set; }

	/// <summary>The raw macro when no dialect decodes it — a system variable, a system setting, or a newer macro.</summary>
	[JsonPropertyName("expression")]
	public string Expression { get; set; }
}

/// <summary>The record-event trigger of a signal start element (what starts the process).</summary>
public sealed class DescribedSignal {
	/// <summary>Triggering entity (object) name.</summary>
	[JsonPropertyName("entity")]
	public string Entity { get; set; }

	/// <summary>Triggering entity schema UId.</summary>
	[JsonPropertyName("entitySchemaUId")]
	public string EntitySchemaUId { get; set; }

	/// <summary>The record change that starts the process: <c>added</c>, <c>modified</c>, or <c>deleted</c> (a single event — the designer has no combined trigger).</summary>
	[JsonPropertyName("on")]
	public string On { get; set; }

	/// <summary>
	/// For an <c>on: modified</c> signal restricted to specific columns: the tracked column names (the process fires
	/// only when one of them changes). <c>null</c> for an any-change signal or a non-modified trigger. Round-trips into
	/// a <c>create-business-process</c>/<c>modify-business-process</c> <c>signal.changedColumns</c>.
	/// </summary>
	[JsonPropertyName("changedColumns")]
	public List<string> ChangedColumns { get; set; }
}

/// <summary>A data source filter group read back from an element — a recursive AND/OR tree of conditions.</summary>
public class DescribedFilterGroup {
	/// <summary>How the members combine: <c>and</c> or <c>or</c>.</summary>
	[JsonPropertyName("logicalOperation")]
	public string LogicalOperation { get; set; }

	/// <summary>Leaf comparisons at this group level.</summary>
	[JsonPropertyName("conditions")]
	public List<DescribedFilterCondition> Conditions { get; set; }

	/// <summary>Nested sub-groups, each with its own <see cref="LogicalOperation"/>.</summary>
	[JsonPropertyName("groups")]
	public List<DescribedFilterGroup> Groups { get; set; }
}

/// <summary>The root data source filter of an element: the group tree plus the object its columns belong to.</summary>
public sealed class DescribedFilter : DescribedFilterGroup {
	/// <summary>Root object (entity schema) the filter columns belong to (for example <c>Contact</c>).</summary>
	[JsonPropertyName("object")]
	public string Object { get; set; }
}

/// <summary>A single leaf comparison of a described filter: <c>column comparison &lt;right-hand value&gt;</c>.</summary>
public sealed class DescribedFilterCondition {
	/// <summary>Column path (may traverse lookups, for example <c>Account.Code</c>).</summary>
	[JsonPropertyName("column")]
	public string Column { get; set; }

	/// <summary>Comparison token (for example <c>equal</c>, <c>greater</c>, <c>contains</c>, <c>isNull</c>).</summary>
	[JsonPropertyName("comparison")]
	public string Comparison { get; set; }

	/// <summary>Constant value (string form); null for a reference or a null check.</summary>
	[JsonPropertyName("value")]
	public string Value { get; set; }

	/// <summary>
	/// Human-readable caption of the value on read-back (never sent on write). For a lookup constant this is the
	/// referenced record's display name (for example <c>Approved</c>) so the value is not shown as a bare GUID; for a
	/// process/element parameter reference it is that parameter's caption (making the opaque <see cref="Expression"/>
	/// token readable). Null for a plain scalar, or when the source process predates the resolved-display serialization.
	/// </summary>
	[JsonPropertyName("displayValue")]
	public string DisplayValue { get; set; }

	/// <summary>
	/// RESERVED (forward-compat) — NOT populated by the current server. The decoder surfaces every parameter
	/// reference (process- or element-level) as the raw meta-path <see cref="Expression"/> token only, so a described
	/// reference always arrives in <see cref="Expression"/>, never here. Kept to mirror the write-side descriptor
	/// (which does accept a by-name process parameter) so a symbolic read-back would bind without a DTO change if a
	/// future server emits one. Do not assume references round-trip structurally today.
	/// </summary>
	[JsonPropertyName("processParameter")]
	public string ProcessParameter { get; set; }

	/// <summary>
	/// RESERVED (forward-compat) — NOT populated by the current server; see <see cref="ProcessParameter"/>. An
	/// element-parameter reference is surfaced as the raw <see cref="Expression"/> token, not this structured shape.
	/// </summary>
	[JsonPropertyName("elementParameter")]
	public DescribedFilterElementRef ElementParameter { get; set; }

	/// <summary>
	/// Raw meta-path expression token. The read-back surfaces EVERY parameter reference here (both process- and
	/// element-parameter references), which is why <see cref="ProcessParameter"/> / <see cref="ElementParameter"/>
	/// stay null on a real describe.
	/// </summary>
	[JsonPropertyName("expression")]
	public string Expression { get; set; }

	/// <summary>A relative-date / system macro compared against the column (for example <c>Today</c>, <c>NextNDays</c>).</summary>
	[JsonPropertyName("macro")]
	public string Macro { get; set; }

	/// <summary>The integer argument for an argument macro (for example <c>NextNDays</c> / <c>PreviousNHours</c>).</summary>
	[JsonPropertyName("macroArgument")]
	public int? MacroArgument { get; set; }

	/// <summary>
	/// A calendar/clock part extracted from a Date/DateTime <see cref="Column"/> before comparing (for example
	/// <c>Year</c>, <c>Month</c>, <c>Day</c>, <c>Weekday</c>): the condition reads <c>Year(CreatedOn) = 2026</c>. A
	/// left-hand modifier of the column, not a right-hand source — the integer parts pair with an integer
	/// <see cref="Value"/>, while <c>HourMinute</c> extracts the time-of-day and reads back a <c>HH:mm:ss</c> value.
	/// </summary>
	[JsonPropertyName("datePart")]
	public string DatePart { get; set; }
}

/// <summary>An element-parameter reference used as a filter's right-hand value.</summary>
public sealed class DescribedFilterElementRef {
	/// <summary>Name (local handle) of the element that owns the parameter — the element's <c>Name</c>, not a GUID.</summary>
	[JsonPropertyName("elementName")]
	public string ElementName { get; set; }

	/// <summary>Name of the parameter on that element.</summary>
	[JsonPropertyName("parameter")]
	public string Parameter { get; set; }
}

/// <summary>A sequence flow between two nodes.</summary>
public sealed class DescribedFlow {
	/// <summary>Source node NAME — not its UId, despite what an earlier revision of this comment said.</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; }

	/// <summary>Target node NAME — not its UId, despite what an earlier revision of this comment said.</summary>
	[JsonPropertyName("target")]
	public string Target { get; set; }

	/// <summary>Flow kind: <c>sequence</c>, <c>conditional</c>, or <c>default</c>.</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; set; }

	/// <summary>
	/// The boolean expression deciding whether a branch is taken, exactly as stored.
	/// <para>Reported whenever the flow carries condition TEXT - including on a flow whose <c>kind</c> is NOT
	/// conditional, which an earlier version of this comment denied. Such text is dropped at generation time and
	/// never evaluated, and <c>kind</c> is what says so; it is still reported because the parameter-delete and
	/// element-retarget guards both SCAN it and refuse on it, so hiding it would leave a caller refused over
	/// something no read API shows. <c>null</c> when there is no text, and on a conditional flow whose branch is
	/// chosen by an activity result the text is STILL reported, because the delete guard scans it - read
	/// <c>branchesOnActivityResult</c> to learn that the flow ignores its expression entirely.</para>
	/// </summary>
	/// <remarks>
	/// This field is NOT optional polish, though the original reason for saying so is gone:
	/// <see cref="DescribedFlow"/> had no <c>[JsonExtensionData]</c> overflow bag when this property was
	/// added, so a server field with no property here was dropped silently on clio's re-serialize and the
	/// caller never learned the condition existed. It has one now (added with the
	/// <see cref="BranchesOnActivityResult"/> nullability fix), so an undeclared field survives - but a
	/// TYPED property is still what this needs, because callers and the delete guard read it by name and a
	/// <c>JsonElement</c> in a dictionary is not that. The bagless failure mode still applies to the
	/// described FILTER types, and is recorded in
	/// <c>docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md</c>.
	/// </remarks>
	[JsonPropertyName("condition")]
	public string Condition { get; set; }

	/// <summary>
	/// <c>true</c> when this flow's branch is decided by the RESULT of the preceding activity - which buttons it
	/// was completed with - and NOT by <see cref="Condition"/>.
	/// <para>The two are indistinguishable without it, and the difference is total: the platform reads the result
	/// map FIRST and only falls back to the expression when it is empty, so on such a flow the condition text is
	/// stored, reported, and never evaluated. <c>setFlowCondition</c> refuses to write one; before this field a
	/// caller verifying their change read the OLD text and took it as proof the change landed.</para>
	/// <para>NULLABLE on purpose, and it is the same reassuring-direction argument the field itself exists
	/// for. <c>describe</c> is allowed on an environment whose package predates this field - its
	/// <c>[RequiresPackage]</c> is presence-only, with no version literal - and such a server simply never
	/// sends it. A non-nullable <c>bool</c> would leave <c>default(bool)</c>, which
	/// <c>WhenWritingNull</c> cannot omit, so the payload would assert <c>false</c> for every flow on a
	/// server that said nothing: a caller reading it would conclude the condition IS evaluated. Absent
	/// stays absent instead, and a caller that finds no key knows to check the package version.</para>
	/// </summary>
	[JsonPropertyName("branchesOnActivityResult")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? BranchesOnActivityResult { get; set; }

	/// <summary>
	/// The flow's LABEL on the diagram - the text the designer draws on the connector - or <c>null</c> when
	/// it carries none.
	/// <para>It does not live in the process metadata. The flow's caption is a platform
	/// <c>LocalizableString</c>, so it is stored in the schema's RESOURCES as
	/// <c>BaseElements.&lt;FlowName&gt;.Caption</c> - which is why diffing two processes' <c>metadata.json</c>
	/// says nothing about their labels.</para>
	/// <para>Typed rather than left to <see cref="AdditionalData"/> for the reason <see cref="Condition"/>
	/// gives: the post-write guard reads it BY NAME to tell a caller their label did not land, and a
	/// <c>JsonElement</c> in a dictionary is not that.</para>
	/// <para>ABSENCE DOES NOT SAY WHY, and an earlier revision of this remark claimed it did. Measured on a
	/// 1.6.0.8 stand: the server omits <c>label</c> for a flow that carries none, exactly as a server
	/// predating the field omits it for every flow - so "this flow has no label" and "this environment does
	/// not report labels" are the same bytes. Clio mirrors that omission rather than inventing an explicit
	/// <c>null</c>, which would only move the ambiguity, not resolve it.</para>
	/// <para>The consequence is worth stating because the shipped guidance tells an agent to read this field
	/// before overwriting a human's label: on an environment whose package predates the member, an
	/// all-absent result is NOT evidence that a designer-authored process is unlabelled. Nothing in the
	/// READ distinguishes the two, and the installed version does not either - clio normally refuses a
	/// package older than the one it ships, so a high number is no evidence the member is present. Treat
	/// an all-absent read as uninformative. The write side needs no such check: <see cref="FlowLabelExpectation"/> reads the flows back and
	/// reports a label that did not land, whatever the reason.</para>
	/// </summary>
	[JsonPropertyName("label")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Label { get; set; }

	/// <summary>
	/// Every other field the server returns on a flow, so a description round-trips losslessly - the same bag
	/// the graph root, nodes and parameters already carry. Added with the nullability fix above: without it a
	/// newer <c>CrtProcessBuilder</c> reporting a new flow field needs a matching clio property AND a clio
	/// release before the caller can see it, and until then it is dropped with no trace. <c>condition</c> and
	/// <c>branchesOnActivityResult</c> keep their typed properties because guards and callers read them by name.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>A parameter read back from the schema, with its value source decoded.</summary>
public sealed class DescribedParameter {
	/// <summary>Parameter name (code).</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>Parameter caption (title); null when unset.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>Parameter description (free-text annotation); null when unset.</summary>
	[JsonPropertyName("description")]
	public string Description { get; set; }

	/// <summary>Parameter UId.</summary>
	[JsonPropertyName("uid")]
	public string UId { get; set; }

	/// <summary>Data value type name (for example <c>ShortText</c>, <c>Integer</c>, <c>Lookup</c>); null when unset.</summary>
	[JsonPropertyName("type")]
	public string Type { get; set; }

	/// <summary>
	/// Direction: <c>In</c>, <c>Out</c>, <c>Variable</c>, or <c>Internal</c>. Together with <see cref="IsResult"/>
	/// lets a caller tell an element's output parameters (mappable as a source) from its inputs. Omitted when the
	/// server (an older <c>CrtProcessBuilder</c>) does not report it.
	/// </summary>
	[JsonPropertyName("direction")]
	public string Direction { get; set; }

	/// <summary>
	/// True when the parameter is a result (output) of its element. A parameter is an output — and therefore usable
	/// as a mapping source — when <see cref="Direction"/> is <c>Out</c> OR this flag is true. Omitted when the server
	/// (an older <c>CrtProcessBuilder</c>) does not report it.
	/// </summary>
	[JsonPropertyName("isResult")]
	public bool? IsResult { get; set; }

	/// <summary>For a lookup parameter: the referenced object (entity schema) name (for example <c>City</c>); null otherwise.</summary>
	[JsonPropertyName("referenceSchema")]
	public string ReferenceSchema { get; set; }

	/// <summary>Value source: <c>None</c>, <c>ConstValue</c>, <c>Mapping</c>, <c>Script</c>, <c>SystemValue</c>, etc.</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; }

	/// <summary>The source value/expression (for a formula source this is the <c>[#...#]</c> expression).</summary>
	[JsonPropertyName("value")]
	public string Value { get; set; }

	/// <summary>
	/// What the designer SHOWS for <see cref="Value"/> — for a Lookup constant the referenced record's name (for
	/// example <c>Call</c> beside the bare id in <see cref="Value"/>), for a mapping the source parameter's caption.
	/// Read-only: <see cref="Value"/> alone is what round-trips back into <c>addMapping</c> / <c>setParameter</c>,
	/// and the display name is re-derived on every write. Null when the parameter carries no display value — for a
	/// Lookup that means the environment could not name the record, NOT that the value is wrong (the designer then
	/// resolves the name itself). Omitted when the server (an older <c>CrtProcessBuilder</c>) does not report it.
	/// </summary>
	[JsonPropertyName("valueDisplay")]
	public string ValueDisplay { get; set; }

	/// <summary>
	/// Provenance stamp, when the parameter carries one: a collection parameter mirrored from an element output is
	/// tagged <c>&lt;elementName&gt;.&lt;parameterName&gt;</c> — the designer's own "create parameter from element"
	/// stamp — so a caller can re-issue the mirror later (<c>setParameter</c> with the same pair, the designer's
	/// <i>Regenerate</i>). Null when untagged; omitted when the server (a <c>CrtProcessBuilder</c> before 1.4.0.41)
	/// does not report it.
	/// </summary>
	[JsonPropertyName("tag")]
	public string Tag { get; set; }

	/// <summary>
	/// The per-item shape of a collection parameter (<c>CompositeObjectList</c>): one entry per column the collection
	/// carries, each a parameter in its own right (name, type, tag = the column UId). This is the DESIGN-TIME contract
	/// a consumer binds against — a collection without it is an opaque list. Null for a scalar and for a bare,
	/// shapeless collection; omitted when the server does not report it. Feed a described collection back through
	/// <c>addParameter</c>'s <c>typeFromElement</c> naming its source, never by re-typing the shape.
	/// </summary>
	[JsonPropertyName("itemProperties")]
	public List<DescribedParameter> ItemProperties { get; set; }
}

#endregion
