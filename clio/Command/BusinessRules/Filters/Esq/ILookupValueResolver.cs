using System;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Resolves a Lookup filter value that the caller supplied as a display name (not a GUID)
/// to the matching record Id on the lookup's reference schema. Used by the local ESQ filter
/// builder so AI assistants can pass natural phrases like "Customer" or "Insurance" instead
/// of pre-resolving GUIDs. Implementations cache hits per resolver instance lifetime.
/// </summary>
internal interface ILookupValueResolver {
	/// <summary>
	/// Resolves <paramref name="displayValue"/> to a record Id on <paramref name="schemaName"/>.
	/// Throws <see cref="BusinessRuleFilterException"/> with
	/// <see cref="BusinessRuleFilterErrorCodes.LookupRecordNotFound"/> when no record matches,
	/// or with a "filter.lookup-value-ambiguous" error code when more than one record matches.
	/// </summary>
	Guid Resolve(string schemaName, string displayValue, string fieldPath);
}
