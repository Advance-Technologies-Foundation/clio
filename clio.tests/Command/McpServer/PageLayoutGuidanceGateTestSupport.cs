using Clio.Command.McpServer.Tools;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Shared test helpers for constructing <see cref="PageUpdateTool"/> / <see cref="PageSyncTool"/>
/// in fixtures that are NOT exercising the write-path layout-guidance gate. Returns a ledger
/// pre-seeded with the <c>ui-page-layout</c> guidance so the gate is satisfied and never blocks
/// these pre-existing tests, plus the real composition detector. Tests that DO target the gate
/// build their own ledger/detector to control the fetched state.
/// </summary>
internal static class PageLayoutGuidanceGateTestSupport {

	/// <summary>Builds a real ledger with <c>ui-page-layout</c> already recorded (gate satisfied).</summary>
	public static IGuidanceAccessLedger SatisfiedLedger() {
		var ledger = new GuidanceAccessLedger();
		ledger.Record(PageLayoutGuidanceGate.RequiredGuidanceName);
		return ledger;
	}

	/// <summary>The production composition detector (stateless, safe to share).</summary>
	public static IPageLayoutCompositionDetector Detector() => new PageLayoutCompositionDetector();
}
