namespace Clio.Command;

using System;
using System.Collections.Generic;

/// <summary>
/// The one rule for reading a Freedom UI page's full chain: from the page's root (base) variant.
/// </summary>
/// <remarks>
/// A page replaced in several packages has one <c>SysSchema</c> row per package, and a name lookup can resolve to
/// any of them. A chain read from a replacing variant can miss layers, so the chain is re-read from the root variant
/// whenever the lookup landed elsewhere. <c>get-page</c> and the <c>update-page</c> package-choice check both read
/// the chain through here: if they read it differently, <c>page.packageChoice</c> and the
/// <c>package-choice-required</c> refusal disagree about the same page.
/// </remarks>
internal static class PageHierarchyChain {
	/// <summary>
	/// Returns the UId of the page's root (base) variant: the LOWEST chain entry carrying the page name, which is
	/// not the chain root when the page is built from a template.
	/// </summary>
	/// <param name="effectiveFirstChain">The chain, effective (leaf) schema first.</param>
	/// <param name="schemaName">The page schema name.</param>
	/// <returns>The root variant's UId, or <c>null</c> when no entry carries the page name.</returns>
	internal static string FindRootSchemaUId(IReadOnlyList<PageDesignerHierarchySchema> effectiveFirstChain,
		string schemaName) {
		for (int i = effectiveFirstChain.Count - 1; i >= 0; i--) {
			if (string.Equals(effectiveFirstChain[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
				return effectiveFirstChain[i].UId;
			}
		}
		return null;
	}

	/// <summary>
	/// Reads the full chain from the page's root variant, reusing <paramref name="anchorChain"/> when it was
	/// already read from there.
	/// </summary>
	/// <param name="hierarchyClient">Designer hierarchy client.</param>
	/// <param name="schemaName">The page schema name.</param>
	/// <param name="anchorSchemaUId">The UId <paramref name="anchorChain"/> was read from.</param>
	/// <param name="designPackageUId">The design package the chain is resolved for.</param>
	/// <param name="anchorChain">The chain read from <paramref name="anchorSchemaUId"/>, effective schema first.</param>
	/// <returns>The full chain, effective schema first; <paramref name="anchorChain"/> when the re-read is empty.</returns>
	/// <exception cref="Exception">Whatever the designer service throws for the re-read; the caller reports it.</exception>
	internal static IReadOnlyList<PageDesignerHierarchySchema> ReadFromRootVariant(
		IPageDesignerHierarchyClient hierarchyClient,
		string schemaName,
		string anchorSchemaUId,
		string designPackageUId,
		IReadOnlyList<PageDesignerHierarchySchema> anchorChain) {
		string rootSchemaUId = FindRootSchemaUId(anchorChain, schemaName);
		if (string.IsNullOrWhiteSpace(rootSchemaUId)
			|| string.Equals(rootSchemaUId, anchorSchemaUId, StringComparison.OrdinalIgnoreCase)) {
			return anchorChain;
		}
		IReadOnlyList<PageDesignerHierarchySchema> fullChain =
			hierarchyClient.GetParentSchemas(rootSchemaUId, designPackageUId);
		return fullChain is { Count: > 0 } ? fullChain : anchorChain;
	}
}
