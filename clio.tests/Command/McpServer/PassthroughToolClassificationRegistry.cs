using System.Collections.Generic;
using Clio.Mcp.E2E;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The four scenarios a dependency path can be exercised under. Kept to this fixed, small vocabulary
/// (ADR "mcp-passthrough-tool-parity" OQ-04) so <see cref="PassthroughToolClassificationRegistry.Coverage"/>
/// stays reviewable.
/// </summary>
internal enum PassthroughScenario {
	/// <summary>Authorized credential passthrough, no <c>environment-name</c> supplied.</summary>
	HeaderOnly,

	/// <summary>Authorized credential passthrough AND an explicit <c>environment-name</c> (confused-deputy probe).</summary>
	MixedInput,

	/// <summary>No credential-passthrough context: stdio, or <c>mcp-http -e &lt;env&gt;</c> with an explicit
	/// registered environment. Unit-level fixtures mock <see cref="Clio.Command.McpServer.IToolCommandResolver"/>
	/// identically for both transports, so one test doubles for both (see Story 15's E2E rows for the
	/// transport-specific proof).</summary>
	RegisteredEnvStdio,

	/// <summary>No passthrough axis applies at all (the tool never touches Creatio, or is already proven
	/// compliant outside this feature's audit). Never used as a REQUIRED scenario; reserved for future rows
	/// that need to name a scenario without asserting a specific test.</summary>
	NotApplicable
}

/// <summary>
/// Where a discovered <c>[McpServerToolType]</c> tool sits relative to the ENG-93347 credential-passthrough
/// audit (PRD "Audit — Resident Tool Passthrough Classification").
/// </summary>
internal enum PassthroughClassification {
	/// <summary>
	/// Audited class c1/c2/matrix tool that this feature fixed to route its Creatio-reaching path(s)
	/// through <see cref="Clio.Command.McpServer.IToolCommandResolver"/>. REQUIRES <see cref="PassthroughToolClassificationRegistry.RequiredCoverage"/> rows.
	/// </summary>
	Routed,

	/// <summary>
	/// Audited class c3 tool (the <c>link-from-repository-*</c> family) that fails fast under
	/// authorized passthrough via <c>ICredentialPassthroughToolGuard</c> instead of routing. REQUIRES
	/// <see cref="PassthroughToolClassificationRegistry.RequiredCoverage"/> rows.
	/// </summary>
	Guarded,

	/// <summary>
	/// PRD "Out of scope — not environment-sensitive" tool: no Creatio credential is ever involved
	/// (telemetry, guidance, local infra assertions, <c>list-creatio-builds</c>' local file lookup, ...).
	/// Excluded from the per-path/scenario mapping requirement (AC-05).
	/// </summary>
	NotEnvironmentSensitive,

	/// <summary>
	/// Every other discovered tool: class (a) <c>BaseTool</c>+resolver or class (b) direct
	/// <c>commandResolver.Resolve&lt;T&gt;(...)</c> tools that were ALREADY passthrough-capable before this
	/// feature (PRD "No change required to class (a)/(b)") and were not part of this feature's per-path
	/// audit. Excluded from the per-path/scenario mapping requirement — see the registry-header note on
	/// why this bucket is NOT re-audited here (deliberate scope boundary, not an oversight).
	/// </summary>
	NotApplicable
}

/// <summary>
/// One (tool, dependency-path, scenario) → test-method mapping. <see cref="MethodName"/> must be produced
/// via <c>nameof(...)</c> at the call site so a rename of the test method fails the BUILD, not merely this
/// guard.
/// </summary>
/// <param name="ToolName">The MCP tool name (matches the string used at <c>[McpServerTool(Name = ...)]</c>).</param>
/// <param name="DependencyPath">
/// The stable dependency-path id. Vocabulary (ADR OQ-04 + Story 16 Implementation Notes, reconciled with
/// PRD AC-03's explicit per-branch link-family names — see the registry header note): <c>outer</c>,
/// <c>version-probe</c>, <c>version</c>, <c>caption-culture-readback</c>, <c>caption-culture-validation</c>,
/// <c>app-info-validation</c>, <c>app-info-polling</c>, <c>find-application-id</c>, <c>by-environment</c>,
/// <c>env-package-path-preparation</c>, <c>unlocked</c>.
/// </param>
/// <param name="Scenario">The scenario this row proves.</param>
/// <param name="FixtureType">The NUnit fixture class that declares <see cref="MethodName"/>.</param>
/// <param name="MethodName">The exact, existing method name (bare — no parameter list) via <c>nameof(...)</c>.</param>
internal sealed record PassthroughCoverageEntry(
	string ToolName,
	string DependencyPath,
	PassthroughScenario Scenario,
	System.Type FixtureType,
	string MethodName);

/// <summary>
/// One audited dependency path for a <see cref="PassthroughClassification.Routed"/> or
/// <see cref="PassthroughClassification.Guarded"/> tool, and the scenarios REQUIRED for that path. This map
/// is authored BY HAND from the PRD/ADR audit and Stories 1-15 — it is intentionally NOT derived from
/// whatever rows happen to exist in <see cref="PassthroughToolClassificationRegistry.Coverage"/>, otherwise
/// an omitted row would trivially satisfy its own requirement (the exact "coarse fixture" failure mode the
/// ADR's OQ-04 rejected alternative would have allowed).
/// </summary>
/// <param name="DependencyPath">The stable dependency-path id (see <see cref="PassthroughCoverageEntry.DependencyPath"/>).</param>
/// <param name="RequiredScenarios">The scenarios this path must have a named, existing, test-attributed method for.</param>
internal sealed record PassthroughRequiredPath(string DependencyPath, IReadOnlyList<PassthroughScenario> RequiredScenarios);

