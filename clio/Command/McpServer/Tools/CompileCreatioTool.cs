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
	/// <summary>
	/// Stable MCP tool name for compilation operations.
	/// </summary>
	internal const string CompileCreatioToolName = "compile-creatio";

	/// <summary>
	/// Compiles Creatio fully or rebuilds a single package for a registered environment.
	/// </summary>
	[McpServerTool(Name = CompileCreatioToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Long-running, may take several minutes; recompiles a registered Creatio environment and forces a runtime reload. Omit `package-name` to run a full compilation (`clio cc -e ENV_NAME --all`). Provide `package-name` to compile only one package. Call only when: (1) C# schemas were added or modified, (2) `set-fsm-mode` has just been toggled, or (3) the runtime reports a missing-in-runtime/schema-not-found error. Do NOT call after `create-app`, `update-page`, `sync-pages`, `update-entity-schema`, `create-page`, or any Freedom UI page-body edit — those changes are AMD modules applied at runtime and DDL is handled by `update-entity-schema`.")]
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
		// FR-05: per-tenant lock keyed by the environment this compilation resolves under (replaces the
		// former tool-local static lock that serialized compile-creatio across ALL tenants).
		string tenantKey = options is EnvironmentOptions environmentOptions
			? commandResolver.GetTenantKey(environmentOptions)
			: McpToolExecutionLock.SharedFallbackKey;
		lock (McpToolExecutionLock.GetLock(tenantKey))
		{
			McpToolExecutionLock.MarkInUse(tenantKey);
			// CompileCreatioTool builds its result from logger.LogMessages, which ConsoleLogger only
			// populates while PreserveMessages is true. Set/restore it locally (like BaseTool,
			// SchemaSyncTool, EntitySchemaTool, AddItemModelTool) so compilation output is captured
			// regardless of transport: the stdio path sets the flag process-wide, the HTTP transport
			// does not, so relying on the global flag returns empty output over HTTP.
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
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
				List<LogMessage> logMessages = [.. logger.LogMessages, new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))];
				CommandExecutionResult result = new(1, logMessages);
				logger.ClearMessages();
				return result;
			}
			finally
			{
				logger.PreserveMessages = previousPreserveMessages;
				McpToolExecutionLock.MarkAvailable(tenantKey);
			}
		}
	}
}

/// <summary>
/// MCP arguments for Creatio compilation operations.
/// </summary>
public sealed record CompileCreatioArgs(
	[property: JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[Description("Optional package name. When omitted, the tool performs a full compilation.")]
	string? PackageName);
