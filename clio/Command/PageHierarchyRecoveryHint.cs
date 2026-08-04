namespace Clio.Command;

using System;

/// <summary>
/// Appends an actionable phantom-cache recovery hint to a page-schema-hierarchy READ failure surfaced by
/// <c>get-page</c> (<see cref="PageGetCommand"/>) and <c>update-page</c> (<see cref="PageUpdateCommand"/>).
/// </summary>
/// <remarks>
/// ENG-94418: a concurrent <c>create-app-section</c> whose creation is abandoned mid-flight can leave the
/// Creatio server-side schema-manager cache holding a <i>phantom</i> for the half-created section. The
/// symptom surfaces on the client through <see cref="IPageDesignerHierarchyClient.GetParentSchemas"/>: the
/// server builds a parent lookup from an empty cached collection, emitting the empty-IN() SqlException
/// (<c>Incorrect syntax near ')'</c>). clio only surfaces this symptom — it does not build the failing SQL
/// and cannot fix the server cache — so the actionable recovery is to clear that cache. An <i>empty</i>
/// hierarchy is deliberately NOT treated as a phantom signal: it has legitimate non-phantom causes (a
/// stale post-save bundle, a wrong/renamed schema name), so keying the hint on it over-fires and would
/// recommend an unnecessary production restart (ENG-94418 review).
/// <para>
/// This mirrors the existing save-path <c>PageUpdateCommand.AppendActionableHint</c> pattern: a pure,
/// additive <c>string → string</c> transform that appends a bracketed <c>[hint: …]</c> only when the error
/// carries a poisoned-cache signature, leaving every other error untouched. ENG-94418 Q1 verified on a
/// live .NET Framework stand that flushing Redis does NOT clear this in-process phantom, so the hint
/// directs to <b>Restart Creatio</b> as the confirmed recovery (a web-farm / Redis-distributed
/// deployment may differ).
/// </para>
/// <para>
/// <b>Known limitation — the signature is Microsoft SQL Server specific.</b>
/// <see cref="EmptyInSqlSignature"/> is the SQL Server wording, which is what the ENG-94418 repro
/// stand emitted. It is unknown whether the phantom produces a SQL syntax error at all on a
/// PostgreSQL environment, and if it does, PostgreSQL words it differently
/// (<c>syntax error at or near ")"</c>). On PostgreSQL this hint therefore does not fire — a missed
/// diagnostic, not a wrong one. The signature is deliberately NOT broadened on inference: firing
/// wrongly recommends restarting a production application, and the Restart-Creatio recovery itself is
/// confirmed only on the MSSQL / .NET Framework stand. Broadening requires first observing the
/// phantom symptom on a PostgreSQL stand.
/// </para>
/// </remarks>
internal static class PageHierarchyRecoveryHint {

	/// <summary>
	/// The empty-IN() SqlException signature the Creatio server emits when the schema-manager cache holds a
	/// phantom for a section whose concurrent creation was abandoned: the parent set is empty, so the server
	/// builds <c>… IN ()</c> and SQL Server rejects it near the <c>)</c>. Microsoft SQL Server wording only —
	/// see the PostgreSQL limitation in the type remarks.
	/// </summary>
	internal const string EmptyInSqlSignature = "Incorrect syntax near ')'";

	/// <summary>
	/// The ticket token embedded in <see cref="Hint"/> and used by <see cref="Append"/> as the
	/// already-appended marker, so the idempotency guard can never drift from the hint wording (which
	/// changed twice during ENG-94418 review). Any reword of <see cref="Hint"/> must keep composing this
	/// constant instead of spelling the ticket out inline.
	/// </summary>
	internal const string HintMarker = "ENG-94418";

	/// <summary>
	/// The recovery hint appended to a poisoned-cache hierarchy-read failure. References the ENG-94418 root
	/// cause (via <see cref="HintMarker"/>) and directs to Restart Creatio — the confirmed recovery.
	/// ENG-94418 Q1 verified on a live .NET Framework stand that flushing Redis
	/// (<c>clio clear-redis-db</c>) does NOT clear this phantom, so the hint no longer suggests it.
	/// </summary>
	internal const string Hint =
		" [hint: the page schema hierarchy could not be resolved. This is often the Creatio schema-manager " +
		"cache holding a phantom for a section whose concurrent creation was abandoned, which poisons " +
		"hierarchy reads (" + HintMarker + "). Restart Creatio to clear the server schema-manager cache — " +
		"the confirmed recovery; flushing the Redis cache alone does NOT clear this phantom. Then verify " +
		"the schema UId via list-pages.]";

	/// <summary>
	/// Returns <paramref name="error"/> unchanged unless it carries the poisoned-cache signature — the
	/// empty-IN() SqlException text (<c>Incorrect syntax near ')'</c>) — in which case <see cref="Hint"/>
	/// is appended exactly once. Additive: any other error (a generic hierarchy read failure, or an empty
	/// hierarchy, which has non-phantom causes) is returned verbatim. Idempotent: an error that already
	/// carries <see cref="HintMarker"/> is returned as-is, so wrapping twice cannot double the hint.
	/// </summary>
	/// <param name="error">The user-facing error message already composed by the calling command seam.</param>
	/// <returns>The error, with the recovery hint appended when a poisoned-cache signature is present.</returns>
	internal static string Append(string error) {
		if (string.IsNullOrEmpty(error) || error.Contains(HintMarker, StringComparison.OrdinalIgnoreCase)) {
			return error;
		}
		return error.Contains(EmptyInSqlSignature, StringComparison.OrdinalIgnoreCase) ? error + Hint : error;
	}
}
