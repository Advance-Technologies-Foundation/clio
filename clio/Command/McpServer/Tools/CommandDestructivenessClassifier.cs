using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Classifies whether a clio command is destructive, so the <c>clio-run</c> / <c>clio-run-destructive</c>
/// split can route each command to the correct surface and a host that auto-approves the safe surface
/// never silently runs a destructive command.
/// </summary>
public interface ICommandDestructivenessClassifier {
	/// <summary>
	/// Determines whether <paramref name="command"/> is destructive. Unknown commands fail CLOSED
	/// (treated as destructive) so they are refused on the safe <c>clio-run</c> surface.
	/// </summary>
	/// <param name="command">The verb name or alias.</param>
	/// <returns><c>true</c> when the command is destructive or unknown; otherwise <c>false</c>.</returns>
	bool IsDestructive(string command);
}

/// <summary>
/// Derives destructiveness from a curated destructive-verb set plus a small set of destructive name
/// prefixes (delete-, remove-, uninstall-, unreg-, deactivate-, drop-). Mirrors the
/// <c>[McpServerTool(Destructive = true)]</c> intent of the flat tools for their verbs while failing
/// CLOSED on anything it cannot positively classify as safe.
/// </summary>
/// <remarks>
/// This is the minimal gate for the spike (Story 4); Story 8 hardens it. Comparison is
/// case-insensitive on the verb token.
/// </remarks>
public sealed class CommandDestructivenessClassifier : ICommandDestructivenessClassifier {

	// Verbs whose flat MCP tool carries Destructive = true but whose name does not start with a
	// destructive prefix, so they must be enumerated explicitly.
	private static readonly HashSet<string> DestructiveVerbs = new(StringComparer.OrdinalIgnoreCase) {
		"compile-configuration", "cc", "compile-remote",
		"compile-package", "comp-pkg",
		"compile",
		"clear-redis-db",
		"clear-browser-session",
		"restore-db",
		"restore-web-farm-db",
		"set-fsm-config",
		"turn-fsm",
		"turn-farm-mode",
		"create-app", "create-app-section", "update-app-section",
		"add-item",
		"push-pkg", "push-package",
		"deploy-application", "deploy-app",
		"download-configuration",
		"restart-web-app", "restart",
		"set-syssetting", "set-sys-setting",
		"add-schema",
		"create-entity-schema", "update-entity-schema", "modify-entity-schema-column",
		"create-source-code-schema", "update-source-code-schema",
		"create-client-unit-schema", "update-client-unit-schema",
		"create-sql-schema", "update-sql-schema", "install-sql-schema",
		"create-page", "update-page",
		"create-business-rule", "create-page-business-rule", "create-entity-business-rule",
		"odata-create", "odata-update", "odata-delete",
		"set-application-version", "set-application-icon",
		"install-gate", "set-dev-mode",
		"package-hotfix",
		"create-ui-project"
	};

	private static readonly string[] DestructivePrefixes = [
		"delete-", "remove-", "uninstall-", "unreg", "deactivate", "drop-", "clear-"
	];

	/// <inheritdoc />
	public bool IsDestructive(string command) {
		if (string.IsNullOrWhiteSpace(command)) {
			// No command to classify safely → fail closed.
			return true;
		}
		string verb = command.Trim();
		if (DestructiveVerbs.Contains(verb)) {
			return true;
		}
		return DestructivePrefixes.Any(prefix =>
			verb.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
	}
}
