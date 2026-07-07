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
/// value sources), so it is universal — no client-side GUID taxonomy. Requires the <c>clioprocessbuilder</c>
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
}

/// <summary>The record-event trigger of a signal start element (what starts the process).</summary>
public sealed class DescribedSignal {
	/// <summary>Triggering entity (object) name.</summary>
	[JsonPropertyName("entity")]
	public string Entity { get; set; }

	/// <summary>Triggering entity schema UId.</summary>
	[JsonPropertyName("entitySchemaUId")]
	public string EntitySchemaUId { get; set; }

	/// <summary>Record change(s) that start the process: <c>added</c>, <c>modified</c>, <c>deleted</c>, or a combination.</summary>
	[JsonPropertyName("on")]
	public string On { get; set; }
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

	/// <summary>Referenced process parameter (by name); null otherwise.</summary>
	[JsonPropertyName("processParameter")]
	public string ProcessParameter { get; set; }

	/// <summary>Referenced element output parameter; null otherwise.</summary>
	[JsonPropertyName("elementParameter")]
	public DescribedFilterElementRef ElementParameter { get; set; }

	/// <summary>Raw meta-path expression token; the read-back surfaces a parameter reference here.</summary>
	[JsonPropertyName("expression")]
	public string Expression { get; set; }

	/// <summary>A relative-date / system macro compared against the column (for example <c>Today</c>, <c>NextNDays</c>).</summary>
	[JsonPropertyName("macro")]
	public string Macro { get; set; }

	/// <summary>The integer argument for an argument macro (for example <c>NextNDays</c> / <c>PreviousNHours</c>).</summary>
	[JsonPropertyName("macroArgument")]
	public int? MacroArgument { get; set; }
}

/// <summary>An element-parameter reference used as a filter's right-hand value.</summary>
public sealed class DescribedFilterElementRef {
	/// <summary>Local name of the element that owns the parameter.</summary>
	[JsonPropertyName("elementId")]
	public string ElementId { get; set; }

	/// <summary>Name of the parameter on that element.</summary>
	[JsonPropertyName("parameter")]
	public string Parameter { get; set; }
}

/// <summary>A sequence flow between two nodes.</summary>
public sealed class DescribedFlow {
	/// <summary>Source node UId.</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; }

	/// <summary>Target node UId.</summary>
	[JsonPropertyName("target")]
	public string Target { get; set; }

	/// <summary>Flow kind: <c>sequence</c>, <c>conditional</c>, or <c>default</c>.</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; set; }
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
	/// server (an older <c>clioprocessbuilder</c>) does not report it.
	/// </summary>
	[JsonPropertyName("direction")]
	public string Direction { get; set; }

	/// <summary>
	/// True when the parameter is a result (output) of its element. A parameter is an output — and therefore usable
	/// as a mapping source — when <see cref="Direction"/> is <c>Out</c> OR this flag is true. Omitted when the server
	/// (an older <c>clioprocessbuilder</c>) does not report it.
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
