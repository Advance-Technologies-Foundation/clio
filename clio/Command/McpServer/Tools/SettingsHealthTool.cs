using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SettingsHealthTool(ISettingsBootstrapService settingsBootstrapService) {
	internal const string ToolName = "check-settings-health";

	[McpToolExecution(
		Location = McpToolExecutionLocation.InProcess,
		Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Reports the clio bootstrap health for appsettings.json, including auto-repairs, active "
		+ "environment resolution, and whether environment-scoped tools can execute. An issue with code "
		+ "'settings-shape-mismatch' means the file is valid JSON that this clio build cannot fully bind: "
		+ "with can-execute-env-tools true the configuration is degraded but usable and clio refuses every "
		+ "settings write until it is resolved; do NOT edit or repair the file when the message says a "
		+ "newer clio wrote it - restart the MCP session (or run update-cli) instead; when the message "
		+ "instead names a member to correct by hand, that member is a mistake in the file.")]
	public SettingsHealthResult GetSettingsHealth() {
		SettingsBootstrapReport report = settingsBootstrapService.GetReport();
		return new SettingsHealthResult(
			report.Status,
			report.SettingsFilePath,
			report.ActiveEnvironmentKey,
			report.ResolvedActiveEnvironmentKey,
			report.EnvironmentCount,
			report.Issues.Select(issue => new SettingsHealthIssue(issue.Code, issue.Message)).ToArray(),
			report.RepairsApplied.Select(repair => new SettingsHealthRepair(repair.Code, repair.Message)).ToArray(),
			report.CanStartBootstrapTools,
			report.CanExecuteEnvTools);
	}
}

public sealed record SettingsHealthResult(
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("settings-file-path")] string SettingsFilePath,
	[property: JsonPropertyName("active-environment-key")] string? ActiveEnvironmentKey,
	[property: JsonPropertyName("resolved-active-environment-key")] string? ResolvedActiveEnvironmentKey,
	[property: JsonPropertyName("environment-count")] int EnvironmentCount,
	[property: JsonPropertyName("issues")] IReadOnlyList<SettingsHealthIssue> Issues,
	[property: JsonPropertyName("repairs-applied")] IReadOnlyList<SettingsHealthRepair> RepairsApplied,
	[property: JsonPropertyName("can-start-bootstrap-tools")] bool CanStartBootstrapTools,
	[property: JsonPropertyName("can-execute-env-tools")] bool CanExecuteEnvTools
);

public sealed record SettingsHealthIssue(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("message")] string Message
);

public sealed record SettingsHealthRepair(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("message")] string Message
);
