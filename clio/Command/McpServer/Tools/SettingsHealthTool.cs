using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SettingsHealthTool(ISettingsBootstrapService settingsBootstrapService) {
	internal const string ToolName = "settings-health";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Reports the clio bootstrap health for appsettings.json, including auto-repairs, active environment resolution, and whether environment-scoped tools can execute.")]
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
