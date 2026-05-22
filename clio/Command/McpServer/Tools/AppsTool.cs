using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that reads one installed Creatio application by id or code, or lists
/// every installed application when neither identifier is provided. Folds the legacy
/// <c>list-apps</c> and <c>get-app-info</c> tools.
/// </summary>
[McpServerToolType]
public sealed class AppsTool(
	ApplicationGetListTool listTool,
	ApplicationGetInfoTool infoTool) {

	internal const string ToolName = "apps";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Lists installed Creatio applications when no identifier is provided; returns full app info (primary package and runtime entity metadata) when `id` or `code` is supplied. Provide at most one of id/code.")]
	public object Read(
		[Description("Parameters: environment-name (required); optional id or code (provide at most one).")] [Required]
		AppsArgs args) {
		bool hasId = !string.IsNullOrWhiteSpace(args.Id);
		bool hasCode = !string.IsNullOrWhiteSpace(args.Code);
		if (hasId || hasCode) {
			return infoTool.ApplicationGetInfo(new ApplicationGetInfoArgs(args.EnvironmentName!, args.Id, args.Code));
		}
		return listTool.ApplicationGetList(new ApplicationGetListArgs(args.EnvironmentName!));
	}
}

/// <summary>
/// Arguments for the consolidated <c>apps</c> MCP tool.
/// </summary>
public sealed record AppsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("id")]
	[property: Description("Optional application identifier (Guid). Provide id or code, never both.")]
	string? Id = null,

	[property: JsonPropertyName("code")]
	[property: Description("Optional application code. Provide id or code, never both.")]
	string? Code = null
);
