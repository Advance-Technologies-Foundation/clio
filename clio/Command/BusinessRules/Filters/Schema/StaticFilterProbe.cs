using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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

internal sealed class StaticFilterProbe(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	: IStaticFilterProbe {

	private const string CountAlias = "ProbeCount";
	private const string SelectQueryFailurePrefix = "SelectQuery failed: ";
	// A COUNT over a large lookup with sub-filters can be slow; past this the rule is saved unverified rather
	// than holding the whole MCP call.
	private const int RequestTimeoutMs = 30_000;

	// Failures that say nothing about the filter: rights on the lookup, or a transient condition (a bounded
	// SelectQuery is not retried). Reporting those as "fix your filter" would send the agent round in circles.
	private static readonly string[] NotAboutTheFilterMarkers = [
		"permission", "access", "denied", "security", "deadlock", "timeout", "timed out", "collection was modified"
	];

	public StaticFilterProbeResult Probe(string esqEnvelope, string rootSchemaName, string targetAttribute) {
		JsonObject query = JsonSerializer.SerializeToNode(SelectQueryHelper.BuildCountQuery(rootSchemaName, CountAlias))!.AsObject();
		query["filters"] = JsonNode.Parse(esqEnvelope);
		try {
			ProbeResponseDto response = SelectQueryHelper.ExecuteSelectQuery<ProbeResponseDto>(
				applicationClient, serviceUrlBuilder, query, RequestTimeoutMs);
			return new StaticFilterProbeResult(ReadCount(response), null);
		} catch (InvalidOperationException ex) when (ex.Message.StartsWith(SelectQueryFailurePrefix, StringComparison.Ordinal)) {
			string serverMessage = ex.Message[SelectQueryFailurePrefix.Length..];
			if (NotAboutTheFilterMarkers.Any(marker => serverMessage.Contains(marker, StringComparison.OrdinalIgnoreCase))) {
				return Unverified(targetAttribute, serverMessage);
			}
			throw new ArgumentException(
				$"apply-static-filter on '{targetAttribute}': the environment rejected the filter built for '{rootSchemaName}' " +
				$"({serverMessage}). Nothing was saved.", ex);
		} catch (Exception ex) when (ex is not ArgumentException) {
			return Unverified(targetAttribute, ex.Message);
		}
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
