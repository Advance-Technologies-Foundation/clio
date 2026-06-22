namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared constants for the write-path layout-guidance gate enforced by both <c>update-page</c>
/// (<see cref="PageUpdateTool"/>) and <c>sync-pages</c> (<see cref="PageSyncTool"/>). The gate is
/// fail-closed: a body that adds or lays out <c>crt.*</c> view components is rejected unless the
/// <see cref="RequiredGuidanceName"/> guidance was fetched this session or the call is forced.
/// </summary>
/// <remarks>
/// The gate is satisfied ONLY by <see cref="RequiredGuidanceName"/> — the thin <c>ui-guidelines</c>
/// index does NOT satisfy it, because reading only the index (and then composing a layout from
/// memory) is exactly the failure this gate exists to prevent.
/// </remarks>
internal static class PageLayoutGuidanceGate {

	/// <summary>
	/// The canonical guidance name that satisfies the gate. Matches the
	/// <c>ui-page-layout</c> catalog entry recorded by get-guidance in the guidance access ledger.
	/// </summary>
	public const string RequiredGuidanceName = "ui-page-layout";

	/// <summary>
	/// The actionable rejection message returned (as the response <c>Error</c>) when the gate
	/// blocks a layout-composing body. Names the exact get-guidance call and the force override.
	/// </summary>
	public const string RejectionMessage =
		"Layout guidance required: this body adds or lays out Freedom UI components (a crt.* "
		+ "insert in viewConfigDiff) but the ui-page-layout guidance was not read in this session. "
		+ "Call get-guidance name=ui-page-layout first and author the layout from it (the concept-to-"
		+ "component map, grid/column math, container nesting, grouping, captions) instead of from "
		+ "memory; read the existing page with get-page so the new layout matches its style. "
		+ "The ui-guidelines index alone does NOT satisfy this — fetch the ui-page-layout leaf. "
		+ "To deliberately override this gate, retry with force:true.";

	/// <summary>
	/// The single fail-closed decision shared by both write-path tools: a body should be blocked
	/// when it is NOT forced, it adds or lays out <c>crt.*</c> components, AND the
	/// <see cref="RequiredGuidanceName"/> guidance was not fetched this session. Both
	/// <see cref="PageUpdateTool"/> and <see cref="PageSyncTool"/> call this so the gate decision
	/// never drifts between them; each keeps only its own per-tool response construction local.
	/// </summary>
	/// <param name="detector">Detects whether <paramref name="body"/> adds or lays out components.</param>
	/// <param name="ledger">Records which guidance articles were fetched this session.</param>
	/// <param name="body">The raw page body being written.</param>
	/// <param name="force">Whether the caller explicitly forced the write (overrides the gate).</param>
	/// <returns><see langword="true"/> when the body must be blocked; otherwise <see langword="false"/>.</returns>
	internal static bool ShouldBlock(
		IPageLayoutCompositionDetector detector,
		IGuidanceAccessLedger ledger,
		string body,
		bool force) =>
		!force
		&& detector.BodyAddsOrLaysOutComponents(body)
		&& !ledger.WasFetched(RequiredGuidanceName);
}
