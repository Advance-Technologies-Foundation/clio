using System;
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared executor behind the <c>clio-run</c> / <c>clio-run-destructive</c> tools: resolves the
/// options type for a verb, binds the free-form JSON args, enforces the destructiveness gate for the
/// requesting surface, and dispatches through the generalized env-scoped executor.
/// </summary>
public interface IClioRunExecutor {
	/// <summary>
	/// Runs <paramref name="command"/> with <paramref name="args"/> on the requested surface.
	/// </summary>
	/// <param name="command">The verb name or alias.</param>
	/// <param name="args">Free-form JSON args object (kebab-keyed option names).</param>
	/// <param name="destructiveSurface">
	/// <c>true</c> when invoked from <c>clio-run-destructive</c>; <c>false</c> from <c>clio-run</c>.
	/// </param>
	/// <returns>The uniform <see cref="CommandExecutionResult"/> envelope.</returns>
	CommandExecutionResult Run(string command, JsonElement? args, bool destructiveSurface);
}

/// <inheritdoc />
public sealed class ClioRunExecutor(
	ICommandOptionsRegistry optionsRegistry,
	IClioRunArgBinder argBinder,
	ICommandDestructivenessClassifier destructivenessClassifier,
	IEnvironmentScopedCommandExecutor commandExecutor) : IClioRunExecutor {

	/// <inheritdoc />
	public CommandExecutionResult Run(string command, JsonElement? args, bool destructiveSurface) {
		if (string.IsNullOrWhiteSpace(command)) {
			return CommandExecutionResult.FromError("Error: 'command' is required.");
		}
		string verb = command.Trim();

		if (!optionsRegistry.TryResolveOptionsType(verb, out Type optionsType)) {
			return CommandExecutionResult.FromError(
				$"Error: unknown command '{verb}'. It is not a registered clio verb or alias.");
		}

		CommandExecutionResult gateFailure = EnforceDestructivenessGate(verb, destructiveSurface);
		if (gateFailure is not null) {
			return gateFailure;
		}

		ClioRunBindResult bindResult = argBinder.Bind(verb, optionsType, args);
		if (!bindResult.Success) {
			return CommandExecutionResult.FromError(bindResult.ErrorText);
		}

		if (bindResult.Options is not EnvironmentOptions environmentOptions) {
			return CommandExecutionResult.FromError(
				$"Error: command '{verb}' is not supported by clio-run (its options are not environment-aware).");
		}

		return commandExecutor.ResolveAndExecute(environmentOptions);
	}

	private CommandExecutionResult EnforceDestructivenessGate(string verb, bool destructiveSurface) {
		bool isDestructive = destructivenessClassifier.IsDestructive(verb);
		if (destructiveSurface && !isDestructive) {
			return CommandExecutionResult.FromError(
				$"Error: command '{verb}' is not destructive; run it via 'clio-run' instead of 'clio-run-destructive'.");
		}
		if (!destructiveSurface && isDestructive) {
			return CommandExecutionResult.FromError(
				$"Error: command '{verb}' is destructive (or its safety is unknown); run it via 'clio-run-destructive'.");
		}
		return null;
	}
}

/// <summary>
/// Generic, non-destructive MCP executor for the long tail of clio commands. Never
/// <see cref="McpServerToolAttribute.ReadOnly"/> / auto-approve. Refuses destructive commands.
/// </summary>
[McpServerToolType]
public sealed class ClioRunTool(IClioRunExecutor executor) {

	/// <summary>Stable MCP tool name for the safe generic executor.</summary>
	internal const string ToolName = "clio-run";

	/// <summary>
	/// Runs a non-destructive clio command by verb name with free-form JSON arguments.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Generic executor for the non-destructive long tail of clio commands. `command` is a clio verb name (kebab-case) and `args` is a free-form JSON object whose keys are the command's `--kebab-option` long names. Refuses destructive commands (use `clio-run-destructive`). Unknown command or invalid args return a structured Error result. NOT auto-approved.")]
	public CommandExecutionResult Run(
		[Description("clio verb name (kebab-case), e.g. \"get-app-info\"")] string command,
		[Description("Free-form JSON object of kebab-case option names → values")] JsonElement? args = null)
		=> executor.Run(command, args, destructiveSurface: false);
}

/// <summary>
/// Generic MCP executor for destructive clio commands. Routes only commands classified as
/// destructive; refuses non-destructive ones.
/// </summary>
[McpServerToolType]
public sealed class ClioRunDestructiveTool(IClioRunExecutor executor) {

	/// <summary>Stable MCP tool name for the destructive generic executor.</summary>
	internal const string ToolName = "clio-run-destructive";

	/// <summary>
	/// Runs a destructive clio command by verb name with free-form JSON arguments.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Generic executor for DESTRUCTIVE clio commands (delete/uninstall/compile/restore/schema changes, etc.). `command` is a clio verb name (kebab-case) and `args` is a free-form JSON object of `--kebab-option` long names. Refuses non-destructive commands (use `clio-run`). Unknown command or invalid args return a structured Error result. Hosts should require confirmation.")]
	public CommandExecutionResult Run(
		[Description("clio verb name (kebab-case), e.g. \"delete-schema\"")] string command,
		[Description("Free-form JSON object of kebab-case option names → values")] JsonElement? args = null)
		=> executor.Run(command, args, destructiveSurface: true);
}
