using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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
/// Caches <see cref="IServiceProvider"/> per environment key so that a single
/// <see cref="IApplicationClient"/> (and its authenticated HTTP session) is reused
/// across successive tool calls targeting the same Creatio instance.
/// </summary>
public class ToolCommandResolver(
	ISettingsRepository settingsRepository,
	ISettingsBootstrapService settingsBootstrapService) : IToolCommandResolver {

	private static readonly ConcurrentDictionary<string, IServiceProvider> ContainerCache = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Resolves a command against an explicit environment or URI-based target.
	/// </summary>
	/// <typeparam name="TCommand">The command type to resolve.</typeparam>
	/// <param name="options">Environment options that identify the execution target.</param>
	/// <returns>A command instance configured for the requested target.</returns>
	public TCommand Resolve<TCommand>(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		SettingsBootstrapReport bootstrapReport = settingsBootstrapService.GetReport();
		EnvironmentSettings settings;
		if (!string.IsNullOrWhiteSpace(options.Environment)) {
			if (!bootstrapReport.CanExecuteEnvTools) {
				throw new InvalidOperationException(
					$"clio settings bootstrap is broken. Repair {bootstrapReport.SettingsFilePath} or use explicit uri/login/password.");
			}
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
				if (!bootstrapReport.CanExecuteEnvTools) {
					throw new InvalidOperationException(
						$"clio settings bootstrap is broken. Repair {bootstrapReport.SettingsFilePath} or provide explicit uri/login/password.");
				}
				throw new InvalidOperationException(
					"Either a configured environment name or an explicit URI is required for MCP command execution.");
			}
		}
		string cacheKey = BuildCacheKey(options, settings);
		IServiceProvider container = ContainerCache.GetOrAdd(cacheKey,
			_ => new BindingsModule().Register(settings));
		return container.GetRequiredService<TCommand>();
	}

	public TCommand ResolveWithoutEnvironment<TCommand>(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		EnvironmentSettings settings = settingsRepository.FindEnvironment(options.Environment)
			?? new EnvironmentSettings {
				Login = "default"
			};
		settings = settings.Fill(options);
		IServiceProvider container = new BindingsModule().Register(settings);
		return container.GetRequiredService<TCommand>();
	}

	private static string BuildCacheKey(EnvironmentOptions options, EnvironmentSettings settings) {
		string identity = options.Environment
			?? settings.Uri
			?? "default";
		string credentials = string.Concat(
			settings.Login ?? string.Empty, "|",
			settings.Password ?? string.Empty, "|",
			settings.ClientId ?? string.Empty, "|",
			settings.IsNetCore.ToString());
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(credentials));
		return $"{identity}:{Convert.ToHexString(hash)[..16]}";
	}
}
