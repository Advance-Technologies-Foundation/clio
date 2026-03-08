using System;
using Clio;
using Clio.UserEnvironment;
using Microsoft.Extensions.DependencyInjection;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Resolves environment-aware command instances for MCP tools.
/// </summary>
public interface IToolCommandResolver {
	/// <summary>
	/// Resolves a command instance for the provided environment options.
	/// </summary>
	/// <typeparam name="TCommand">The command type to resolve.</typeparam>
	/// <param name="options">Environment options that identify the execution target.</param>
	/// <returns>A command instance configured for the requested target.</returns>
	TCommand Resolve<TCommand>(EnvironmentOptions options);
	TCommand ResolveWithoutEnvironment<TCommand>(EnvironmentOptions options);
}

/// <summary>
/// Creates isolated command instances for MCP tool execution targets.
/// </summary>
public class ToolCommandResolver(ISettingsRepository settingsRepository) : IToolCommandResolver {
	/// <summary>
	/// Resolves a command against an explicit environment or URI-based target.
	/// </summary>
	/// <typeparam name="TCommand">The command type to resolve.</typeparam>
	/// <param name="options">Environment options that identify the execution target.</param>
	/// <returns>A command instance configured for the requested target.</returns>
	public TCommand Resolve<TCommand>(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		EnvironmentSettings settings;
		if (!string.IsNullOrWhiteSpace(options.Environment)) {
			if (!settingsRepository.IsEnvironmentExists(options.Environment)) {
				throw new InvalidOperationException(
					$"Environment with key '{options.Environment}' not found. Check your clio configuration.");
			}
			settings = settingsRepository.FindEnvironment(options.Environment)
				?? throw new InvalidOperationException(
					$"Environment with key '{options.Environment}' not found. Check your clio configuration.");
			settings = settings.Fill(options);
		} 
		else {
			settings = new EnvironmentSettings().Fill(options);
			if (string.IsNullOrWhiteSpace(settings.Uri)) {
				throw new InvalidOperationException(
					"Either a configured environment name or an explicit URI is required for MCP command execution.");
			}
		}
		IServiceProvider container = new BindingsModule().Register(settings);
		return container.GetRequiredService<TCommand>();
	}
	public TCommand ResolveWithoutEnvironment<TCommand>(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		EnvironmentSettings settings = new EnvironmentSettings().Fill(options);
		IServiceProvider container = new BindingsModule().Register(settings);
		return container.GetRequiredService<TCommand>();
	}
}
