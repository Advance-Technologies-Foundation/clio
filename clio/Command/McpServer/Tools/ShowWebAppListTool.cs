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
	/// Stable MCP tool name for listing registered web applications.
	/// </summary>
	internal const string ShowWebAppListToolName = "show-webApp-list";

	/// <summary>
	/// Returns all registered web application settings as structured MCP JSON without masking sensitive fields.
	/// </summary>
	[McpServerTool(Name = ShowWebAppListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Shows the list of registered web applications and their settings as structured JSON without masking sensitive values.")]
	public IReadOnlyList<ShowWebAppSettingsResult> ShowWebAppList()
	{
		return command.GetAllWebAppSettings(maskSensitiveData: false);
	}
}
