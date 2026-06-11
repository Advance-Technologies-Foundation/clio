using System;
using System.Collections.Generic;
using System.Linq;
using Clio.UserEnvironment;

namespace Clio.Common;

/// <summary>
/// Builds a single, actionable "environment not found" message shared by every clio
/// environment-resolution path — the MCP <see cref="Clio.Command.McpServer.Tools.ToolCommandResolver"/>
/// and the application-family read/write services (<c>list-apps</c>, <c>get-app-info</c>,
/// <c>list-app-sections</c>, <c>create-app</c>, …).
/// </summary>
/// <remarks>
/// Before this helper the two paths diverged: the resolver listed the registered environments
/// while the application services threw the generic "Check your clio configuration.". Centralising
/// the text removes that divergence and lets every caller end the error with a copy-pasteable
/// <c>reg-web-app</c> command so an AI agent or developer can fix a missing registration without
/// guessing the flag names. See ENG-91275.
/// </remarks>
public static class EnvironmentNotFoundError {
	/// <summary>
	/// Composes the actionable message for a missing environment registration.
	/// </summary>
	/// <param name="missingEnvironmentName">The environment key that could not be resolved.</param>
	/// <param name="availableEnvironmentNames">
	/// The currently registered environment names, or <c>null</c> when they cannot be enumerated.
	/// </param>
	/// <returns>A human- and agent-readable message that ends with a copy-pasteable fix.</returns>
	public static string Build(string? missingEnvironmentName, IEnumerable<string>? availableEnvironmentNames) {
		string name = string.IsNullOrWhiteSpace(missingEnvironmentName)
			? "<unknown>"
			: missingEnvironmentName.Trim();
		string availableHint = BuildAvailableHint(availableEnvironmentNames);
		string fix = $"To register it, run: clio reg-web-app {name} -u <url> -l <login> -p <password>";
		return $"Environment with key '{name}' not found.{availableHint} {fix}";
	}

	/// <summary>
	/// Composes the actionable message, reading the registered environment names from the supplied
	/// settings repository. Failures while enumerating environments degrade gracefully to the
	/// no-environments hint instead of masking the original "not found" error.
	/// </summary>
	/// <param name="missingEnvironmentName">The environment key that could not be resolved.</param>
	/// <param name="settingsRepository">The settings repository used to list registered environments.</param>
	/// <returns>A human- and agent-readable message that ends with a copy-pasteable fix.</returns>
	public static string Build(string? missingEnvironmentName, ISettingsRepository? settingsRepository) {
		IEnumerable<string>? names = null;
		try {
			names = settingsRepository?.GetAllEnvironments()?.Keys;
		} catch {
			// Enumerating environments is best-effort; never let it hide the not-found error.
			names = null;
		}
		return Build(missingEnvironmentName, names);
	}

	private static string BuildAvailableHint(IEnumerable<string>? availableEnvironmentNames) {
		List<string> names = availableEnvironmentNames?
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name.Trim())
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList() ?? [];
		return names.Count == 0
			? " No environments are registered."
			: $" Available environments: {string.Join(", ", names)} (use `list-environments` to inspect them).";
	}
}
