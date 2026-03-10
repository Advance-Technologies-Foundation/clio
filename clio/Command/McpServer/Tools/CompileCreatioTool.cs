using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for full Creatio compilation and package-only compilation.
/// </summary>
[McpServerToolType]
public sealed class CompileCreatioTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
{
	private static readonly object CommandExecutionLock = new();

	/// <summary>
	/// Stable MCP tool name for compilation operations.
	/// </summary>
	internal const string CompileCreatioToolName = "compile-creatio";

	/// <summary>
	/// Compiles Creatio fully or rebuilds a single package for a registered environment.
	/// </summary>
	[McpServerTool(Name = CompileCreatioToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Compiles a registered Creatio environment. Omit `package-name` to run a full compilation (`clio cc -e ENV_NAME --all`). Provide `package-name` to compile only one package. This tool is recommended after `set-fsm-mode`.")]
	public CommandExecutionResult CompileCreatio(
		[Description("Compilation parameters")] [Required] CompileCreatioArgs args)
	{
		if (!string.IsNullOrWhiteSpace(args.PackageName) && args.PackageName.Contains(',', StringComparison.Ordinal))
		{
			return new CommandExecutionResult(1, [
				new ErrorMessage("`package-name` must contain exactly one package name. Comma-separated package lists are not supported by `compile-creatio`.")
			]);
		}

		return string.IsNullOrWhiteSpace(args.PackageName)
			? ExecuteFullCompile(args.EnvironmentName)
			: ExecutePackageCompile(args.EnvironmentName, args.PackageName.Trim());
	}

	private CommandExecutionResult ExecuteFullCompile(string environmentName)
	{
		CompileConfigurationOptions options = new()
		{
			Environment = environmentName,
			All = true
		};
		CompileConfigurationCommand command = commandResolver.Resolve<CompileConfigurationCommand>(options);
		return Execute(command, options);
	}

	private CommandExecutionResult ExecutePackageCompile(string environmentName, string packageName)
	{
		CompilePackageOptions options = new()
		{
			Environment = environmentName,
			PackageName = packageName
		};
		CompilePackageCommand command = commandResolver.Resolve<CompilePackageCommand>(options);
		return Execute(command, options);
	}

	private CommandExecutionResult Execute<TOptions>(Command<TOptions> command, TOptions options)
	{
		int exitCode = -1;
		lock (CommandExecutionLock)
		{
			try
			{
				exitCode = command.Execute(options);
				Thread.Sleep(500);
				CommandExecutionResult result = new(exitCode, [.. logger.LogMessages.ToList()]);
				logger.ClearMessages();
				return result;
			}
			catch (Exception exception)
			{
				List<LogMessage> logMessages = [.. logger.LogMessages, new ErrorMessage(exception.Message)];
				CommandExecutionResult result = new(1, logMessages);
				logger.ClearMessages();
				return result;
			}
		}
	}
}

/// <summary>
/// MCP arguments for Creatio compilation operations.
/// </summary>
public sealed record CompileCreatioArgs(
	[property: JsonPropertyName("environment-name")]
	[Description("Registered clio environment name")]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[Description("Optional package name. When omitted, the tool performs a full compilation.")]
	string? PackageName);
