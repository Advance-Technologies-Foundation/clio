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
	/// <returns>The structured description, or an error (not found / unreachable / server failure).</returns>
	ErrorOr<DescribeProcessResult> Describe(ProcessIdentity identity, string culture);
}

/// <inheritdoc cref="IProcessDescriber" />
public sealed class ServerProcessDescriber(
	IApplicationClient applicationClient,
	IDataProvider dataProvider,
	IServiceUrlBuilder serviceUrlBuilder) : IProcessDescriber {

	private const string DescribeErrorCode = "DescribeProcess";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ErrorOr<DescribeProcessResult> Describe(ProcessIdentity identity, string culture) {
		ErrorOr<JsonObject> requestObject = BuildIdentityPayload(identity);
		if (requestObject.IsError) {
			return requestObject.Errors;
		}
		if (!string.IsNullOrWhiteSpace(culture)) {
			requestObject.Value["culture"] = culture;
		}

		string body = new JsonObject { ["request"] = requestObject.Value }.ToJsonString();
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DescribeProcess);
		string responseBody;
		try {
			responseBody = applicationClient.ExecutePostRequest(url, body, 10_000, 3, 1);
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
		return result;
	}

	private ErrorOr<JsonObject> BuildIdentityPayload(ProcessIdentity identity) {
		if (!string.IsNullOrWhiteSpace(identity.UId)) {
			return new JsonObject { ["uid"] = identity.UId.Trim() };
		}
		if (!string.IsNullOrWhiteSpace(identity.Code)) {
			return new JsonObject { ["name"] = identity.Code.Trim() };
		}
		if (!string.IsNullOrWhiteSpace(identity.Caption)) {
			try {
				IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(dataProvider);
				VwProcessLib row = ctx.Models<VwProcessLib>().FirstOrDefault(p => p.Caption == identity.Caption);
				if (row is null) {
					return Error.Failure("ResolveId", $"process not found (caption '{identity.Caption}')");
				}
				return new JsonObject { ["name"] = row.Name };
			} catch (Exception e) {
				return Error.Failure("ResolveId", e.Message);
			}
		}
		return Error.Failure("ResolveId", "no process identity provided (code, uid, or caption)");
	}

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
	/// Captures every other field the server reports on an element so the description round-trips losslessly:
	/// a newer <c>CrtProcessBuilder</c> reporting a block this build does not declare reaches the command output
	/// verbatim instead of being discarded without a trace.
	/// </summary>
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
	/// losslessly: a newer <c>CrtProcessBuilder</c> reporting something this build does not declare — a template
	/// selection, a body format, an attachment list — reaches the command output verbatim instead of being
	/// discarded without a trace. This block is where the next email feature lands, so it needs the bag most.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalData { get; set; }
}

/// <summary>
/// The performer of a user-task element ("Who performs the task?"), read back from its performer-assignment
/// options: top-level on a described Perform task, inside the <c>email</c> block on a described Send email element.
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
/// <see cref="DescribedElement"/> and <see cref="DescribedEmail"/>, this type has no
/// <c>[JsonExtensionData]</c> yet — an accepted gap on the connections ticket's own surface, not something these
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
	/// This field is NOT optional polish. <see cref="DescribedFlow"/> has no <c>[JsonExtensionData]</c> overflow
	/// bag, so a server field with no property here is dropped silently on clio's re-serialize and the caller
	/// never learns the condition exists. The same failure mode is recorded for described filter types in
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
	/// <para>Like <see cref="Condition"/>, this needs a property here or it is dropped on clio's re-serialize -
	/// <see cref="DescribedFlow"/> has no <c>[JsonExtensionData]</c> overflow bag.</para>
	/// </summary>
	[JsonPropertyName("branchesOnActivityResult")]
	public bool BranchesOnActivityResult { get; set; }
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
}

#endregion
