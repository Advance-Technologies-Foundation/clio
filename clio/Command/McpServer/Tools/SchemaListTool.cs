using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that lists schemas of the requested type.
/// At present only <c>schema-type=entity</c> is supported and delegates to
/// <see cref="FindEntitySchemaTool"/>. Other schema types reserve the surface
/// for future implementations.
/// </summary>
[McpServerToolType]
public sealed class SchemaListTool(FindEntitySchemaTool entityFinder) {
	internal const string ToolName = "list-schemas";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"List or search schemas of the requested type. Currently supports schema-type=entity. " +
		"For entity, provide environment-name plus exactly one of schema-name (exact match), search-pattern " +
		"(case-insensitive contains), or uid (Guid exact match).")]
	public object List(
		[Description("List-schemas parameters")] [Required] SchemaListArgs args) {
		CommandExecutionResult modeError = CommandExecutionResult.ValidateExactlyOneMode(
			"schema-type", args.SchemaType, SchemaCreateTool.SchemaTypeEntity);
		if (modeError != null) {
			return modeError;
		}

		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		return entityFinder.FindEntitySchema(new FindEntitySchemaArgs(
			args.EnvironmentName,
			args.SchemaName,
			args.SearchPattern,
			args.Uid));
	}
}

/// <summary>
/// Arguments for the consolidated <c>list-schemas</c> MCP tool.
/// </summary>
public sealed record SchemaListArgs(
	[property: JsonPropertyName("schema-type")]
	[property: Description("Discriminator: currently only 'entity' is supported.")]
	[property: Required]
	string SchemaType,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Optional exact match by schema name.")]
	string? SchemaName = null,

	[property: JsonPropertyName("search-pattern")]
	[property: Description("Optional case-insensitive substring search across schema names.")]
	string? SearchPattern = null,

	[property: JsonPropertyName("uid")]
	[property: Description("Optional exact match by schema UId (Guid).")]
	string? Uid = null
);
