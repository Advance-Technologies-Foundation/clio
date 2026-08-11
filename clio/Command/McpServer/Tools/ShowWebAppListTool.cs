using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Structured result of the <c>list-environments</c> MCP tool.
/// </summary>
/// <param name="Environments">The registered environments as read from the settings file at call time.</param>
/// <param name="SettingsFilePath">
/// The settings file the list came from. Carried so a caller that edited a file elsewhere (a different
/// <c>CLIO_HOME</c>, another user profile) can see that this server reads a different one.
/// </param>
/// <param name="Warnings">
/// Non-fatal problems found while reading the settings file, or <c>null</c> when there were none. A
/// warning means the returned list is the last state this server managed to read, not the file content.
/// </param>
public sealed record ShowWebAppListToolResult(
	[property: JsonPropertyName("environments")] IReadOnlyList<ShowWebAppSettingsResult> Environments,
	[property: JsonPropertyName("settingsFilePath")] string SettingsFilePath,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

/// <summary>
/// MCP tool surface for listing registered web applications with their configured settings.
/// </summary>
[McpServerToolType]
public sealed class ShowWebAppListTool(ShowAppListCommand command, ISettingsRepository settingsRepository)
{
	/// <summary>
	/// Stable MCP tool name for listing registered clio environments.
	/// </summary>
	internal const string ShowWebAppListToolName = "list-environments";

	/// <summary>
	/// Returns all registered web application settings as structured MCP JSON with sensitive fields masked.
	/// </summary>
	/// <remarks>
	/// The settings file is re-read before the list is built. The MCP server is a long-lived process, so
	/// without that step this tool would answer from the snapshot taken at process start and report a
	/// missing environment as missing even after it had been registered — exactly when someone is
	/// diagnosing a registration problem. An unreadable file is not an error here: the previously loaded
	/// list is returned together with a warning.
	/// </remarks>
	[McpServerTool(Name = ShowWebAppListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Shows the list of registered web applications and their settings as structured JSON, "
		+ "read from appsettings.json at call time. Sensitive values such as passwords are masked. "
		+ "Returns {environments, settingsFilePath, warnings}.")]
	public ShowWebAppListToolResult ShowWebAppList()
	{
		SettingsReloadResult reload = settingsRepository.Reload();
		IReadOnlyList<string> warnings = string.IsNullOrWhiteSpace(reload?.Warning)
			? null
			: new[] { reload.Warning };
		return new ShowWebAppListToolResult(
			command.GetAllWebAppSettings(maskSensitiveData: true),
			settingsRepository.AppSettingsFilePath,
			warnings);
	}
}
