using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Clio.Common.McpWorker;

/// <summary>
/// How long the worker that serves a call is expected to live. Only two states exist here on purpose:
/// the routing metadata's <c>Unspecified</c> / <c>NotApplicable</c> values describe a call that never
/// reaches a worker at all, so they cannot reach environment composition.
/// </summary>
public enum McpWorkerLifetime {

	/// <summary>The worker is created for this call and reaped when it answers.</summary>
	PerCall,

	/// <summary>The worker outlives the response so a status poller of the same family can reach it.</summary>
	Sticky
}

/// <summary>
/// The contract between the MCP host and a worker child: the mode flag, the frozen feature generation
/// carried across the process boundary, and the deadline variables the parent composes into the child's
/// environment.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mode travels in argv, the payload travels in the environment.</b> The split is deliberate. An
/// environment variable is inherited by grandchildren, so an environment-selected mode would silently turn
/// any clio a worker spawns (through <c>clio-run</c>) into a worker as well — the inheritance leak ADR rule 11
/// forbids for the read deadline. Argv does not inherit. The frozen payload, conversely, is inert without the
/// mode: <see cref="ReadFrozenFeatures"/> is consulted only while <see cref="IsWorkerProcess"/> is set.
/// </para>
/// <para>
/// <b>Why a frozen generation at all.</b> The enabled-tool set is resolved once in the parent and passed down.
/// A child that re-read <c>appsettings.json</c> could disagree with the parent mid-session — a tool present in
/// the parent's <c>tools/list</c> but absent in the worker, or the reverse — and the disagreement would surface
/// as an unroutable call rather than as an error. The whole feature map is frozen rather than a hand-picked
/// subset: the resident tool set is compile-time static, so the flag values are the entire runtime variable,
/// and a worker also dispatches CLI verbs through <c>clio-run</c>, which means a CLI-only flag such as
/// <c>ring</c> is in scope even though it gates no MCP tool.
/// </para>
/// <para>
/// <b>No handshake is possible.</b> MCP primitive registration happens inside the container build, before the
/// stdio transport is attached and long before the child can receive a message, so the tool surface is already
/// fixed when the first byte arrives. The generation therefore has to be present at process start, which is
/// what an environment variable is for.
/// </para>
/// </remarks>
public static class McpWorkerEnvironment {

	/// <summary>The bare kebab-case long name of the hidden mode option on the <c>mcp-server</c> verb.</summary>
	public const string WorkerOptionLongName = "worker";

	/// <summary>The mode flag as it appears in the child's command line.</summary>
	public const string WorkerFlag = "--" + WorkerOptionLongName;

	/// <summary>
	/// Name of the variable carrying the parent's frozen feature generation, formatted by
	/// <see cref="Format"/> and read back by <see cref="Parse"/>.
	/// </summary>
	public const string FrozenFeaturesVariableName = "CLIO_MCP_WORKER_FROZEN_FEATURES";

	/// <summary>
	/// Name of the read-response deadline override. A worker must never inherit it: the parent bounds an
	/// ordinary worker call by KILLING the child, and a second in-child deadline would abandon work while
	/// keeping the per-tenant monitor — the wedge this feature exists to remove.
	/// </summary>
	public const string ReadDeadlineVariableName = "CLIO_MCP_READ_DEADLINE_SECONDS";

	/// <summary>
	/// Name of the write-path response deadline override. A STICKY worker keeps it verbatim, because its
	/// in-progress envelope is what returns the call; stripping it turned a 25 s backend call into a 77 s
	/// block in the prototype.
	/// </summary>
	public const string ResponseDeadlineVariableName = "CLIO_MCP_RESPONSE_DEADLINE_SECONDS";

	private const char PairSeparator = ';';
	private const char NameValueSeparator = '=';

	/// <summary>
	/// Gets or sets a value indicating whether THIS process is an MCP worker child. Set once from the
	/// command line during startup (see <c>Program.IsMcpWorkerMode</c>) and read by the composition root,
	/// which must decide between the live and the frozen feature-toggle service before the parser runs.
	/// </summary>
	public static bool IsWorkerProcess { get; set; }

