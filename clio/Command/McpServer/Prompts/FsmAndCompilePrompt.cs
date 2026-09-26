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
		 to perform the full `clio cc -e ENV_NAME --all` compilation — but first warn the user that compilation is a
		 heavy operation affecting all connected users and confirm they want to compile now rather than postpone.
		 On .NET Framework hosts, also run
		 `{Tools.RestartTool.RestartByEnvironmentNameToolName}` afterward — new C# does not load until the
		 app restarts; it waits for readiness by default, so no separate poll is needed.
		 """;

	/// <summary>
	/// Builds a prompt for full, package-only, or business-process compilation.
	/// </summary>
	[McpServerPrompt(Name = Tools.CompileCreatioTool.CompileCreatioToolName), Description("Prompt to compile Creatio fully, by package, or for one business process")]
	public static string CompileCreatio(
		[Required]
		[Description("Registered clio environment name")]
		string environmentName,
		[Description("Optional package name for package-only compilation")]
		string? packageName = null,
		[Description("Optional business process code: compile the package that process is in")]
		string? processName = null) =>
		// A blank scope is refused by the tool rather than read as "omitted", so the prompt does not hand out the
		// full-compilation guidance for one either.
		(processName is not null && string.IsNullOrWhiteSpace(processName))
			|| (packageName is not null && string.IsNullOrWhiteSpace(packageName))
			? $"""
			  An empty `package-name` or `process-name` is not a request for a full compilation: `{Tools.CompileCreatioTool.CompileCreatioToolName}`
			  refuses it. Ask the user which package or business process to compile on `{environmentName}`, or omit both
			  arguments for a full compilation - after warning the user that any compile reloads the runtime for every
			  connected user and getting their confirmation.
			  """
			: !string.IsNullOrWhiteSpace(processName)
			? $"""
			  Compilation is a HEAVY operation that forces a runtime reload affecting every user connected to
			  `{environmentName}`. First warn the user and ask whether to compile now or postpone (every time, not
			  once per session — a prior request or answer is not standing consent, so re-ask even for an identical repeat); only proceed after they confirm. If they postpone, do NOT compile — tell them it
			  can be run later.
			  Once confirmed, use clio mcp server `{Tools.CompileCreatioTool.CompileCreatioToolName}` with `process-name`
			  `{processName}` for registered Creatio environment `{environmentName}`: it compiles the package that process is in
			  through CrtProcessBuilder (1.6.6.33 or newer) and reports the compiler errors, the process's own first. It is the
			  compile a script task or process methods saved by create/modify-business-process need; on Creatio 10.x a
			  `package-name` compile does not pick such a save up and a full one takes about 20 minutes. Do not pass
			  `package-name` with it.
			  If the tool returns exit-code 0 with an in-progress note, it is still running server-side —
			  poll `{Tools.CompileStatusTool.CompileStatusToolName}` instead of retrying.
			  """
			: string.IsNullOrWhiteSpace(packageName)
			? $"""
			  Compilation is a HEAVY operation that forces a runtime reload affecting every user connected to
			  `{environmentName}`. First warn the user and ask whether to compile now or postpone (every time, not
			  once per session — a prior request or answer is not standing consent, so re-ask even for an identical repeat); only proceed after they confirm. If they postpone, do NOT compile — tell them it
			  can be run later (ask you again, or run `clio cc -e {environmentName} --all`).
			  Once confirmed, use clio mcp server `{Tools.CompileCreatioTool.CompileCreatioToolName}` to run a full compilation for
			  registered Creatio environment `{environmentName}`.
			  Do not pass `package-name` when you need the equivalent of `clio cc -e {environmentName} --all`.
			  A full compilation can take several minutes; if the tool returns exit-code 0 with an
			  in-progress note, it is still running server-side — poll `{Tools.CompileStatusTool.CompileStatusToolName}` instead of retrying.
			  """
			: $"""
			  Compilation is a HEAVY operation that forces a runtime reload affecting every user connected to
			  `{environmentName}`. First warn the user and ask whether to compile now or postpone (every time, not
			  once per session — a prior request or answer is not standing consent, so re-ask even for an identical repeat); only proceed after they confirm. If they postpone, do NOT compile — tell them it
			  can be run later.
			  Once confirmed, use clio mcp server `{Tools.CompileCreatioTool.CompileCreatioToolName}` to compile only package
			  `{packageName}` for registered Creatio environment `{environmentName}`.
			  Pass `package-name` exactly as provided to avoid switching to full compilation.
			  If the tool returns exit-code 0 with an in-progress note, it is still running server-side —
			  poll `{Tools.CompileStatusTool.CompileStatusToolName}` instead of retrying.
			  """;
}
