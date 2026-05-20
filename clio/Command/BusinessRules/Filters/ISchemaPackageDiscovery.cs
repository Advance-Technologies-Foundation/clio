using System;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Discovers the package UId that owns the root definition of an entity schema by name.
/// Used as a fallback when <c>EntitySchemaDesignerService.svc/GetSchemaDesignItem</c> with an
/// empty <c>PackageUId</c> fails to return a schema. Base lookups like <c>City</c> exist in
/// several packages (root in <c>CrtCoreBase</c>, plus extensions in <c>SSP</c> and
/// <c>CrtGoogleAnalytics</c>); the designer endpoint cannot disambiguate without an explicit
/// package context, so we must resolve the owning package via a direct <c>SysSchema</c> query.
/// </summary>
internal interface ISchemaPackageDiscovery {
	/// <summary>
	/// Returns the <c>SysPackage.UId</c> of the package that owns the root (non-extending)
	/// definition of <paramref name="schemaName"/>, or <c>null</c> if no such schema exists.
	/// </summary>
	Guid? TryFindRootPackageUId(string schemaName);
}
