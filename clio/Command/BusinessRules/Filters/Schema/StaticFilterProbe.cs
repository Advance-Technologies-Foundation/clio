using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.BusinessRules.Filters.Schema;

internal sealed record StaticFilterProbeResult(int? MatchCount, string? Warning);

/// <summary>
/// Runs the ESQ envelope built for an apply-static-filter action as a one-row COUNT on the target lookup's
/// reference schema, before the rule is saved.
/// </summary>
internal interface IStaticFilterProbe {
	/// <exception cref="ArgumentException">The server rejected the filter itself; the rule must not be saved.</exception>
	StaticFilterProbeResult Probe(string esqEnvelope, string rootSchemaName, string targetAttribute);
}

internal sealed class StaticFilterProbe(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	Func<TimeSpan>? elapsed = null)
	: IStaticFilterProbe {

	private const string CountAlias = "ProbeCount";
	private const string SelectQueryFailurePrefix = "SelectQuery failed: ";
	// A COUNT over a large lookup with sub-filters can be slow; past this the rule is saved unverified rather
	// than holding the whole MCP call.
	private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
	// One probe instance serves one create or update call, so this caps all its probes together: a batch of slow
	// filters must still answer well inside the ~180 s an MCP client waits, or the client gives up while clio
	// saves the rules and a retry creates duplicates.
	private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan ShortestUsefulProbe = TimeSpan.FromSeconds(1);

	// Failures that say nothing about the filter: rights on the lookup, or a transient condition (a bounded
	// SelectQuery is not retried). Reporting those as "fix your filter" would send the agent round in circles.
	// Whole words only, and never for a column the server could not find: a rejection naming a column such as
	// UsrAccessLevel or TimeoutSec must still stop the rule instead of saving it unverified.
	private static readonly Regex NotAboutTheFilter = new(
		@"\b(permission|access|denied|security|deadlock|timeout|timed out|collection was modified)\b",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	private static readonly Regex NamesAMissingColumn = new(@"\bnot found\b",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private readonly Func<TimeSpan> _elapsed = elapsed ?? StartStopwatch();

	public StaticFilterProbeResult Probe(string esqEnvelope, string rootSchemaName, string targetAttribute) {
		TimeSpan remaining = CallBudget - _elapsed();
		if (remaining < ShortestUsefulProbe) {
			return Unverified(targetAttribute,
				$"the {CallBudget.TotalSeconds:0} s this call allows for checking filters is spent");
		}
		TimeSpan timeout = remaining < ProbeTimeout ? remaining : ProbeTimeout;
		JsonObject query = JsonSerializer.SerializeToNode(SelectQueryHelper.BuildCountQuery(rootSchemaName, CountAlias)).AsObject();
		query["filters"] = JsonNode.Parse(esqEnvelope);
		try {
			ProbeResponseDto response = SelectQueryHelper.ExecuteSelectQuery<ProbeResponseDto>(
				applicationClient, serviceUrlBuilder, query, (int)timeout.TotalMilliseconds);
			return new StaticFilterProbeResult(ReadCount(response), null);
		} catch (InvalidOperationException ex) when (ex.Message.StartsWith(SelectQueryFailurePrefix, StringComparison.Ordinal)) {
			string serverMessage = ex.Message[SelectQueryFailurePrefix.Length..];
			if (IsNotAboutTheFilter(serverMessage)) {
				return Unverified(targetAttribute, Untrusted(serverMessage));
			}
			throw new ArgumentException(
				$"apply-static-filter on '{targetAttribute}': the environment rejected the filter built for '{rootSchemaName}' " +
				$"({Untrusted(serverMessage)}). Nothing was saved.", ex);
		} catch (Exception ex) when (ex is not ArgumentException) {
			return Unverified(targetAttribute, Untrusted(ex.Message));
		}
	}

	// Classified on the raw server text: the fenced form can lose a marker to scrubbing or clamping.
	private static bool IsNotAboutTheFilter(string serverMessage) =>
		NotAboutTheFilter.IsMatch(serverMessage) && !NamesAMissingColumn.IsMatch(serverMessage);

	private static string Untrusted(string text) =>
		SensitiveErrorTextRedactor.RedactUntrustedOrNull(text) ?? "the environment gave no reason";

	private static Func<TimeSpan> StartStopwatch() {
		Stopwatch stopwatch = Stopwatch.StartNew();
		return () => stopwatch.Elapsed;
	}

	private static StaticFilterProbeResult Unverified(string targetAttribute, string reason) =>
		new(null, $"apply-static-filter on '{targetAttribute}' was saved without running its filter on the environment: {reason}");

	private static int? ReadCount(ProbeResponseDto response) {
		JsonElement? cell = response.Rows?.FirstOrDefault()?.GetValueOrDefault(CountAlias);
		return cell is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out int count) ? count : null;
	}

	private sealed class ProbeResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<Dictionary<string, JsonElement>>? Rows { get; set; }
	}
}
