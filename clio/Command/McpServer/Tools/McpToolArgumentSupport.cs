using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared helpers for MCP tools that bind a single <c>args</c> record with kebab-case fields.
/// Centralizes two pieces of logic several tools used to copy verbatim — legacy-alias rejection
/// over the <c>[JsonExtensionData]</c> overflow bag, and the edit-distance ranking used for
/// "did you mean" suggestions — so the behavior (and the SonarCloud duplication budget) stays in
/// one place. Each caller keeps its own canonical alias map and wording via the parameters.
/// </summary>
internal static class McpToolArgumentSupport {
	/// <summary>
	/// True when the SDK binds this parameter from the request's <c>arguments</c> object. Parameters the
	/// SDK injects from the request context (<see cref="RequestContext{T}"/>, <see cref="CancellationToken"/>,
	/// <see cref="IServiceProvider"/>, <c>McpServer</c>, and anything else the MCP SDK owns) are not bound
	/// from caller-supplied arguments, so they are excluded when deciding how many user-supplied
	/// parameters a tool exposes.
	/// </summary>
	/// <remarks>
	/// ENG-95885: this predicate is the SINGLE definition shared by <c>ClioRunTool</c>'s argument mapping
	/// and <c>McpToolErrorFilter</c>'s flat-argument normalizer. Both must agree on "exactly one bindable
	/// non-framework composite parameter" — if they drifted apart, the normalizer could rewrite a
	/// <c>clio-run</c> payload that <c>ClioRunExecutor.RecoverWrappedCall</c> also claims ownership of, and
	/// two mechanisms would fight over the same arguments object.
	/// </remarks>
	public static bool IsBindableToolParameter(ParameterInfo parameter) {
		ArgumentNullException.ThrowIfNull(parameter);
		return !IsFrameworkOwnedType(parameter.ParameterType);
	}

	/// <summary>
	/// True when <paramref name="type"/> belongs to the hosting framework rather than to a tool's own
	/// caller-supplied argument contract. Three independent rules, none of them name-based:
	/// <list type="number">
	/// <item><description>the two BCL context types the SDK injects (<see cref="CancellationToken"/>,
	/// <see cref="IServiceProvider"/>);</description></item>
	/// <item><description>anything declared in the MCP SDK's own assembly — <c>McpServer</c>,
	/// <c>IMcpServer</c>, <see cref="RequestContext{T}"/>, <c>ProgressToken</c> and every future
	/// SDK-injected type, whatever namespace the SDK puts it in;</description></item>
	/// <item><description>anything ASSIGNABLE TO <c>McpServer</c>, which catches a host-defined subclass
	/// declared OUTSIDE the SDK assembly — the one framework shape rule 2 cannot see.</description></item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// The exclusion is keyed on the SDK ASSEMBLY, never on a namespace-name prefix. A prefix match
	/// (<c>type.Namespace.StartsWith("ModelContextProtocol")</c>) silently swallowed any unrelated type
	/// whose namespace merely began with those characters, and silently missed an <c>McpServer</c>
	/// subclass declared elsewhere — both of which would move the "exactly one bindable parameter" count
	/// and hand the normalizer a payload it must not rewrite. Assembly identity plus
	/// <see cref="Type.IsAssignableFrom"/> cannot drift that way at the next SDK upgrade.
	/// Pinned by <c>McpToolArgumentSupportTests</c>.
	/// </remarks>
	public static bool IsFrameworkOwnedType(Type type) {
		ArgumentNullException.ThrowIfNull(type);
		if (type == typeof(CancellationToken) || type == typeof(IServiceProvider)) {
			return true;
		}
		if (type.Assembly == McpSdkAssembly) {
			return true;
		}
		return typeof(ModelContextProtocol.Server.McpServer).IsAssignableFrom(type);
	}

	/// <summary>
	/// The assembly that owns every SDK-injected parameter type. Resolved from a type the tool layer
	/// actually declares, so it follows the SDK package rather than a hardcoded assembly name.
	/// </summary>
	private static readonly Assembly McpSdkAssembly =
		typeof(ModelContextProtocol.Server.McpServer).Assembly;

	/// <summary>
	/// True for a composite ("args record") parameter — a non-string reference type the tool expects to
	/// receive as ONE bound argument object. Scalars (string, bool, numbers, enums, and any other value
	/// type) are bound by name from the arguments object instead, so a single scalar parameter is not a
	/// composite wrapper.
	/// </summary>
	public static bool IsCompositeArgsParameter(Type type) {
		ArgumentNullException.ThrowIfNull(type);
		Type underlying = Nullable.GetUnderlyingType(type) ?? type;
		return underlying != typeof(string) && !underlying.IsValueType;
	}