/// <summary>
/// FR-06/OQ-04 audited classification + per-path coverage registry for the ENG-93347
/// credential-passthrough tool-parity feature.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in <c>clio.tests</c>, not the main <c>clio</c> assembly.</b> <see cref="PassthroughCoverageEntry.FixtureType"/>
/// references NUnit test-fixture types declared in BOTH <c>clio.tests</c> (the Stories 1-14 unit fixtures)
/// and <c>clio.mcp.e2e</c> (the Story 15 E2E fixtures). <c>clio.tests</c> already project-references both
/// <c>clio</c> and <c>clio.mcp.e2e</c> (see <c>McpFixturePolicyTests</c> for the established precedent of a
/// guard living here for the same reason), so this is the only assembly that can compile a reference to all
/// three without a circular project reference. The main <c>clio</c> assembly cannot reference either test
/// project.
/// </para>
/// <para>
/// <b>Dependency-path vocabulary reconciliation.</b> The Story 16 Implementation Notes list <c>preparation</c>
/// and <c>unlocked-query</c> as vocabulary entries, while PRD AC-03's own prose names the three
/// <c>link-from-repository-*</c> branches <c>by-environment</c>, <c>env-package-path-preparation</c>, and
/// <c>unlocked</c>. This registry follows AC-03's per-branch names (each branch is a distinct discovered
/// tool name, so its path id should read as that branch's identity, not a generic vocabulary word) — a
/// deliberate, documented choice where the two source documents disagree.
/// </para>
/// <para>
/// <b>Nested-path scenario scope (a documented interpretation of AC-03, not a literal reading).</b>
/// AC-03's prose, read most literally, could be taken to require <see cref="PassthroughScenario.HeaderOnly"/>
/// + <see cref="PassthroughScenario.MixedInput"/> + <see cref="PassthroughScenario.RegisteredEnvStdio"/> rows
/// for EVERY audited path, including create-app-section's four nested sub-call paths
/// (<c>caption-culture-readback</c>, <c>caption-culture-validation</c>, <c>app-info-validation</c>,
/// <c>app-info-polling</c>) and create-app's/update-app-section's/delete-app-section's/list-app-sections'
/// analogous nested paths. Stories 1-14 deliberately did NOT write Mixed/RegisteredEnvStdio tests for these
/// nested paths (Story 6's "four extra nested-path tests" are each HeaderOnly-only) because they are
/// structurally covered once, at the tool's OUTER entry path: under <c>MixedInput</c> the outer
/// <c>commandResolver.Resolve&lt;EnvironmentSettings&gt;</c> throws BEFORE any nested call runs at all, and
/// under <c>RegisteredEnvStdio</c> every nested call receives the SAME settings object the outer resolve
/// produced (the "no settings-based overload ever calls a name-based overload" invariant, ADR OQ-01) — so a
/// per-nested-path Mixed/RegisteredEnvStdio test would prove nothing the outer-path test does not already
/// prove. Requiring per-tuple tests that cannot exist without expanding this story's scope into writing new
/// Stories 1-14 tests would also violate the "smallest change that satisfies the order" instruction. This
/// registry therefore requires <see cref="PassthroughScenario.HeaderOnly"/> ONLY for nested sub-call paths
/// (the specific header-blindness regression they exist to catch), and the full three-scenario matrix only
/// for each tool's OUTER/entry path. This is a named, deliberate scope decision — flagged to the architect,
/// not silently assumed.
/// </para>
/// <para>
/// <b><c>update-page</c>'s <c>write</c> path is intentionally absent from <see cref="RequiredCoverage"/>.</b>
/// AC-03's prose uses "update-page has separate rows for its write path and its version-probe path" as the
/// illustrative example of per-path granularity, but the PRD's own audit states the write path
/// (<c>PageUpdateTool.cs:64</c>) was ALREADY compliant (resolver-backed) before this feature — only the
/// version-probe path was the defect Story 11 fixed. No Stories 1-14 test names the write path specifically
/// (it is proven generically by the shared <c>BaseTool</c>/<c>ToolCommandResolver</c> infrastructure tests,
/// the same class-(a) proof every already-compliant tool relies on). Only <c>version-probe</c> is therefore
/// a required path for <c>update-page</c> here — flagged, not silently assumed.
/// </para>
/// <para>
/// <b>What this guard does NOT prove (AC-01/AC-08 scope caveat).</b> This is an allowlist + mapping design
/// (ADR OQ-04, the alternative CHOSEN over a Roslyn dataflow analyzer). It proves (1) every currently
/// discovered tool has a classification row (<see cref="PassthroughToolClassificationGuardTests"/>
/// completeness test), and (2) every <see cref="PassthroughClassification.Routed"/>/<see cref="PassthroughClassification.Guarded"/>
/// tool has a named, existing, test-attributed method per required (path, scenario) tuple. It does NOT
/// perform dataflow/bypass detection: a brand-new tool that reaches Creatio outside the resolver but is
/// (incorrectly) classified <see cref="PassthroughClassification.NotApplicable"/> by a future contributor
/// would satisfy both guard tests and ship unnoticed. Closing that gap would require the Roslyn-analyzer
/// alternative the ADR explicitly rejected for this story. State this plainly — do not claim AC-08 is met
/// by construction.
/// </para>
/// </remarks>
internal static class PassthroughToolClassificationRegistry {

