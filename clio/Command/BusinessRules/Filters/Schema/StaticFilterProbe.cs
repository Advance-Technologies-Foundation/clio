using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Runs the ESQ envelope built for an apply-static-filter action on the environment before the rule is saved.
/// </summary>
/// <remarks>
/// ENG-95270: the business-rule guidance used to make the agent re-type the friendly filter as a full ESQ
/// SelectQuery and run it through execute-esq as a mandatory dry run, so one rule needed both filter dialects.
/// The envelope clio itself built is the only thing worth testing, so the tool runs it: a one-row COUNT on the
/// lookup's reference schema. A server rejection (unknown column, wrong value type, broken macro) fails the
/// rule before anything is written.
/// </remarks>
internal interface IStaticFilterProbe {
	void Probe(string esqEnvelope, string rootSchemaName, string targetAttribute);
}

internal sealed class StaticFilterProbe(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	: IStaticFilterProbe {

	private const string CountAlias = "ProbeCount";
	private const string SelectQueryFailurePrefix = "SelectQuery failed: ";

	public void Probe(string esqEnvelope, string rootSchemaName, string targetAttribute) {
		JsonObject query = JsonSerializer.SerializeToNode(SelectQueryHelper.BuildCountQuery(rootSchemaName, CountAlias))!.AsObject();
		query["filters"] = JsonNode.Parse(esqEnvelope);
		try {
			SelectQueryHelper.ExecuteSelectQuery<ProbeResponseDto>(applicationClient, serviceUrlBuilder, query);
		} catch (InvalidOperationException ex) when (ex.Message.StartsWith(SelectQueryFailurePrefix, StringComparison.Ordinal)) {
			throw new ArgumentException(
				$"apply-static-filter on '{targetAttribute}': the environment rejected the filter built for '{rootSchemaName}' " +
				$"({ex.Message[SelectQueryFailurePrefix.Length..]}). Nothing was saved. Fix the column, value or macro the " +
				"message names and call again; no execute-esq dry run is needed, this check already ran the filter.", ex);
		}
	}

	private sealed class ProbeResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto;
}
