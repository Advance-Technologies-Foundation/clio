using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

// Connection args shared by the schema read/list MCP tools: prefer environment-name; uri/login/password are
// the direct-connection fallback. Centralized so every tool exposes an identical connection arg surface.
public abstract record ConnectionArgsBase {
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	public string? EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	[Description(McpToolDescriptions.Uri)]
	public string? Uri { get; init; }

	[JsonPropertyName("login")]
	[Description(McpToolDescriptions.Login)]
	public string? Login { get; init; }

	[JsonPropertyName("password")]
	[Description(McpToolDescriptions.Password)]
	public string? Password { get; init; }
}

public abstract record SchemaGetBaseArgs : ConnectionArgsBase {
	[JsonPropertyName("output-file")]
	[Description("Optional absolute path to write the schema body to. When set, body is omitted from the response.")]
	public string? OutputFile { get; init; }
}

public abstract record SchemaCreateBaseArgs {
	[JsonPropertyName("caption")]
	[Description("Optional display caption. Defaults to schema-name when omitted.")]
	public string? Caption { get; init; }

	[JsonPropertyName("description")]
	[Description("Optional schema description.")]
	public string? Description { get; init; }

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	public string? EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	[Description("Direct Creatio URL. Use only for bootstrap or before environment registration.")]
	public string? Uri { get; init; }

	[JsonPropertyName("login")]
	[Description(McpToolDescriptions.Login)]
	public string? Login { get; init; }

	[JsonPropertyName("password")]
	[Description(McpToolDescriptions.Password)]
	public string? Password { get; init; }
}
