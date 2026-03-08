using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the new test project MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for generating backend unit test projects")]
public static class CreateTestProjectPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to generate backend unit test scaffolding through MCP.
	/// </summary>
	[McpServerPrompt(Name = "new-test-project"), Description("Prompt to generate a backend unit test project")]
	public static string CreateTestProject(
		[Required]
		[Description("Package name")]
		string packageName,
		[Description("Optional workspace root path")]
		string workspacePath = null,
		[Description("Optional environment name")]
		string environmentName = null) =>
		$"""
		 Use clio mcp server `new-test-project` tool to generate backend unit test scaffolding for package
		 `{packageName}`.
		 Use `workspace-path` when the command must run from a specific workspace root that already contains
		 `.clio/workspaceSettings.json`:
		 `{workspacePath ?? "<current MCP server working directory>"}`.
		 Set `environment-name` only when the caller explicitly provided one:
		 `{environmentName ?? "<not provided>"}`.
		 """;
}
