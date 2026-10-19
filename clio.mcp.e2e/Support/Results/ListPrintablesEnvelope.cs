using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Wire envelope for the <c>list-printables</c> tool: the shared <c>EnvironmentProbeResponse</c> success/error
/// contract plus the printables <c>ListPrintablesCommand</c> returns. Parsed from the real MCP tool result via
/// <see cref="EntitySchemaStructuredResultParser.Extract{T}"/>.
/// </summary>
internal sealed record ListPrintablesEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string? Error,
	[property: JsonPropertyName("count")] int Count,
	[property: JsonPropertyName("printables")] IReadOnlyList<ListPrintableRow>? Printables);

/// <summary>
/// One printable of a <c>list-printables</c> result, mirroring <c>PrintableSummary</c>: <c>templateId</c> is the
/// id a <c>fileProcessing</c> element's <c>report.printable</c> takes, and the two schema names are the printable's
/// own object and its section's object (the run time falls back to the second when the first is empty).
/// </summary>
internal sealed record ListPrintableRow(
	[property: JsonPropertyName("templateId")] string TemplateId,
	[property: JsonPropertyName("printableCaption")] string? PrintableCaption,
	[property: JsonPropertyName("convertInPDF")] bool ConvertInPdf,
	[property: JsonPropertyName("showInCard")] bool ShowInCard,
	[property: JsonPropertyName("showInSection")] bool ShowInSection,
	[property: JsonPropertyName("entitySchemaName")] string? EntitySchemaName,
	[property: JsonPropertyName("moduleEntitySchemaName")] string? ModuleEntitySchemaName);
