using System;
using System.Text.Json;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Builds the Creatio ESQ filter envelope JSON string that the platform stores verbatim
/// in <c>BusinessRuleValueExpression.value</c> for <c>BusinessRuleActionSetFilter</c>.
/// Replaces the previous HTTP delegation to <c>LlmEsqConverterService</c> (CrtCopilot
/// package) so apply-static-filter works on any Creatio environment without that package.
/// </summary>
internal interface ILocalEsqFilterBuilder {
	/// <summary>
	/// Converts a friendly filter into a Creatio ESQ envelope JSON string. Throws
	/// <see cref="BusinessRuleFilterException"/> on schema-aware failures (unknown column,
	/// non-Lookup traversal, invalid GUID for Lookup leaf, malformed backward reference).
	/// </summary>
	string ConvertToEsqFilter(string rootSchemaName, StaticFilterGroup filter);
}

internal sealed class LocalEsqFilterBuilder(
	IFilterSchemaProvider schemaProvider,
	ILookupValueResolver? lookupValueResolver = null)
	: ILocalEsqFilterBuilder {

	// Enums serialized as integers (default System.Text.Json behavior without
	// JsonStringEnumConverter) to match the platform's BVE1 envelope shape. WriteIndented
	// is false to keep the BVE1 escape-once string compact.
	private static readonly JsonSerializerOptions SerializerOptions = new() {
		WriteIndented = false,
		PropertyNamingPolicy = null,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
	};

	public string ConvertToEsqFilter(string rootSchemaName, StaticFilterGroup filter) {
		ArgumentException.ThrowIfNullOrWhiteSpace(rootSchemaName);
		ArgumentNullException.ThrowIfNull(filter);
		LocalEsqFilterConverter converter = new(schemaProvider, lookupValueResolver);
		SerializableFilters envelope = converter.BuildTopLevelGroup(filter, rootSchemaName);
		return JsonSerializer.Serialize(envelope, SerializerOptions);
	}
}
