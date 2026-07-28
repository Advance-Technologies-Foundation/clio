using System;
using System.Collections.Generic;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Resolves a page (client-unit) schema NAME to its effective schema hierarchy in a given package context —
/// package-scoped and replacement-aware. A page replaced across packages has a DIFFERENT UId per package, so a
/// flat by-name lookup (rowCount 1, no package) resolves to an arbitrary variant; this resolver instead prefers
/// the variant defined in the target package and otherwise walks the designer hierarchy to the root, matching how
/// the platform resolves the effective page from a package.
/// </summary>
/// <remarks>
/// <see cref="Clio.Command.BusinessRules.PageBusinessRuleSchemaProvider"/> currently holds an equivalent inline
/// copy of this resolution. Unifying both consumers onto this single resolver (and the shared save/rebuild
/// round-trip) is tracked as ENG-93249 — deferred out of clio PR #791 to avoid touching the working business-rule
/// path in that PR.
/// </remarks>
internal interface IPageSchemaResolver {
	/// <summary>
	/// Resolves <paramref name="pageSchemaName"/> to its effective designer hierarchy as seen from
	/// <paramref name="packageUId"/>. Element [0] is the effective (current) schema; the rest are its parents.
	/// Throws when the page cannot be found or its hierarchy is empty.
	/// </summary>
	IReadOnlyList<PageDesignerHierarchySchema> ResolveHierarchy(string pageSchemaName, Guid packageUId);
}

internal sealed class PageSchemaResolver(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPageDesignerHierarchyClient hierarchyClient)
	: IPageSchemaResolver {

	public IReadOnlyList<PageDesignerHierarchySchema> ResolveHierarchy(string pageSchemaName, Guid packageUId) {
		string normalizedSchemaName = pageSchemaName.Trim();
		string targetPackageUId = packageUId.ToString();
		// Prefer the variant DEFINED IN the target package (deterministic); otherwise resolve the root schema.
		var (schemaUId, schemaLookupError) = PageSchemaMetadataHelper.QueryExistingSchemaInPackage(
			applicationClient, serviceUrlBuilder, normalizedSchemaName, targetPackageUId);
		if (!string.IsNullOrWhiteSpace(schemaLookupError)) {
			throw new InvalidOperationException(schemaLookupError);
		}
		if (string.IsNullOrWhiteSpace(schemaUId)) {
			schemaUId = ResolveRootSchemaUId(normalizedSchemaName, targetPackageUId);
		}
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy =
			hierarchyClient.GetParentSchemas(schemaUId, targetPackageUId);
		if (hierarchy.Count == 0) {
			throw new InvalidOperationException($"Page schema '{pageSchemaName}' hierarchy is empty.");
		}
		return hierarchy;
	}

	private string ResolveRootSchemaUId(string pageSchemaName, string targetPackageUId) {
		var (metadata, error) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			applicationClient, serviceUrlBuilder, pageSchemaName, ("UId", "UId"), ("PackageUId", "SysPackage.UId"));
		if (metadata is null) {
			throw new InvalidOperationException(error);
		}
		string schemaUId = metadata["UId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId)) {
			throw new InvalidOperationException($"Page schema '{pageSchemaName}' metadata is missing schema UId.");
		}
		string metadataPackageUId = metadata["PackageUId"]?.ToString();
		string designPackageUId = hierarchyClient.GetDesignPackageUId(schemaUId);
		if (string.IsNullOrWhiteSpace(designPackageUId)) {
			designPackageUId = string.IsNullOrWhiteSpace(metadataPackageUId) ? targetPackageUId : metadataPackageUId;
		}
		IReadOnlyList<PageDesignerHierarchySchema> initialHierarchy =
			hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
		return FindRootSchemaUId(initialHierarchy, pageSchemaName) ?? schemaUId;
	}

	private static string FindRootSchemaUId(IReadOnlyList<PageDesignerHierarchySchema> hierarchy, string schemaName) {
		for (int i = hierarchy.Count - 1; i >= 0; i--) {
			if (string.Equals(hierarchy[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
				return hierarchy[i].UId;
			}
		}
		return null;
	}
}
