using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for workspace package and test project MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for adding workspace packages and creating workspace test projects")]
public static class WorkspacePackagePrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to add a package to a specific workspace.
	/// </summary>
	[McpServerPrompt(Name = "add-package"), Description("Prompt to add a package to a workspace")]
	public static string AddPackage(
		[Required]
		[Description("Package name")]
		string name,
		[Required]
		[Description("Absolute path to the local workspace")]
		string workspacePath,
		[Description("Creatio environment name used for follow-up download when needed")]
		string environmentName = null,
		[Description("Whether to create an application descriptor for the package")]
		bool asApp = false,
		[Description("Path to a Creatio zip file or extracted directory to get configuration from")]
		string buildZipPath = null) =>
		$"""
		 Use clio mcp server `add-package` tool to create package `{name}` in the workspace at
		 `{workspacePath}`.
		 Pass `workspace-path` exactly as provided and set `as-app` to `{asApp}`.
		 Use `build-zip-path` value `{buildZipPath ?? "<not provided>"}` when configuration should come
		 from a local build archive or extracted directory.
		 When `build-zip-path` is not provided, the follow-up flow may need environment access to
		 download configuration, so use `environment-name` `{environmentName ?? "<not provided>"}` when
		 that remote download is required.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to create a test project in a specific workspace.
	/// </summary>
	[McpServerPrompt(Name = "new-test-project"), Description("Prompt to create a workspace test project")]
	public static string NewTestProject(
		[Required]
		[Description("Workspace package name")]
		string packageName,
		[Required]
		[Description("Absolute path to the local workspace")]
		string workspacePath,
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `new-test-project` tool to create a test project for workspace package
		 `{packageName}` in the workspace at `{workspacePath}`.
		 Pass `workspace-path` exactly as provided and use `environment-name` `{environmentName}`.
		 Operate on the specified workspace package only.
		""";

	/// <summary>Builds a prompt for portable integration-test project creation.</summary>
	[McpServerPrompt(Name = "new-integration-test-project"), Description("Prompt to create a Creatio integration-test project")]
	public static string NewIntegrationTestProject(
		[Required, Description("Workspace package name")] string packageName,
		[Required, Description("Absolute path to the local workspace")] string workspacePath,
		[Description("Target framework")] string targetFramework = "net10.0") =>
		$"""
		 Read get-guidance name=integration-testing, then use the `new-integration-test-project` tool
		 to create a portable integration-test project for `{packageName}` in `{workspacePath}` with
		 target framework `{targetFramework}`. Do not embed credentials or assume a local clio environment.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to inspect packages installed in an environment.
	/// </summary>
	[McpServerPrompt(Name = GetPkgListTool.GetPkgListToolName), Description("Prompt to list environment packages")]
	public static string GetPkgList(
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Optional package-name filter")]
		string? filter = null) =>
		string.IsNullOrWhiteSpace(filter)
			? $"""
			   Use clio mcp server `{GetPkgListTool.GetPkgListToolName}` tool to list packages installed in
			   Creatio environment `{environmentName}`.
			   Pass `environment-name` exactly as provided and omit `filter` when you need the full package list.
			   """
			: $"""
			   Use clio mcp server `{GetPkgListTool.GetPkgListToolName}` tool to list packages installed in
			   Creatio environment `{environmentName}`.
			   Pass `environment-name` exactly as provided and use `filter` `{filter}` to narrow the result.
			   """;
}
