using System.Collections.Generic;

namespace ClioRing.Ipc;

/// <summary>
/// Decides which assert-infrastructure sections are REQUIRED for a given deploy mode, and reports any
/// that did not pass. A real deploy must be blocked (or explicitly overridden, naming each failure)
/// when this returns a non-empty list. Local needs local + filesystem; Rancher (default Kubernetes)
/// needs k8. Pure — reused by the wizard and the dry-run harness.
/// </summary>
public static class PreflightGate {
	/// <summary>Required section names for the chosen infra mode.</summary>
	public static IReadOnlyList<string> RequiredSections(bool local) =>
		local ? new[] { "local", "filesystem" } : new[] { "k8" };

	/// <summary>
	/// Named failures for each required section that is not "pass" (empty = all required checks passed).
	/// </summary>
	public static IReadOnlyList<string> RequiredFailures(AssertResult assert, bool local) {
		var failures = new List<string>();
		foreach (string section in RequiredSections(local)) {
			if (!assert.SectionPasses(section)) {
				failures.Add($"assert-infrastructure section '{section}' = {assert.SectionStatus(section)} (required for {(local ? "Local" : "Rancher")} deploy; overall={assert.Status})");
			}
		}
		return failures;
	}
}
