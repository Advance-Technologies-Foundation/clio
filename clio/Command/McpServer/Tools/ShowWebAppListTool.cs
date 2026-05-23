using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for listing registered web applications with their configured settings.
/// </summary>
[McpServerToolType]
public sealed class ShowWebAppListTool(ShowAppListCommand command)
{
	/// <summary>
	/// Stable MCP tool name for listing registered clio environments.
	/// </summary>
	internal const string ShowWebAppListToolName = "list-environments";

	/// <summary>
	/// Returns all registered web application settings as structured MCP JSON with sensitive fields masked.
	/// </summary>
	[McpServerTool(Name = ShowWebAppListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Shows the list of registered web applications and their settings as structured JSON. Sensitive values such as passwords are masked.")]
	public IReadOnlyList<ShowWebAppSettingsResult> ShowWebAppList()
	{
		return command.GetAllWebAppSettings(maskSensitiveData: true);
	}
}
