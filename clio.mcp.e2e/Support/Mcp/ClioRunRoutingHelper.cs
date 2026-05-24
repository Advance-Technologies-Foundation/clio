using Clio.Command.McpServer.Tools;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// ENG-90312 Phase 2 — translates legacy "direct tool call" e2e helpers into the new
/// <c>clio-run</c> envelope. The non-read-only-tool registry is mined at runtime from
/// <see cref="ClioRunArgs"/>'s <see cref="JsonDerivedTypeAttribute"/> entries so that any
/// new derived command automatically routes through <c>clio-run</c> without an extra
/// table update here.
/// </summary>
internal static class ClioRunRoutingHelper {
	/// <summary>Set of MCP tool names that Phase 2 routed through <c>clio-run</c> (the 52 non-read-only commands).</summary>
	public static IReadOnlySet<string> NonReadOnlyCommands { get; } = LoadFromAttributes();

	/// <summary>Returns <c>true</c> when the legacy tool name must be wrapped in a <c>clio-run</c> envelope.</summary>
	public static bool IsRoutedThroughClioRun(string toolName) => NonReadOnlyCommands.Contains(toolName);

	/// <summary>
	/// Returns the <c>(toolName, arguments)</c> pair the e2e harness should pass to
	/// <c>McpClient.CallToolAsync</c>. For read-only tools the call shape is unchanged;
	/// for non-read-only tools it becomes <c>clio-run</c> with <c>{ command, ...originalArgs }</c>.
	/// </summary>
	public static (string ToolName, IReadOnlyDictionary<string, object?> Arguments) Resolve(
		string legacyToolName,
		IReadOnlyDictionary<string, object?> originalArgs) {
		if (!IsRoutedThroughClioRun(legacyToolName)) {
			return (legacyToolName, new Dictionary<string, object?> { ["args"] = originalArgs });
		}
		var merged = new Dictionary<string, object?>(originalArgs ?? new Dictionary<string, object?>()) {
			["command"] = legacyToolName
		};
		return (ClioRunTool.ToolName, new Dictionary<string, object?> { ["args"] = merged });
	}

	/// <summary>
	/// Returns the name the e2e harness should assert is present in <c>tools/list</c>. For
	/// non-read-only tools the dispatcher (<c>clio-run</c>) is what surfaces in the registry,
	/// not the legacy command name.
	/// </summary>
	public static string ResolveAdvertisedName(string legacyToolName) =>
		IsRoutedThroughClioRun(legacyToolName) ? ClioRunTool.ToolName : legacyToolName;

	private static HashSet<string> LoadFromAttributes() {
		return typeof(ClioRunArgs)
			.GetCustomAttributes<JsonDerivedTypeAttribute>()
			.Select(a => a.TypeDiscriminator?.ToString() ?? string.Empty)
			.Where(s => !string.IsNullOrEmpty(s))
			.ToHashSet();
	}
}
