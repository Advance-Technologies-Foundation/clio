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
	ILogger logger,
	IApplicationClient applicationClient,
	IDataProvider dataProvider,
	IServiceUrlBuilder serviceUrlBuilder) : IProcessDescriber {

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
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DescribeProcessSchema);
		string responseBody;
		try {
			responseBody = applicationClient.ExecutePostRequest(url, body, 10_000, 3, 1);
		} catch (Exception e) {
			return Error.Failure("DescribeProcess", e.Message);
		}
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return Error.Failure("DescribeProcess", "empty response from the server");
		}

		DescribeProcessResultEnvelope envelope;
		try {
			envelope = JsonSerializer.Deserialize<DescribeProcessResultEnvelope>(responseBody, JsonOptions);
		} catch (JsonException e) {
			return Error.Failure("DescribeProcess", $"could not parse server response: {e.Message}");
		}

		DescribeProcessResult result = envelope?.Result;
		if (result is null) {
			return Error.Failure("DescribeProcess", "unexpected server response shape");
		}
		if (!result.Success) {
			return Error.Failure("DescribeProcess", result.ErrorMessage ?? "describe-process failed on the server");
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
}

#region DTOs (server wire shape — re-serialized verbatim as the command output)

/// <summary>Wrapper for the WCF <c>BodyStyle=Wrapped</c> response envelope.</summary>
public sealed class DescribeProcessResultEnvelope {
	/// <summary>The wrapped <c>DescribeProcess</c> result.</summary>
	[JsonPropertyName("DescribeProcessResult")]
	public DescribeProcessResult Result { get; set; }
}

/// <summary>The structured process description returned by the server-side <c>DescribeProcess</c>.</summary>
public sealed class DescribeProcessResult {
	/// <summary>True when the process was read successfully (server-internal flag; omitted from output).</summary>
	[JsonPropertyName("success")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Success { get; set; }

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

	/// <summary>Server error message (server-internal; omitted from output on success).</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; set; }
}

/// <summary>A process node read back from the schema.</summary>
public sealed class DescribedElement {
	/// <summary>Element UId.</summary>
	[JsonPropertyName("id")]
	public string Id { get; set; }

	/// <summary>Element schema name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>Localized caption.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>Runtime class name (for example <c>ProcessSchemaUserTask</c>, <c>ProcessSchemaStartEvent</c>).</summary>
	[JsonPropertyName("type")]
	public string Type { get; set; }

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

	/// <summary>Parameter UId.</summary>
	[JsonPropertyName("uid")]
	public string UId { get; set; }

	/// <summary>Value source: <c>None</c>, <c>ConstValue</c>, <c>Mapping</c>, <c>Script</c>, <c>SystemValue</c>, etc.</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; }

	/// <summary>The source value/expression (for a formula source this is the <c>[#...#]</c> expression).</summary>
	[JsonPropertyName("value")]
	public string Value { get; set; }
}

#endregion
