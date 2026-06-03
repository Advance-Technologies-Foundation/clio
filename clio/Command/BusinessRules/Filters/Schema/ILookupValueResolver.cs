using System;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Resolves a lookup display name to a record GUID against a Creatio environment.
/// Implementations cache per instance and reject no-match / multi-match with explicit messages.
/// </summary>
internal interface ILookupValueResolver {
	Guid ResolveIdByDisplayName(string referenceSchemaName, string displayName);

	/// <summary>
	/// Opportunistically resolves a record's primary display value by Id (reverse of <see cref="ResolveIdByDisplayName"/>).
	/// Returns false on missing record / missing primary display column — callers should treat the enrichment as optional.
	/// </summary>
	bool TryResolveDisplayNameById(string referenceSchemaName, Guid id, out string? displayName);
}
