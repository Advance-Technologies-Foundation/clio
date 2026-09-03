namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Whether a persisted schema mutation changes the OData entity contract, which is what decides
/// if the OData entities assembly has to be rebuilt.
/// </summary>
/// <remarks>
/// Measured against a live stand: the OData contract carries only the property name, its EDM type,
/// nullability, and the navigation properties. A caption, description, default value, mask, usage type,
/// required flag, or primary-display column therefore leaves the published contract byte-for-byte
/// identical, and a rebuild after one of those spends 90-120s of server-side compilation producing the
/// document it already had. Adding or removing a column, changing its type, changing its reference
/// schema, or creating a schema does change the contract, and nothing else in the platform rebuilds it -
/// <c>WorkspaceBuilder.BuildOData</c> has exactly one caller, the background task this request starts.
/// </remarks>
internal enum ODataContractImpact
{
	/// <summary>The mutation leaves the published OData contract unchanged; no rebuild is requested.</summary>
	Unchanged,

	/// <summary>The mutation changes the published OData contract; the entities assembly must be rebuilt.</summary>
	Changed
}
