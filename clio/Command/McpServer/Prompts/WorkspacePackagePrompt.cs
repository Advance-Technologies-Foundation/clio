using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
}