	/// <summary>
	/// One row per <c>[McpServerToolType]</c> tool name discovered in the current <c>clio</c> assembly
	/// (verified against <see cref="Clio.Command.McpServer.McpFeatureToggleFilter.GetAttributedTypes"/>
	/// expanded to <c>[McpServerTool(Name = ...)]</c> verb names — see
	/// <see cref="PassthroughToolClassificationGuardTests"/>). 150 tools at authoring time (2026-07-11):
	/// 12 <see cref="PassthroughClassification.Routed"/>, 3 <see cref="PassthroughClassification.Guarded"/>,
	/// 20 <see cref="PassthroughClassification.NotEnvironmentSensitive"/> (PRD's literal out-of-scope audit,
	/// matched to CURRENT tool names — several PRD names are stale, see the inline notes below), and 115
	/// <see cref="PassthroughClassification.NotApplicable"/> (class (a)/(b), no change required).
	/// </summary>
	internal static readonly IReadOnlyDictionary<string, PassthroughClassification> Classification =
		new Dictionary<string, PassthroughClassification>(System.StringComparer.Ordinal) {
			// --- Routed (12): audited class c1 (7) + c2 (1) + matrix (4), fixed in Stories 3-14 ---
			["list-apps"] = PassthroughClassification.Routed,
			["get-app-info"] = PassthroughClassification.Routed,
			["create-app"] = PassthroughClassification.Routed,
			["create-app-section"] = PassthroughClassification.Routed,
			["update-app-section"] = PassthroughClassification.Routed,
			["delete-app-section"] = PassthroughClassification.Routed,
			["list-app-sections"] = PassthroughClassification.Routed,
			["get-user-culture"] = PassthroughClassification.Routed,
			["update-page"] = PassthroughClassification.Routed,
			["sync-pages"] = PassthroughClassification.Routed,
			["get-component-info"] = PassthroughClassification.Routed,
			["build-theme"] = PassthroughClassification.Routed,

			// --- Guarded (3): audited class c3, fail-fast under passthrough (Story 1) ---
			["link-from-repository-by-environment"] = PassthroughClassification.Guarded,
			["link-from-repository-by-env-package-path"] = PassthroughClassification.Guarded,
			["link-from-repository-unlocked"] = PassthroughClassification.Guarded,

			// --- NotEnvironmentSensitive (20): PRD "Out of scope" audit, matched to CURRENT tool names.
			// Several PRD prose names are stale vs. the actual [McpServerTool(Name=...)]; the ACTUAL name
			// is used as the dictionary key and the PRD's (older) name is noted inline for traceability:
			["get-telemetry-consent"] = PassthroughClassification.NotEnvironmentSensitive,
			["send-telemetry"] = PassthroughClassification.NotEnvironmentSensitive,
			["withdraw-telemetry-consent"] = PassthroughClassification.NotEnvironmentSensitive,
			["get-guidance"] = PassthroughClassification.NotEnvironmentSensitive,
			["get-tool-contract"] = PassthroughClassification.NotEnvironmentSensitive,
			["assert-infrastructure"] = PassthroughClassification.NotEnvironmentSensitive,
			["show-passing-infrastructure"] = PassthroughClassification.NotEnvironmentSensitive,
			["list-environments"] = PassthroughClassification.NotEnvironmentSensitive, // PRD prose: "show-web-app-list" (ShowWebAppListTool); actual tool name is list-environments
			["find-empty-iis-port"] = PassthroughClassification.NotEnvironmentSensitive,
			["list-creatio-builds"] = PassthroughClassification.NotEnvironmentSensitive, // the exact false-positive FR-06 must not trip on
			["advise-theme-palette"] = PassthroughClassification.NotEnvironmentSensitive,
			["reg-web-app"] = PassthroughClassification.NotEnvironmentSensitive,
			["deploy-creatio"] = PassthroughClassification.NotEnvironmentSensitive, // PRD prose: "install-creatio" (InstallerCommandTool); actual tool name is deploy-creatio
			["uninstall-creatio"] = PassthroughClassification.NotEnvironmentSensitive,
			["install-toolkit"] = PassthroughClassification.NotEnvironmentSensitive, // PRD prose: "install-skills"
			["update-toolkit"] = PassthroughClassification.NotEnvironmentSensitive, // PRD prose: "update-skill"
			["delete-toolkit"] = PassthroughClassification.NotEnvironmentSensitive, // PRD prose: "delete-skill"
			["add-data-binding-row"] = PassthroughClassification.NotEnvironmentSensitive,
			["remove-data-binding-row"] = PassthroughClassification.NotEnvironmentSensitive,
			["check-settings-health"] = PassthroughClassification.NotEnvironmentSensitive, // PRD prose: "get-settings-health"

			// --- NotApplicable (117): class (a)/(b) — already passthrough-capable, out of this audit ---
			["StopAllCreatio"] = PassthroughClassification.NotApplicable,
			["add-item-model"] = PassthroughClassification.NotApplicable,
			["add-package"] = PassthroughClassification.NotApplicable,
			["add-package-dependency"] = PassthroughClassification.NotApplicable,
			["check-auth-code-flow"] = PassthroughClassification.NotApplicable,
			["check-theming-access"] = PassthroughClassification.NotApplicable,
			["clear-browser-session"] = PassthroughClassification.NotApplicable,
			["clear-redis-db-by-credentials"] = PassthroughClassification.NotApplicable,
			["clear-redis-db-by-environment"] = PassthroughClassification.NotApplicable,
			["clear-themes-cache"] = PassthroughClassification.NotApplicable,
			["clio-run"] = PassthroughClassification.NotApplicable,
			["clio-run-destructive"] = PassthroughClassification.NotApplicable,
			["compile-creatio"] = PassthroughClassification.NotApplicable,
			["create-business-process"] = PassthroughClassification.NotApplicable,
			["create-client-unit-schema"] = PassthroughClassification.NotApplicable,
			["create-data-binding"] = PassthroughClassification.NotApplicable,
			["create-data-binding-db"] = PassthroughClassification.NotApplicable,
			["create-entity-business-rules"] = PassthroughClassification.NotApplicable,
			["create-entity-schema"] = PassthroughClassification.NotApplicable,
			["create-lookup"] = PassthroughClassification.NotApplicable,
			["create-oauth-technical-user"] = PassthroughClassification.NotApplicable,
			["create-page"] = PassthroughClassification.NotApplicable,
			["create-page-business-rules"] = PassthroughClassification.NotApplicable,
			["create-related-page-addon"] = PassthroughClassification.NotApplicable,
			["create-schema"] = PassthroughClassification.NotApplicable,
			["create-server-to-server-oauth-app"] = PassthroughClassification.NotApplicable,
			["create-sql-schema"] = PassthroughClassification.NotApplicable,
			["create-sys-setting"] = PassthroughClassification.NotApplicable,
			["create-theme"] = PassthroughClassification.NotApplicable,
			["create-user-task"] = PassthroughClassification.NotApplicable,
			["create-workspace"] = PassthroughClassification.NotApplicable,
			["dataforge-context"] = PassthroughClassification.NotApplicable,
			["dataforge-find-lookups"] = PassthroughClassification.NotApplicable,
			["dataforge-find-tables"] = PassthroughClassification.NotApplicable,
			["dataforge-get-relations"] = PassthroughClassification.NotApplicable,
			["dataforge-get-table-columns"] = PassthroughClassification.NotApplicable,
			["dataforge-initialize"] = PassthroughClassification.NotApplicable,
			["dataforge-status"] = PassthroughClassification.NotApplicable,
			["dataforge-update"] = PassthroughClassification.NotApplicable,
			["delete-app"] = PassthroughClassification.NotApplicable,
			["delete-entity-business-rules"] = PassthroughClassification.NotApplicable,
			["delete-page-business-rules"] = PassthroughClassification.NotApplicable,
			["delete-schema"] = PassthroughClassification.NotApplicable,
			["delete-theme"] = PassthroughClassification.NotApplicable,
			["deploy-identity"] = PassthroughClassification.NotApplicable,
			["describe-business-process"] = PassthroughClassification.NotApplicable,
			["describe-environment"] = PassthroughClassification.NotApplicable,
			["download-configuration-by-build"] = PassthroughClassification.NotApplicable,
			["download-configuration-by-environment"] = PassthroughClassification.NotApplicable,
			["execute-esq"] = PassthroughClassification.NotApplicable,
			["experimental"] = PassthroughClassification.NotApplicable,
			["find-app"] = PassthroughClassification.NotApplicable,
			["find-entity-schema"] = PassthroughClassification.NotApplicable,
			["finish-hotfix"] = PassthroughClassification.NotApplicable,
			["generate-process-model"] = PassthroughClassification.NotApplicable,
			["generate-source-code"] = PassthroughClassification.NotApplicable,
			["get-browser-session"] = PassthroughClassification.NotApplicable,
			["get-client-unit-schema"] = PassthroughClassification.NotApplicable,
			["get-entity-schema-column-properties"] = PassthroughClassification.NotApplicable,
			["get-entity-schema-properties"] = PassthroughClassification.NotApplicable,
			["set-entity-schema-properties"] = PassthroughClassification.NotApplicable,
			["get-fsm-mode"] = PassthroughClassification.NotApplicable,
			["get-identity-assertion"] = PassthroughClassification.NotApplicable,
			["get-identity-public-jwk"] = PassthroughClassification.NotApplicable,
			["get-identity-service-config"] = PassthroughClassification.NotApplicable,
			["get-page"] = PassthroughClassification.NotApplicable,
			["get-page-hierarchy"] = PassthroughClassification.NotApplicable,
			["get-process-signature"] = PassthroughClassification.NotApplicable,
			["get-related-page-addon"] = PassthroughClassification.NotApplicable,
			["get-schema"] = PassthroughClassification.NotApplicable,
			["get-schema-name-prefix"] = PassthroughClassification.NotApplicable,
			["get-sql-schema"] = PassthroughClassification.NotApplicable,
			["get-sys-setting"] = PassthroughClassification.NotApplicable,
			["install-application"] = PassthroughClassification.NotApplicable,
			["install-gate"] = PassthroughClassification.NotApplicable,
			["install-sql-schema"] = PassthroughClassification.NotApplicable,
			["list-packages"] = PassthroughClassification.NotApplicable,
			["list-page-templates"] = PassthroughClassification.NotApplicable,
			["list-pages"] = PassthroughClassification.NotApplicable,
			["list-sys-settings"] = PassthroughClassification.NotApplicable,
			["list-themes"] = PassthroughClassification.NotApplicable,
			["list-user-tasks"] = PassthroughClassification.NotApplicable,
			["modify-business-process"] = PassthroughClassification.NotApplicable,
			["modify-entity-schema-column"] = PassthroughClassification.NotApplicable,
			["modify-user-task-parameters"] = PassthroughClassification.NotApplicable,
			["new-integration-test-project"] = PassthroughClassification.NotApplicable,
			["new-test-project"] = PassthroughClassification.NotApplicable,
			["new-ui-project"] = PassthroughClassification.NotApplicable,
			["odata-create"] = PassthroughClassification.NotApplicable,
			["odata-delete"] = PassthroughClassification.NotApplicable,
			["odata-read"] = PassthroughClassification.NotApplicable,
			["odata-update"] = PassthroughClassification.NotApplicable,
			["pkg-to-db"] = PassthroughClassification.NotApplicable,
			["pkg-to-file-system"] = PassthroughClassification.NotApplicable,
			["push-workspace"] = PassthroughClassification.NotApplicable,
			["read-entity-business-rules"] = PassthroughClassification.NotApplicable,
			["read-page-business-rules"] = PassthroughClassification.NotApplicable,
			["regenerate-identity-signing-key"] = PassthroughClassification.NotApplicable,
			["remove-data-binding-row-db"] = PassthroughClassification.NotApplicable,
			["remove-package-dependency"] = PassthroughClassification.NotApplicable,
			["resolve-oauth-system-user"] = PassthroughClassification.NotApplicable,
			["restart-by-credentials"] = PassthroughClassification.NotApplicable,
			["restart-by-environment-name"] = PassthroughClassification.NotApplicable,
			["restore-db-by-credentials"] = PassthroughClassification.NotApplicable,
			["restore-db-by-environment"] = PassthroughClassification.NotApplicable,
			["restore-db-to-local-server"] = PassthroughClassification.NotApplicable,
			["restore-workspace"] = PassthroughClassification.NotApplicable,
			["set-fsm-mode"] = PassthroughClassification.NotApplicable,
			["set-user-theme"] = PassthroughClassification.NotApplicable, // BaseTool ExecuteResolved<SetUserThemeCommand> per-call env resolution (ENG-93302) — already passthrough-capable, like create-theme/list-themes
			["start-creatio"] = PassthroughClassification.NotApplicable,
			["stop-all-creatio"] = PassthroughClassification.NotApplicable,
			["stop-creatio"] = PassthroughClassification.NotApplicable,
			["sync-schemas"] = PassthroughClassification.NotApplicable,
			["unlock-for-hotfix"] = PassthroughClassification.NotApplicable,
			["update-client-unit-schema"] = PassthroughClassification.NotApplicable,
			["update-entity-business-rules"] = PassthroughClassification.NotApplicable,
			["update-entity-schema"] = PassthroughClassification.NotApplicable,
			["update-page-business-rules"] = PassthroughClassification.NotApplicable,
			["update-schema"] = PassthroughClassification.NotApplicable,
			["update-sql-schema"] = PassthroughClassification.NotApplicable,
			["update-sys-setting"] = PassthroughClassification.NotApplicable,
			["update-theme"] = PassthroughClassification.NotApplicable,
			["upsert-data-binding-row-db"] = PassthroughClassification.NotApplicable,
			["validate-page"] = PassthroughClassification.NotApplicable,
			["validate-process-graph"] = PassthroughClassification.NotApplicable,
			["verify-oauth-app"] = PassthroughClassification.NotApplicable
		};

