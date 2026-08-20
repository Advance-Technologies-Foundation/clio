namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;

/// <summary>Options for the <c>get-classic-list-columns</c> command.</summary>
[Verb("get-classic-list-columns",
	HelpText = "Resolve the effective default column set of a Classic section list without changing Creatio data")]
public class GetClassicListColumnsOptions : EnvironmentOptions {

	/// <summary>Classic section client-unit schema name.</summary>
	[Option("schema-name", Required = true, HelpText = "Classic section schema name, for example 'ContactSectionV2'")]
	public string SchemaName { get; set; }

	/// <summary>Skips the saved grid profile and reports only what the section declares in code.</summary>
	/// <remarks>
	/// The saved profile is what the section actually renders, so it leads the resolution order and answers for
	/// nearly every product section. This switch exists for the opposite question — "what does the section
	/// DECLARE?" — which is the only answer a code reader can verify, and it makes the static branches
	/// observable on a stand whose profiles are seeded.
	/// </remarks>
	[Option("ignore-profile", Required = false,
		HelpText = "Skip the saved grid profile and resolve only statically declared columns")]
	public bool IgnoreProfile { get; set; }
}

/// <summary>One resolved Classic list column.</summary>
/// <param name="Name">Column path as declared by the section.</param>
/// <param name="Caption">Entity column title, omitted for a dotted traversal path.</param>
/// <param name="Origin">
/// Which Classic method declared this path: <c>getGridDataColumns</c>, <c>initColumnsConfig</c>, or
/// <c>both</c>. The two are not interchangeable — <c>initColumnsConfig</c> describes what the grid RENDERS,
/// <c>getGridDataColumns</c> what the section LOADS — so a flattened list alone cannot tell a consumer
/// whether a column is displayed or merely loaded. Carrying the origin keeps that choice with the consumer:
/// take the rendered set, the loaded set, or the union under its own fidelity rules. Omitted when the columns
/// did not come from the section schema (the entity-default fallback).
/// </param>
public sealed record ClassicListColumnInfo(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Caption,
	[property: JsonPropertyName("origin")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Origin = null);

/// <summary>Response returned by <c>get-classic-list-columns</c>.</summary>
public sealed class GetClassicListColumnsResponse {

	/// <summary>Whether the section and its list-column fallback were resolved successfully.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>Requested Classic section schema.</summary>
	[JsonPropertyName("sectionSchema")]
	public string SectionSchema { get; set; }

	/// <summary>Entity schema bound to the Classic section.</summary>
	[JsonPropertyName("entity")]
	public string Entity { get; set; }

	/// <summary>
	/// Resolution source: <c>profile</c>, <c>schema-default</c>, <c>entity-default</c>, or <c>none</c>.
	/// <see langword="null"/> on a failure response, where no source was resolved and naming one would be a
	/// claim the command cannot make.
	/// </summary>
	[JsonPropertyName("source")]
	public string Source { get; set; }

	/// <summary>
	/// Active view the saved profile named, for example <c>GridDataView</c>. Omitted unless the columns came
	/// from a profile.
	/// </summary>
	[JsonPropertyName("view")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string View { get; set; }

	/// <summary>
	/// Which stored configuration the columns came from: <c>listed</c> or <c>tiled</c>. A Classic grid stores
	/// both, with different sets and orders, so a profile answer is incomplete without saying which one it is.
	/// Omitted unless the columns came from a profile.
	/// </summary>
	[JsonPropertyName("viewType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ViewType { get; set; }

	/// <summary>
	/// Whether a profile answer is the shared default (<c>shared</c>), possibly the calling user's own
	/// customization (<c>user</c>), or unclassifiable (<c>unknown</c>). Omitted unless the columns came from a
	/// profile. This is the field that keeps the command from presenting one user's saved layout as the
	/// section's canonical set. It classifies the GRID-SETTINGS row only: the active-view profile that selects
	/// which view is reported is not classified, so a personal active-view selection can still steer a
	/// <c>shared</c> answer.
	/// </summary>
	[JsonPropertyName("profileScope")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ProfileScope { get; set; }

	/// <summary>Ordered effective default list columns.</summary>
	[JsonPropertyName("columns")]
	public IReadOnlyList<ClassicListColumnInfo> Columns { get; set; } = [];

	/// <summary>Non-fatal resolution details; empty when no details are needed.</summary>
	[JsonPropertyName("notes")]
	public IReadOnlyList<string> Notes { get; set; } = [];

	/// <summary>Failure reason when <see cref="Success"/> is <see langword="false"/>.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; set; }
}

/// <summary>Prints the effective default columns of a Classic section list as JSON.</summary>
public class GetClassicListColumnsCommand(IClassicListColumnResolver resolver, ILogger logger)
	: Command<GetClassicListColumnsOptions> {

	/// <summary>Resolves the list-column result without writing to the target environment.</summary>
	/// <param name="options">Command options containing the section schema name.</param>
	/// <param name="response">Resolved response or a failure envelope.</param>
	/// <returns><see langword="true"/> when resolution completed successfully.</returns>
	public virtual bool TryResolve(
		GetClassicListColumnsOptions options,
		out GetClassicListColumnsResponse response) {
		string schemaName = options?.SchemaName;
		try {
			ArgumentNullException.ThrowIfNull(options);
			response = resolver.Resolve(schemaName, options.IgnoreProfile);
			return true;
		}
		catch (Exception exception) {
			response = new GetClassicListColumnsResponse {
				Success = false,
				SectionSchema = schemaName,
				Columns = [],
				Notes = [],
				Error = exception.Message
			};
			return false;
		}
	}

	/// <inheritdoc />
	public override int Execute(GetClassicListColumnsOptions options) {
		bool success = TryResolve(options, out GetClassicListColumnsResponse response);
		// Notes and Error can both carry an inner exception message from the pipeline, which routinely holds a
		// host:port or a full request URI. The MCP tool redacts before returning; the CLI writes the
		// serialized response straight to stdout, so it has to redact here too.
		// Error is the rawer of the two — it is exception.Message verbatim from TryResolve's catch, so it
		// catches every exception in the pipeline (the ESQ call, the designer JSON parse, the entity metadata
		// read), not just the one GetDesignPackageUId message that reaches Notes. The redactor returns already
		// clean text unchanged, so the caller-actionable messages survive intact.
		if (!string.IsNullOrEmpty(response?.Error)) {
			response.Error = Clio.Command.McpServer.SensitiveErrorTextRedactor.Redact(response.Error);
		}
		if (response?.Notes is {Count: > 0}) {
			response.Notes = Clio.Command.McpServer.SensitiveErrorTextRedactor.RedactAll(response.Notes);
		}
		logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}
}
