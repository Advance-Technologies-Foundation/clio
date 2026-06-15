using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Projects a parsed <see cref="ProcessSchemaResponse"/> into a structured, AI-readable
/// <see cref="ProcessDescription"/> (elements, flows, process parameters) — the "read &amp; explain"
/// inverse of process generation. Pure in-memory; no I/O.
/// </summary>
/// <remarks>
/// Element types are classified through the shared <see cref="ManagerMap"/> taxonomy (same vocabulary the
/// validator and the <c>process-modeling</c> guidance use). v1 does NOT decode the heavily-escaped filter /
/// mapping expressions inside element parameters — it returns structure + types + flows + basic params only.
/// </remarks>
public interface IProcessGraphExtractor {
	/// <summary>
	/// Projects the parsed schema into a structured description.
	/// </summary>
	/// <param name="schema">The parsed process schema.</param>
	/// <param name="culture">Culture used to resolve localized captions (e.g. <c>en-US</c>).</param>
	/// <returns>The structured description.</returns>
	ProcessDescription Extract(ProcessSchemaResponse schema, string culture);
}

/// <summary>Structured, AI-readable description of an existing business process.</summary>
public sealed record ProcessDescription(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("uId")] string UId,
	[property: JsonPropertyName("elements")] IReadOnlyList<ProcessDescriptionElement> Elements,
	[property: JsonPropertyName("flows")] IReadOnlyList<ProcessDescriptionFlow> Flows,
	[property: JsonPropertyName("parameters")] IReadOnlyList<ProcessDescriptionParameter> Parameters);

/// <summary>One process element (event, activity, gateway, …).</summary>
public sealed record ProcessDescriptionElement(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("dataId")] string DataId,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("label")] string Label,
	[property: JsonPropertyName("parameters")] IReadOnlyList<ProcessDescriptionParameter> Parameters);

/// <summary>One sequence/conditional/default flow between two elements.</summary>
public sealed record ProcessDescriptionFlow(
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("target")] string Target,
	[property: JsonPropertyName("kind")] string Kind);

/// <summary>One process- or element-level parameter.</summary>
public sealed record ProcessDescriptionParameter(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("direction")] string Direction,
	[property: JsonPropertyName("caption")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Caption);