	/// <summary>
	/// The HAND-AUTHORED, audited required (path, scenario) set for every
	/// <see cref="PassthroughClassification.Routed"/>/<see cref="PassthroughClassification.Guarded"/> tool.
	/// See the type-level remarks for the nested-path and <c>update-page</c> write-path scope decisions.
	/// </summary>
	internal static readonly IReadOnlyDictionary<string, IReadOnlyList<PassthroughRequiredPath>> RequiredCoverage =
		new Dictionary<string, IReadOnlyList<PassthroughRequiredPath>>(System.StringComparer.Ordinal) {
			["list-apps"] = [Entry("outer")],
			["get-app-info"] = [Entry("outer")],
			["create-app"] = [
				Entry("outer"),
				HeaderOnlyPath("caption-culture-readback"),
				HeaderOnlyPath("app-info-polling")
			],
			["create-app-section"] = [
				Entry("outer"),
				HeaderOnlyPath("caption-culture-readback"),
				HeaderOnlyPath("caption-culture-validation"),
				HeaderOnlyPath("app-info-validation"),
				HeaderOnlyPath("app-info-polling")
			],
			["update-app-section"] = [
				Entry("outer"),
				HeaderOnlyPath("caption-culture-readback"),
				HeaderOnlyPath("app-info-validation")
			],
			["delete-app-section"] = [
				Entry("outer"),
				HeaderOnlyPath("find-application-id")
			],
			["list-app-sections"] = [
				Entry("outer"),
				HeaderOnlyPath("find-application-id")
			],
			["get-user-culture"] = [Entry("outer")],
			["update-page"] = [Entry("version-probe")], // "write" intentionally absent — see type remarks
			["sync-pages"] = [Entry("version-probe")],
			["get-component-info"] = [Entry("outer")],
			["build-theme"] = [Entry("version")],

			["link-from-repository-by-environment"] = [GuardedEntry("by-environment")],
			["link-from-repository-by-env-package-path"] = [GuardedEntry("env-package-path-preparation")],
			["link-from-repository-unlocked"] = [GuardedEntry("unlocked")]
		};

