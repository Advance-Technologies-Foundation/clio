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
}

/// <summary>One resolved Classic list column.</summary>
public sealed record ClassicListColumnInfo(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Caption);

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

	/// <summary>Resolution source: <c>schema-default</c>, <c>entity-default</c>, or <c>none</c>.</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; }

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
			response = resolver.Resolve(schemaName);
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
		logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}
}
