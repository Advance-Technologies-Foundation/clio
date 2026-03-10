using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for FSM mode and compilation MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for querying/changing FSM mode and compiling Creatio")]
public static class FsmAndCompilePrompt
{
	/// <summary>
	/// Builds a prompt for querying FSM mode.
	/// </summary>
	[McpServerPrompt(Name = Tools.FsmModeTool.GetFsmModeToolName), Description("Prompt to detect whether FSM mode is on or off")]
	public static string GetFsmMode(
		[Required]
		[Description("Registered clio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{Tools.FsmModeTool.GetFsmModeToolName}` to detect whether registered Creatio environment
		 `{environmentName}` is currently in FSM mode on or off.
		 If you need to change the mode afterward, use `{Tools.FsmModeTool.SetFsmModeToolName}`.
		 """;

	/// <summary>
	/// Builds a prompt for changing FSM mode.
	/// </summary>
	[McpServerPrompt(Name = Tools.FsmModeTool.SetFsmModeToolName), Description("Prompt to activate or deactivate FSM mode")]
	public static string SetFsmMode(
		[Required]
		[Description("Registered clio environment name")]
		string environmentName,
		[Required]
		[Description("Target FSM mode value: on or off")]
		string mode) =>
		$"""
		 Use clio mcp server `{Tools.FsmModeTool.SetFsmModeToolName}` to turn FSM mode `{mode}` for registered
		 Creatio environment `{environmentName}`.
		 After changing FSM mode, run `{Tools.CompileCreatioTool.CompileCreatioToolName}` without `package-name`
		 to perform the full `clio cc -e ENV_NAME --all` compilation.
		 """;

	/// <summary>
	/// Builds a prompt for full or package-only compilation.
	/// </summary>
	[McpServerPrompt(Name = Tools.CompileCreatioTool.CompileCreatioToolName), Description("Prompt to compile Creatio fully or by package")]
	public static string CompileCreatio(
		[Required]
		[Description("Registered clio environment name")]
		string environmentName,
		[Description("Optional package name for package-only compilation")]
		string? packageName = null) =>
		string.IsNullOrWhiteSpace(packageName)
			? $"""
			  Use clio mcp server `{Tools.CompileCreatioTool.CompileCreatioToolName}` to run a full compilation for
			  registered Creatio environment `{environmentName}`.
			  Do not pass `package-name` when you need the equivalent of `clio cc -e {environmentName} --all`.
			  """
			: $"""
			  Use clio mcp server `{Tools.CompileCreatioTool.CompileCreatioToolName}` to compile only package
			  `{packageName}` for registered Creatio environment `{environmentName}`.
			  Pass `package-name` exactly as provided to avoid switching to full compilation.
			  """;
}
