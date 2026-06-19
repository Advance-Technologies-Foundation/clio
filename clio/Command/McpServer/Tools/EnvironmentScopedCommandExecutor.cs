using System;
using System.Collections.Generic;
using System.Reflection;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Generalizes <c>BaseTool&lt;T&gt;</c>'s env-scoped resolution and locked-execute path so a command
/// whose options <see cref="Type"/> is known only at runtime can be resolved from the per-call
/// environment-scoped container and executed with the same log-capture semantics as the flat tools.
/// </summary>
public interface IEnvironmentScopedCommandExecutor {
	/// <summary>
	/// Chooses the correct <see cref="IToolCommandResolver"/> entry point for the given options
	/// instance (preserving the env-less special cases <c>BaseTool</c> uses) and resolves a service.
	/// </summary>
	/// <typeparam name="TService">The service to resolve from the env-scoped container.</typeparam>
	/// <param name="options">An <see cref="EnvironmentOptions"/>-derived options instance.</param>
	/// <returns>The resolved service.</returns>
	TService ResolveFromCallContainer<TService>(EnvironmentOptions options);

	/// <summary>
	/// Resolves the <c>Command&lt;TOptions&gt;</c> for the runtime type of <paramref name="options"/>
	/// from the env-scoped container and executes it under the shared MCP lock, capturing and
	/// forwarding the execution log exactly as the flat tools do.
	/// </summary>
	/// <param name="options">An <see cref="EnvironmentOptions"/>-derived, parser-bound options instance.</param>
	/// <returns>The uniform <see cref="CommandExecutionResult"/> envelope.</returns>
	CommandExecutionResult ResolveAndExecute(EnvironmentOptions options);
}

/// <summary>
/// Shared implementation of the env-scoped resolution + locked-execute path. This is the single
/// place the env-less special-case decision lives, consumed by both <c>BaseTool&lt;T&gt;</c> and the
/// generic <c>clio-run</c> executor so the resolution logic is not duplicated.
/// </summary>
public sealed class EnvironmentScopedCommandExecutor(
	ILogger logger,
	IToolCommandResolver commandResolver,
	IDbOperationLogContextAccessor dbOperationLogContextAccessor = null) : IEnvironmentScopedCommandExecutor {

	private static readonly object CommandExecutionLock = McpToolExecutionLock.SyncRoot;

	/// <inheritdoc />
	public TService ResolveFromCallContainer<TService>(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (commandResolver is null) {
			throw new InvalidOperationException(
				"Environment-based command resolution is not available: no command resolver was provided.");
		}
		return UsesEnvironmentlessResolution(options)
			? commandResolver.ResolveWithoutEnvironment<TService>(options)
			: commandResolver.Resolve<TService>(options);
	}

	/// <summary>
	/// Mirrors the env-less special cases of <c>BaseTool.ResolveFromCallContainer</c>: a handful of
	/// options types resolve against a default (environment-less) container when no environment/uri
	/// is supplied. Keeping this decision here is what guarantees zero behavioral drift between the
	/// flat tools and the generic executor.
	/// </summary>
	internal static bool UsesEnvironmentlessResolution(EnvironmentOptions options) =>
		options switch {
			CreateTestProjectOptions o when string.IsNullOrWhiteSpace(o.Environment) && string.IsNullOrWhiteSpace(o.Uri) => true,
			AddPackageOptions o when string.IsNullOrWhiteSpace(o.Environment) && string.IsNullOrWhiteSpace(o.Uri) => true,
			CreateWorkspaceCommandOptions o when o.Empty && string.IsNullOrWhiteSpace(o.Environment) && string.IsNullOrWhiteSpace(o.Uri) => true,
			CreateUiProjectOptions o when string.IsNullOrWhiteSpace(o.Environment) && string.IsNullOrWhiteSpace(o.Uri) => true,
			_ => false
		};

	/// <inheritdoc />
	public CommandExecutionResult ResolveAndExecute(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		object command;
		try {
			command = ResolveCommandForRuntimeType(options);
		}
		catch (Exception e) {
			return CommandExecutionResult.FromException(e);
		}
		return ExecuteLocked(command, options);
	}

	// Resolves Command<TOptions> where TOptions is the runtime type of the options instance. The
	// IToolCommandResolver methods are generic over the command type, so the closed Command<TOptions>
	// type is constructed and the env-aware resolution entry point is invoked via reflection.
	private object ResolveCommandForRuntimeType(EnvironmentOptions options) {
		Type optionsType = options.GetType();
		Type commandType = typeof(Command<>).MakeGenericType(optionsType);
		string resolveMethodName = UsesEnvironmentlessResolution(options)
			? nameof(IToolCommandResolver.ResolveWithoutEnvironment)
			: nameof(IToolCommandResolver.Resolve);
		MethodInfo resolveMethod = typeof(IToolCommandResolver)
			.GetMethod(resolveMethodName)!
			.MakeGenericMethod(commandType);
		return resolveMethod.Invoke(commandResolver, [options]);
	}

	// Faithful copy of BaseTool.InternalExecute(Command<T>, T): runs Execute under the shared lock,
	// flushes the captured log snapshot, and forwards notifications OUTSIDE the lock. Kept in lockstep
	// so clio-run produces the same envelope shape as every flat tool.
	private CommandExecutionResult ExecuteLocked(object command, EnvironmentOptions options) {
		string correlationId = Guid.NewGuid().ToString("N")[..12];
		CommandExecutionResult executionResult;
		IReadOnlyList<LogMessage> messagesToForward;
		lock (CommandExecutionLock) {
			dbOperationLogContextAccessor?.ClearLastCompletedPath();
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				int exitCode = InvokeExecute(command, options);
				IReadOnlyList<LogMessage> flushedMessages = logger.FlushAndSnapshotMessages(clearMessages: true);
				messagesToForward = flushedMessages;
				executionResult = new CommandExecutionResult(
					exitCode,
					[.. flushedMessages],
					dbOperationLogContextAccessor?.LastCompletedPath,
					CorrelationId: correlationId);
			}
			catch (TargetInvocationException tie) when (tie.InnerException is not null) {
				List<LogMessage> priorLogs = [.. logger.FlushAndSnapshotMessages(clearMessages: true)];
				messagesToForward = priorLogs;
				executionResult = CommandExecutionResult.FromException(tie.InnerException, priorLogs, correlationId);
			}
			catch (Exception e) {
				List<LogMessage> priorLogs = [.. logger.FlushAndSnapshotMessages(clearMessages: true)];
				messagesToForward = priorLogs;
				executionResult = CommandExecutionResult.FromException(e, priorLogs, correlationId);
			}
			finally {
				logger.PreserveMessages = previousPreserveMessages;
			}
		}
		McpLogNotifier.ForwardMessages(messagesToForward, correlationId);
		return executionResult;
	}

	private static int InvokeExecute(object command, EnvironmentOptions options) {
		Type commandType = typeof(Command<>).MakeGenericType(options.GetType());
		MethodInfo execute = commandType.GetMethod(nameof(Command<EnvironmentOptions>.Execute))!;
		return (int)execute.Invoke(command, [options])!;
	}
}
