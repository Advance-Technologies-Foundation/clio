using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

public abstract record SchemaGetBaseArgs {
	[JsonPropertyName("output-file")]
	[Description("Optional absolute path to write the schema body to. When set, body is omitted from the response.")]
	public string? OutputFile { get; init; }

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