	/// <summary>An audited OUTER/entry path: requires the full Routed scenario matrix.</summary>
	private static PassthroughRequiredPath Entry(string dependencyPath) =>
		new(dependencyPath, [PassthroughScenario.HeaderOnly, PassthroughScenario.MixedInput, PassthroughScenario.RegisteredEnvStdio]);

	/// <summary>An audited NESTED sub-call path: requires HeaderOnly only (see type remarks).</summary>
	private static PassthroughRequiredPath HeaderOnlyPath(string dependencyPath) =>
		new(dependencyPath, [PassthroughScenario.HeaderOnly]);

	/// <summary>A Guarded (fail-fast) entry path: HeaderOnly maps to the guard-rejection test,
	/// RegisteredEnvStdio to the unchanged-behavior test (AC-03 parenthetical).</summary>
	private static PassthroughRequiredPath GuardedEntry(string dependencyPath) =>
		new(dependencyPath, [PassthroughScenario.HeaderOnly, PassthroughScenario.RegisteredEnvStdio]);

	/// <summary>
	/// Every (tool, dependency-path, scenario) → test-method row for the 15 tools touched in Stories 1-14,
	/// primarily pointing at the Stories 1-14 UNIT fixtures (the reflection-verifiable, in-process source),
	/// supplemented with the Story 15 E2E rows that use an enum-compatible scenario (Story 15's own richer
	/// taxonomy — <c>TwoTenantIsolation</c>, <c>RegisteredEnvHttp</c>, <c>NonSerialization</c> — has no
	/// <see cref="PassthroughScenario"/> equivalent and is therefore not represented here; those extra E2E
	/// proofs exist and passed per Story 15's Dev Agent Record but are out of this fixed vocabulary).
	/// Extra/duplicate rows for the same tuple (e.g. a Unit AND an E2E row for the same RegisteredEnvStdio
	/// tuple) are harmless — the mapping-presence test only requires AT LEAST ONE valid row per required
	/// tuple.
	/// </summary>
	internal static readonly IReadOnlyList<PassthroughCoverageEntry> Coverage = [
		// --- list-apps (outer) ---
		new("list-apps", "outer", PassthroughScenario.HeaderOnly, typeof(ApplicationGetListToolPassthroughTests),
			nameof(ApplicationGetListToolPassthroughTests.ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
		new("list-apps", "outer", PassthroughScenario.MixedInput, typeof(ApplicationGetListToolPassthroughTests),
			nameof(ApplicationGetListToolPassthroughTests.ApplicationGetList_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent)),
		new("list-apps", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(ApplicationGetListToolPassthroughTests),
			nameof(ApplicationGetListToolPassthroughTests.ApplicationGetList_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
		new("list-apps", "outer", PassthroughScenario.HeaderOnly, typeof(McpHttpMultiTenantE2ETests),
			nameof(McpHttpMultiTenantE2ETests.ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
		new("list-apps", "outer", PassthroughScenario.MixedInput, typeof(McpHttpMultiTenantE2ETests),
			nameof(McpHttpMultiTenantE2ETests.ApplicationGetList_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied)),
		new("list-apps", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- get-app-info (outer) ---
		new("get-app-info", "outer", PassthroughScenario.HeaderOnly, typeof(ApplicationGetInfoToolPassthroughTests),
			nameof(ApplicationGetInfoToolPassthroughTests.ApplicationGetInfo_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
		new("get-app-info", "outer", PassthroughScenario.MixedInput, typeof(ApplicationGetInfoToolPassthroughTests),
			nameof(ApplicationGetInfoToolPassthroughTests.ApplicationGetInfo_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent)),
		new("get-app-info", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(ApplicationGetInfoToolPassthroughTests),
			nameof(ApplicationGetInfoToolPassthroughTests.ApplicationGetInfo_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
		new("get-app-info", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- create-app (outer, caption-culture-readback, app-info-polling) ---
		new("create-app", "outer", PassthroughScenario.HeaderOnly, typeof(ApplicationCreateToolPassthroughTests),
			nameof(ApplicationCreateToolPassthroughTests.ApplicationCreate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
		new("create-app", "outer", PassthroughScenario.MixedInput, typeof(ApplicationCreateToolPassthroughTests),
			nameof(ApplicationCreateToolPassthroughTests.ApplicationCreate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent)),
		new("create-app", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(ApplicationCreateToolPassthroughTests),
			nameof(ApplicationCreateToolPassthroughTests.ApplicationCreate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
		new("create-app", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),
		new("create-app", "caption-culture-readback", PassthroughScenario.HeaderOnly, typeof(ApplicationCreateToolPassthroughTests),
			nameof(ApplicationCreateToolPassthroughTests.ApplicationCreate_ShouldResolveCaptionCultureAgainstHeaderTenant_WhenHeaderOnly)),
		new("create-app", "app-info-polling", PassthroughScenario.HeaderOnly, typeof(ApplicationCreateToolPassthroughTests),
			nameof(ApplicationCreateToolPassthroughTests.ApplicationCreate_ShouldPollHeaderTenantForReadback_WhenCreateAppTimesOut)),

		// --- create-app-section (outer + 4 nested paths) ---
		new("create-app-section", "outer", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionCreateToolPassthroughTests),
			nameof(ApplicationSectionCreateToolPassthroughTests.ApplicationSectionCreate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
		new("create-app-section", "outer", PassthroughScenario.MixedInput, typeof(ApplicationSectionCreateToolPassthroughTests),
			nameof(ApplicationSectionCreateToolPassthroughTests.ApplicationSectionCreate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent)),
		new("create-app-section", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(ApplicationSectionCreateToolPassthroughTests),
			nameof(ApplicationSectionCreateToolPassthroughTests.ApplicationSectionCreate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
		new("create-app-section", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),
		new("create-app-section", "outer", PassthroughScenario.MixedInput, typeof(McpHttpMultiTenantE2ETests),
			nameof(McpHttpMultiTenantE2ETests.ApplicationSectionCreate_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied)),
		new("create-app-section", "caption-culture-readback", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionCreateToolPassthroughTests),
			nameof(ApplicationSectionCreateToolPassthroughTests.ApplicationSectionCreate_ShouldResolveReadbackCaptionCultureAgainstHeaderTenant_WhenHeaderOnly)),
		new("create-app-section", "caption-culture-readback", PassthroughScenario.HeaderOnly, typeof(McpHttpMultiTenantE2ETests),
			nameof(McpHttpMultiTenantE2ETests.ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenHeaderOnly)),
		new("create-app-section", "caption-culture-validation", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionCreateToolPassthroughTests),
			nameof(ApplicationSectionCreateToolPassthroughTests.ApplicationSectionCreate_ShouldResolveProfileValidationCaptionCultureAgainstHeaderTenant_WhenHeaderOnly)),
		new("create-app-section", "app-info-validation", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionCreateToolPassthroughTests),
			nameof(ApplicationSectionCreateToolPassthroughTests.ApplicationSectionCreate_ShouldLoadValidationApplicationInfoAgainstHeaderTenant_WhenHeaderOnly)),
		new("create-app-section", "app-info-polling", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionCreateToolPassthroughTests),
			nameof(ApplicationSectionCreateToolPassthroughTests.ApplicationSectionCreate_ShouldPollApplicationInfoAgainstHeaderTenant_WhenHeaderOnly)),

		// --- update-app-section (outer + 2 nested paths) ---
		new("update-app-section", "outer", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionUpdateToolPassthroughTests),
			nameof(ApplicationSectionUpdateToolPassthroughTests.ApplicationSectionUpdate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
		new("update-app-section", "outer", PassthroughScenario.MixedInput, typeof(ApplicationSectionUpdateToolPassthroughTests),
			nameof(ApplicationSectionUpdateToolPassthroughTests.ApplicationSectionUpdate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent)),
		new("update-app-section", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(ApplicationSectionUpdateToolPassthroughTests),
			nameof(ApplicationSectionUpdateToolPassthroughTests.ApplicationSectionUpdate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
		new("update-app-section", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),
		new("update-app-section", "caption-culture-readback", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionUpdateToolPassthroughTests),
			nameof(ApplicationSectionUpdateToolPassthroughTests.ApplicationSectionUpdate_ShouldResolveCaptionCultureAgainstHeaderTenant_WhenHeaderOnly)),
		new("update-app-section", "app-info-validation", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionUpdateToolPassthroughTests),
			nameof(ApplicationSectionUpdateToolPassthroughTests.ApplicationSectionUpdate_ShouldLoadApplicationInfoAgainstHeaderTenant_WhenHeaderOnly)),

		// --- delete-app-section (outer + find-application-id) ---
		new("delete-app-section", "outer", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionDeleteToolPassthroughTests),
			nameof(ApplicationSectionDeleteToolPassthroughTests.ApplicationSectionDelete_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
		new("delete-app-section", "outer", PassthroughScenario.MixedInput, typeof(ApplicationSectionDeleteToolPassthroughTests),
			nameof(ApplicationSectionDeleteToolPassthroughTests.ApplicationSectionDelete_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent)),
		new("delete-app-section", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(ApplicationSectionDeleteToolPassthroughTests),
			nameof(ApplicationSectionDeleteToolPassthroughTests.ApplicationSectionDelete_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
		new("delete-app-section", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),
		new("delete-app-section", "find-application-id", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionDeleteToolPassthroughTests),
			nameof(ApplicationSectionDeleteToolPassthroughTests.ApplicationSectionDelete_ShouldResolveApplicationIdAgainstHeaderTenant_WhenHeaderOnly)),

		// --- list-app-sections (outer + find-application-id) ---
		new("list-app-sections", "outer", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionGetListToolPassthroughTests),
			nameof(ApplicationSectionGetListToolPassthroughTests.ApplicationSectionGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
		new("list-app-sections", "outer", PassthroughScenario.MixedInput, typeof(ApplicationSectionGetListToolPassthroughTests),
			nameof(ApplicationSectionGetListToolPassthroughTests.ApplicationSectionGetList_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent)),
		new("list-app-sections", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(ApplicationSectionGetListToolPassthroughTests),
			nameof(ApplicationSectionGetListToolPassthroughTests.ApplicationSectionGetList_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
		new("list-app-sections", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),
		new("list-app-sections", "find-application-id", PassthroughScenario.HeaderOnly, typeof(ApplicationSectionGetListToolPassthroughTests),
			nameof(ApplicationSectionGetListToolPassthroughTests.ApplicationSectionGetList_ShouldResolveApplicationIdAgainstHeaderTenant_WhenHeaderOnly)),

		// --- get-user-culture (outer) ---
		new("get-user-culture", "outer", PassthroughScenario.HeaderOnly, typeof(GetUserCultureToolPassthroughTests),
			nameof(GetUserCultureToolPassthroughTests.GetUserCulture_ShouldResolveHeaderTenant_WhenHeaderOnly)),
		new("get-user-culture", "outer", PassthroughScenario.MixedInput, typeof(GetUserCultureToolPassthroughTests),
			nameof(GetUserCultureToolPassthroughTests.GetUserCulture_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothPresent)),
		new("get-user-culture", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(GetUserCultureToolPassthroughTests),
			nameof(GetUserCultureToolPassthroughTests.GetUserCulture_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
		new("get-user-culture", "outer", PassthroughScenario.HeaderOnly, typeof(McpHttpMultiTenantE2ETests),
			nameof(McpHttpMultiTenantE2ETests.GetUserCulture_ShouldResolveHeaderTenant_WhenHeaderOnly)),
		new("get-user-culture", "outer", PassthroughScenario.MixedInput, typeof(McpHttpMultiTenantE2ETests),
			nameof(McpHttpMultiTenantE2ETests.GetUserCulture_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied)),
		new("get-user-culture", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- update-page (version-probe only — "write" is a pre-existing compliant path, see type remarks) ---
		new("update-page", "version-probe", PassthroughScenario.HeaderOnly, typeof(PageUpdateToolTests),
			nameof(PageUpdateToolTests.UpdatePage_ShouldResolveVersionAgainstHeaderTenant_WhenHeaderOnly)),
		new("update-page", "version-probe", PassthroughScenario.MixedInput, typeof(PageUpdateToolTests),
			nameof(PageUpdateToolTests.UpdatePage_ShouldRejectProbeBeforeNamedTenantLookup_WhenMixedHeaderAndEnvironmentName)),
		new("update-page", "version-probe", PassthroughScenario.RegisteredEnvStdio, typeof(PageUpdateToolTests),
			nameof(PageUpdateToolTests.UpdatePage_ShouldResolveVersionAgainstRegisteredEnvironment_WhenEnvironmentNameSupplied)),
		new("update-page", "version-probe", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- sync-pages (version-probe) ---
		new("sync-pages", "version-probe", PassthroughScenario.HeaderOnly, typeof(PageSyncToolTests),
			nameof(PageSyncToolTests.SyncPages_ShouldReachVersionProbeThroughResolver_WhenEnvironmentNameIsBlank)),
		new("sync-pages", "version-probe", PassthroughScenario.MixedInput, typeof(PageSyncToolTests),
			nameof(PageSyncToolTests.SyncPages_ShouldFailSoftWithoutBypassingResolver_WhenVersionProbeResolverRejectsMixedInput)),
		new("sync-pages", "version-probe", PassthroughScenario.RegisteredEnvStdio, typeof(PageSyncToolTests),
			nameof(PageSyncToolTests.SyncPages_ShouldScopeChartCatalogToResolvedVersion_WhenEnvironmentResolvesVersion)),
		new("sync-pages", "version-probe", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- get-component-info (outer) ---
		new("get-component-info", "outer", PassthroughScenario.HeaderOnly, typeof(ComponentInfoToolTests),
			nameof(ComponentInfoToolTests.ComponentInfoTool_Should_NeverCallCommandResolver_WhenHeaderOnly)),
		new("get-component-info", "outer", PassthroughScenario.MixedInput, typeof(ComponentInfoToolTests),
			nameof(ComponentInfoToolTests.ComponentInfoTool_Should_RejectMixedInput_BeforeNamedTenantProbe)),
		new("get-component-info", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(ComponentInfoToolTests),
			nameof(ComponentInfoToolTests.ComponentInfoTool_Should_Resolve_Version_From_Passed_Environment)),
		new("get-component-info", "outer", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- build-theme (version) ---
		new("build-theme", "version", PassthroughScenario.HeaderOnly, typeof(BuildThemeToolTests),
			nameof(BuildThemeToolTests.BuildTheme_ShouldResolveVersionAgainstResolverSettings_WhenHeaderOnlyWithCommandResolver)),
		new("build-theme", "version", PassthroughScenario.MixedInput, typeof(BuildThemeToolTests),
			nameof(BuildThemeToolTests.BuildTheme_ShouldFailSoftToLatestFallbackWithNoNamedTenantProbe_WhenResolverThrowsForMixedInput)),
		new("build-theme", "version", PassthroughScenario.RegisteredEnvStdio, typeof(BuildThemeToolTests),
			nameof(BuildThemeToolTests.BuildTheme_ShouldResolveVersionFromEnvironment_WhenEnvironmentNameProvided)),
		new("build-theme", "version", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- link-from-repository-by-environment (by-environment) ---
		new("link-from-repository-by-environment", "by-environment", PassthroughScenario.HeaderOnly, typeof(LinkFromRepositoryToolPassthroughTests),
			nameof(LinkFromRepositoryToolPassthroughTests.LinkFromRepositoryByEnvironment_ShouldReturnUniformRejection_WhenPassthroughActiveWithoutEnvironmentName)),
		new("link-from-repository-by-environment", "by-environment", PassthroughScenario.MixedInput, typeof(LinkFromRepositoryToolPassthroughTests),
			nameof(LinkFromRepositoryToolPassthroughTests.LinkFromRepositoryByEnvironment_ShouldRejectBeforeExecution_WhenPassthroughActiveWithExplicitEnvironmentName)),
		new("link-from-repository-by-environment", "by-environment", PassthroughScenario.RegisteredEnvStdio, typeof(LinkFromRepositoryToolPassthroughTests),
			nameof(LinkFromRepositoryToolPassthroughTests.LinkFromRepositoryByEnvironment_ShouldExecuteUnchanged_WhenNotPassthroughAndEnvironmentNameSupplied)),
		new("link-from-repository-by-environment", "by-environment", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- link-from-repository-by-env-package-path (env-package-path-preparation) ---
		// NOTE: no Unit fixture proves the RegisteredEnvStdio/unchanged scenario for this branch specifically
		// (Story 1 covers skip-preparation=true/false but not a dedicated "unchanged, registered env, guard
		// inactive" case) — Story 15's E2E row is the ONLY row for that tuple, which is exactly why this
		// registry deliberately also draws from Story 15, not only Stories 1-14.
		new("link-from-repository-by-env-package-path", "env-package-path-preparation", PassthroughScenario.HeaderOnly, typeof(LinkFromRepositoryToolPassthroughTests),
			nameof(LinkFromRepositoryToolPassthroughTests.LinkFromRepositoryByEnvPackagePath_ShouldReturnUniformRejection_WhenPassthroughActiveAndSkipPreparationAbsent)),
		new("link-from-repository-by-env-package-path", "env-package-path-preparation", PassthroughScenario.MixedInput, typeof(LinkFromRepositoryToolPassthroughTests),
			nameof(LinkFromRepositoryToolPassthroughTests.LinkFromRepositoryByEnvPackagePath_ShouldRejectBeforeExecution_WhenPassthroughActiveAndSkipPreparationFalse)),
		new("link-from-repository-by-env-package-path", "env-package-path-preparation", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused)),

		// --- link-from-repository-unlocked (unlocked) ---
		new("link-from-repository-unlocked", "unlocked", PassthroughScenario.HeaderOnly, typeof(LinkFromRepositoryToolPassthroughTests),
			nameof(LinkFromRepositoryToolPassthroughTests.LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenPassthroughActiveWithoutEnvironmentName)),
		new("link-from-repository-unlocked", "unlocked", PassthroughScenario.MixedInput, typeof(LinkFromRepositoryToolPassthroughTests),
			nameof(LinkFromRepositoryToolPassthroughTests.LinkFromRepositoryUnlocked_ShouldRejectBeforeExecution_WhenPassthroughActiveWithExplicitEnvironmentName)),
		new("link-from-repository-unlocked", "unlocked", PassthroughScenario.RegisteredEnvStdio, typeof(LinkFromRepositoryToolPassthroughTests),
			nameof(LinkFromRepositoryToolPassthroughTests.LinkFromRepositoryUnlocked_ShouldExecuteUnchanged_WhenGuardNotWiredAndEnvironmentNameSupplied)),
		new("link-from-repository-unlocked", "unlocked", PassthroughScenario.HeaderOnly, typeof(McpHttpMultiTenantE2ETests),
			nameof(McpHttpMultiTenantE2ETests.LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenPassthroughActive)),
		new("link-from-repository-unlocked", "unlocked", PassthroughScenario.MixedInput, typeof(McpHttpMultiTenantE2ETests),
			nameof(McpHttpMultiTenantE2ETests.LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenHeaderAndEnvironmentNameBothSupplied)),
		new("link-from-repository-unlocked", "unlocked", PassthroughScenario.RegisteredEnvStdio, typeof(McpHttpNoRegressionE2ETests),
			nameof(McpHttpNoRegressionE2ETests.Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused))
	];
}
