namespace Clio.Command;

using System;

/// <summary>
/// Appends an actionable phantom-cache recovery hint to a page-schema-hierarchy READ failure surfaced by
/// <c>get-page</c> (<see cref="PageGetCommand"/>) and <c>update-page</c> (<see cref="PageUpdateCommand"/>).
/// </summary>
/// <remarks>
/// ENG-94418: a concurrent <c>create-app-section</c> whose creation is abandoned mid-flight can leave the
/// Creatio server-side schema-manager cache holding a <i>phantom</i> for the half-created section. Two
/// symptoms then surface on the client through <see cref="IPageDesignerHierarchyClient.GetParentSchemas"/>:
/// the server builds a parent lookup from an empty cached collection, emitting the empty-IN() SqlException
/// (<c>Incorrect syntax near ')'</c>), or the hierarchy simply comes back empty. clio only surfaces these
/// symptoms — it does not build the failing SQL and cannot fix the server cache — so the actionable
/// recovery is to clear that cache.
/// <para>
/// This mirrors the existing save-path <c>PageUpdateCommand.AppendActionableHint</c> pattern: a pure,
/// additive <c>string → string</c> transform that appends a bracketed <c>[hint: …]</c> only when the error
/// carries a poisoned-cache signature, leaving every other error untouched. ENG-94418 Q1 verified on a
/// live .NET Framework stand that flushing Redis does NOT clear this in-process phantom, so the hint
/// directs to <b>Restart Creatio</b> as the confirmed recovery (a web-farm / Redis-distributed
/// deployment may differ).
/// </para>
/// </remarks>
internal static class PageHierarchyRecoveryHint {

	/// <summary>
	/// The empty-IN() SqlException signature the Creatio server emits when the schema-manager cache holds a
	/// phantom for a section whose concurrent creation was abandoned: the parent set is empty, so the server
	/// builds <c>… IN ()</c> and SQL Server rejects it near the <c>)</c>.
	/// </summary>
	internal const string EmptyInSqlSignature = "Incorrect syntax near ')'";

	// The empty-hierarchy message both get-page (PageGetCommand.TryGetPage) and update-page
	// (PageUpdateCommand.TryGetHierarchy) already produce when GetParentSchemas returns no rows.
	private const string EmptyHierarchySignature = "hierarchy is empty";

	/// <summary>
	/// The recovery hint appended to a poisoned-cache hierarchy-read failure. References the ENG-94418 root
	/// cause and directs to Restart Creatio — the confirmed recovery. ENG-94418 Q1 verified on a live
	/// .NET Framework stand that flushing Redis (<c>clio clear-redis-db</c>) does NOT clear this phantom,
	/// so the hint no longer suggests it.
	/// </summary>
	internal const string Hint =
		" [hint: the page schema hierarchy could not be resolved. This is often the Creatio schema-manager " +
		"cache holding a phantom for a section whose concurrent creation was abandoned, which poisons " +
		"hierarchy reads (ENG-94418). Restart Creatio to clear the server schema-manager cache — the " +
		"confirmed recovery; flushing the Redis cache alone does NOT clear this phantom. Then verify the " +
		"schema UId via list-pages.]";

	/// <summary>
	/// Returns <paramref name="error"/> unchanged unless it carries a poisoned-cache hierarchy-read
	/// signature — the empty-IN() SqlException text or an empty-hierarchy message — in which case
	/// <see cref="Hint"/> is appended exactly once. Additive: any other error (including a generic
	/// hierarchy read failure) is returned verbatim.
	/// </summary>
	/// <param name="error">The user-facing error message already composed by the calling command seam.</param>
	/// <returns>The error, with the recovery hint appended when a poisoned-cache signature is present.</returns>
	internal static string Append(string error) {
		if (string.IsNullOrEmpty(error) || error.Contains("ENG-94418", StringComparison.Ordinal)) {
			return error;
		}
		bool carriesPhantomSignature =
			error.Contains(EmptyInSqlSignature, StringComparison.OrdinalIgnoreCase)
			|| error.Contains(EmptyHierarchySignature, StringComparison.OrdinalIgnoreCase);
		return carriesPhantomSignature ? error + Hint : error;
	}
}
