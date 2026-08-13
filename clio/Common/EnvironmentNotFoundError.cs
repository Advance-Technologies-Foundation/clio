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
	public static string Build(string? missingEnvironmentName, IEnumerable<string>? availableEnvironmentNames,
		bool? isMcpContext = null) {
		string name = string.IsNullOrWhiteSpace(missingEnvironmentName)
			? "<unknown>"
			: missingEnvironmentName.Trim();
		string availableHint = BuildAvailableHint(availableEnvironmentNames);
		// The caller is the same command class on both surfaces, so the audience cannot be decided per
		// call site: it is a property of the process. Program.IsMcpServerMode is set once from the verb
		// (mcp-server / mcp-http) — the same ambient marker ConsoleLogger already branches on. The
		// parameter exists so a test can pin either text without mutating that process-wide state.
		bool isMcp = isMcpContext ?? Program.IsMcpServerMode;
		string fix = isMcp ? BuildMcpFix(name) : BuildCliFix(name);
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
	public static string Build(string? missingEnvironmentName, ISettingsRepository? settingsRepository,
		bool? isMcpContext = null) {
		IEnumerable<string>? names = null;
		try {
			names = settingsRepository?.GetAllEnvironments()?.Keys;
		} catch {
			// Enumerating environments is best-effort; never let it hide the not-found error.
			names = null;
		}
		return Build(missingEnvironmentName, names, isMcpContext);
	}

	private static string BuildCliFix(string name) =>
		$"To register it, run: clio reg-web-app {name} -u <url> -l <login> -p <password>";

	// Inside an MCP session the shell command is the wrong advice: it registers the environment in
	// appsettings.json of ANOTHER process, and this server keeps its own loaded copy — the caller would
	// then see the registration succeed and the very next tool call still fail. clio-run reaches
	// reg-web-app in THIS process, so the file and the running server move together.
	private static string BuildMcpFix(string name) =>
		"To register it from this MCP session, call the clio-run tool with "
		+ $"{{\"command\":\"reg-web-app\",\"args\":{{\"environment-name\":\"{name}\",\"uri\":\"<url>\","
		+ "\"login\":\"<login>\",\"password\":\"<password>\"}} — that writes appsettings.json and updates "
		+ "this running server in one step. "
		+ "This server holds the environment list it loaded from appsettings.json at start; "
		+ "`list-environments` and environment resolution re-read that file at call time, but tools bound "
		+ "at server start still answer from the loaded copy, so an edit made outside this process (Bash, "
		+ "or `clio reg-web-app` in another process) is not guaranteed to be seen before a restart.";

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
