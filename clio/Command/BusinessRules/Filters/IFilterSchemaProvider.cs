using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Fetches columns of a schema referenced by a static-filter root or a backward-reference child.
/// Implementations cache results per instance lifetime so a single rule validation does not
/// re-fetch the same schema multiple times.
/// </summary>
internal interface IFilterSchemaProvider {
	/// <summary>
	/// Returns columns of the requested schema, keyed by column Name (ordinal).
	/// Throws <see cref="BusinessRuleFilterException"/> with <see cref="BusinessRuleFilterErrorCodes.PathUnknown"/>
	/// when the schema cannot be resolved on the target environment.
	/// </summary>
	IReadOnlyDictionary<string, EntitySchemaColumnDto> GetSchemaColumns(string schemaName);
}
