using System;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Resolves a lookup display name to a record GUID against a Creatio environment.
/// Implementations cache per instance and reject no-match / multi-match with explicit messages.
/// </summary>
internal interface ILookupValueResolver {
	Guid ResolveIdByDisplayName(string referenceSchemaName, string displayName);
}
