using System;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Resolves Lookup filter values on a lookup's reference schema. Two directions:
/// <list type="bullet">
///   <item><description>display name → <see cref="LookupResolution"/> (Id + display value)
///     so AI callers can pass natural phrases like "Customer" instead of pre-resolving
///     GUIDs;</description></item>
///   <item><description>GUID → display value, used by the converter to enrich GUID-only
///     inputs so the generated rule's UI renders the lookup name correctly.</description></item>
/// </list>
/// Implementations cache hits per resolver instance lifetime.
/// </summary>
internal interface ILookupValueResolver {
	/// <summary>
	/// Resolves <paramref name="displayValue"/> to a record on <paramref name="schemaName"/>.
	/// Returns the resolved record's Id together with the original display value so the
	/// converter can emit the full lookup parameter shape. Throws
	/// <see cref="BusinessRuleFilterException"/> with
	/// <see cref="BusinessRuleFilterErrorCodes.LookupRecordNotFound"/> when no record matches,
	/// or with a "filter.lookup-value-ambiguous" code when multiple records match.
	/// </summary>
	LookupResolution Resolve(string schemaName, string displayValue, string fieldPath);

	/// <summary>
	/// Tries to fetch the primary-display-column value of the record identified by
	/// <paramref name="id"/> on <paramref name="schemaName"/>. Returns <c>null</c> when no
	/// record exists or when the schema has no primary display column. Used to enrich
	/// GUID-only inputs so the generated business rule's UI shows the lookup record's
	/// name rather than a placeholder.
	/// </summary>
	string? TryResolveDisplayName(string schemaName, Guid id);
}