	/// <summary>
	/// The shared trigger predicate: true when <paramref name="method"/> exposes EXACTLY ONE bindable
	/// non-framework parameter and that parameter is composite. This is the only shape for which a flat
	/// argument payload is unambiguous — a multi-parameter tool (e.g. <c>clio-run</c>'s
	/// <c>command</c> + <c>args</c>) or a single-scalar tool binds top-level keys by parameter name, so
	/// its payload must never be rewritten.
	/// </summary>
	/// <param name="method">The tool implementation method.</param>
	/// <param name="parameter">The single composite parameter when the predicate holds; otherwise <c>null</c>.</param>
	public static bool TryGetSingleCompositeParameter(
		MethodInfo method,
		[NotNullWhen(true)] out ParameterInfo? parameter) {
		ArgumentNullException.ThrowIfNull(method);
		parameter = null;
		ParameterInfo[] bindable = method.GetParameters().Where(IsBindableToolParameter).ToArray();
		if (bindable.Length != 1 || !IsCompositeArgsParameter(bindable[0].ParameterType)) {
			return false;
		}
		parameter = bindable[0];
		return true;
	}

	/// <summary>
	/// The camelCase / snake_case mis-spellings of <c>environment-name</c> an LLM tends to emit, each mapped to
	/// the canonical kebab-case name so a wrong spelling is rejected with a rename hint instead of silently
	/// binding to nothing. Shared by every environment-scoped tool so the pair is defined once; a tool with extra
	/// fields seeds its own map from this and adds them.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> EnvironmentNameAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["environmentName"] = "environment-name",
			["environment_name"] = "environment-name",
			// ENG-95885: the bare 'environment' spelling was the one missing member of this set — it is
			// what an agent writes when it is thinking about the CLI's -e/--environment flag. Like every
			// other entry it is REJECTION-ONLY: it produces a rename hint, never a silent binding, so the
			// accepted field set stays exactly the canonical kebab-case one.
			["environment"] = "environment-name"
		};

	/// <summary>
	/// Builds a single actionable rename hint from the fields an MCP arg record could not bind
	/// (captured in its <c>[JsonExtensionData]</c> bag). Known camelCase/snake_case spellings are
	/// reported as <c>'alias' -&gt; 'canonical'</c> renames; everything else is listed as unknown
	/// with the caller-supplied valid-field hint. Returns <c>null</c> when there is nothing to
	/// flag (no overflow), so a clean call passes straight through.
	/// </summary>
	/// <param name="extensionData">The arg record's overflow bag (unbound JSON fields).</param>
	/// <param name="aliases">Canonical map of known mis-spelling -&gt; canonical kebab-case name.</param>
	/// <param name="renameSuffix">Trailing text appended after the rename list (e.g. <c>"."</c> or a type reminder); use <c>""</c> for none.</param>
	/// <param name="unknownHint">Sentence appended after the unknown-args list, e.g. <c>"Valid: a, b, c."</c>.</param>
	public static string? BuildLegacyAliasError(
		IReadOnlyDictionary<string, JsonElement>? extensionData,
		IReadOnlyDictionary<string, string> aliases,
		string renameSuffix,
		string unknownHint) {
		if (extensionData is null || extensionData.Count == 0) {
			return null;
		}
		List<string> mapped = [];
		List<string> unknown = [];
		foreach (string key in extensionData.Keys) {
			if (aliases.TryGetValue(key, out string? canonical)) {
				mapped.Add($"'{key}' -> '{canonical}'");
			} else {
				unknown.Add($"'{key}'");
			}
		}
		List<string> parts = [];
		if (mapped.Count > 0) {
			parts.Add("Rename: " + string.Join(", ", mapped) + renameSuffix);
		}
		if (unknown.Count > 0) {
			parts.Add("Unknown args: " + string.Join(", ", unknown) + ". " + unknownHint);
		}
		return parts.Count > 0 ? string.Join(" ", parts) : null;
	}

	/// <summary>
	/// Case-insensitive Levenshtein edit distance between two identifiers. Drives the "closest
	/// match" ranking for unknown tool names and unknown component types. Equal strings score 0.
	/// </summary>
	public static int LevenshteinDistance(string? source, string? target) {
		string left = (source ?? string.Empty).ToLowerInvariant();
		string right = (target ?? string.Empty).ToLowerInvariant();
		if (left.Length == 0) {
			return right.Length;
		}
		if (right.Length == 0) {
			return left.Length;
		}
		int[,] matrix = new int[left.Length + 1, right.Length + 1];
		for (int i = 0; i <= left.Length; i++) {
			matrix[i, 0] = i;
		}
		for (int j = 0; j <= right.Length; j++) {
			matrix[0, j] = j;
		}
		for (int i = 1; i <= left.Length; i++) {
			for (int j = 1; j <= right.Length; j++) {
				int cost = left[i - 1] == right[j - 1] ? 0 : 1;
				matrix[i, j] = Math.Min(
					Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
					matrix[i - 1, j - 1] + cost);
			}
		}
		return matrix[left.Length, right.Length];
	}
}