	/// <summary>
	/// Determines whether an argument vector selects worker mode.
	/// </summary>
	/// <param name="args">The command-line arguments, already stripped of global switches.</param>
	/// <returns><see langword="true"/> when <see cref="WorkerFlag"/> is present.</returns>
	public static bool IsWorkerModeArgv(IReadOnlyList<string> args) {
		if (args is null) {
			return false;
		}
		for (int index = 0; index < args.Count; index++) {
			if (string.Equals(args[index], WorkerFlag, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Formats a feature map for <see cref="FrozenFeaturesVariableName"/> as
	/// <c>name=1;other-name=0</c>, ordered by name so the payload is stable and diffable.
	/// </summary>
	/// <param name="features">The parent's whole feature map (typically <c>ISettingsRepository.GetFeatures()</c>).</param>
	/// <returns>The formatted payload; an empty string when there are no features.</returns>
	/// <exception cref="ArgumentException">A feature name contains a separator character.</exception>
	public static string Format(IReadOnlyDictionary<string, bool> features) {
		if (features is null || features.Count == 0) {
			return string.Empty;
		}
		List<string> pairs = [];
		foreach (KeyValuePair<string, bool> feature in features.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
			if (string.IsNullOrWhiteSpace(feature.Key)) {
				continue;
			}
			if (feature.Key.IndexOf(PairSeparator) >= 0 || feature.Key.IndexOf(NameValueSeparator) >= 0) {
				throw new ArgumentException(
					$"Feature name '{feature.Key}' cannot be carried to a worker: it contains "
					+ $"'{PairSeparator}' or '{NameValueSeparator}'.",
					nameof(features));
			}
			pairs.Add(string.Create(
				CultureInfo.InvariantCulture,
				$"{feature.Key}{NameValueSeparator}{(feature.Value ? 1 : 0)}"));
		}
		return string.Join(PairSeparator, pairs);
	}

	/// <summary>
	/// Parses a payload produced by <see cref="Format"/>.
	/// </summary>
	/// <remarks>
	/// The map is <see cref="StringComparer.OrdinalIgnoreCase"/> because the settings repository compares
	/// feature keys case-insensitively; an ordinal map here would let a case-differing name read as absent in
	/// the worker while the parent read it as enabled. Unparseable segments are skipped rather than throwing:
	/// a worker that refused to start on a malformed payload would fail the call with a startup crash instead
	/// of the ordinary "feature is off" behaviour, and the payload is produced by clio itself.
	/// </remarks>
	/// <param name="rawValue">The raw variable value; may be <see langword="null"/> or empty.</param>
	/// <returns>The frozen feature map; empty when nothing could be parsed.</returns>
	public static IReadOnlyDictionary<string, bool> Parse(string rawValue) {
		Dictionary<string, bool> features = new(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(rawValue)) {
			return features;
		}
		foreach (string segment in rawValue.Split(
				PairSeparator,
				StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
			int separator = segment.IndexOf(NameValueSeparator);
			if (separator <= 0) {
				continue;
			}
			string name = segment[..separator].Trim();
			string value = segment[(separator + 1)..].Trim();
			if (name.Length == 0 || !TryParseFlag(value, out bool enabled)) {
				continue;
			}
			features[name] = enabled;
		}
		return features;
	}

	/// <summary>
	/// Reads the frozen feature generation this worker was started with.
	/// </summary>
	/// <remarks>
	/// An absent or empty variable yields an EMPTY map, which reads as "every gated feature is off". That is
	/// fail-closed on purpose: a worker must never consult <c>appsettings.json</c> for feature state, so
	/// falling back to the live repository would reintroduce exactly the parent/child disagreement the frozen
	/// generation exists to prevent.
	/// </remarks>
	/// <returns>The frozen feature map.</returns>
	public static IReadOnlyDictionary<string, bool> ReadFrozenFeatures() =>
		Parse(Environment.GetEnvironmentVariable(FrozenFeaturesVariableName));

	/// <summary>
	/// Composes the variables the parent adds to a worker's environment on top of the supervisor's
	/// inherited-variable allowlist.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The supervisor clears the inherited environment and re-applies a small allowlist that carries neither
	/// deadline variable, so "copy the parent environment MINUS the read deadline" needs no subtraction here —
	/// what this method returns is the whole delta. The asymmetry is the point: a sticky worker gets the
	/// response deadline verbatim, an ordinary one gets no deadline override at all.
	/// </para>
	/// <para>
	/// Deadlines cannot be applied by mutating the child's process state later: both defaults are
	/// <c>static readonly</c> and captured at TYPE LOAD, so any post-load mutation is inert. Composition at
	/// spawn is the only seam that works.
	/// </para>
	/// </remarks>
	/// <param name="frozenFeatures">The parent's whole feature map, frozen into this worker.</param>
	/// <param name="lifetime">Whether the worker is sticky or per-call.</param>
	/// <param name="parentEnvironmentReader">
	/// Reads a variable from the parent process; defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>.
	/// Injected so the composition rule is testable without mutating process-wide state.
	/// </param>
	/// <returns>The variables to add to the worker's environment.</returns>
	public static IReadOnlyDictionary<string, string> ComposeChildEnvironment(
		IReadOnlyDictionary<string, bool> frozenFeatures,
		McpWorkerLifetime lifetime,
		Func<string, string> parentEnvironmentReader = null) {
		Func<string, string> readParent = parentEnvironmentReader ?? Environment.GetEnvironmentVariable;
		Dictionary<string, string> environment = new(StringComparer.Ordinal) {
			[FrozenFeaturesVariableName] = Format(frozenFeatures)
		};
		if (lifetime == McpWorkerLifetime.Sticky) {
			string responseDeadline = readParent(ResponseDeadlineVariableName);
			if (!string.IsNullOrWhiteSpace(responseDeadline)) {
				environment[ResponseDeadlineVariableName] = responseDeadline;
			}
		}
		return environment;
	}

	private static bool TryParseFlag(string value, out bool enabled) {
		if (value.Equals("1", StringComparison.Ordinal)
			|| value.Equals("true", StringComparison.OrdinalIgnoreCase)) {
			enabled = true;
			return true;
		}
		if (value.Equals("0", StringComparison.Ordinal)
			|| value.Equals("false", StringComparison.OrdinalIgnoreCase)) {
			enabled = false;
			return true;
		}
		enabled = false;
		return false;
	}
}
