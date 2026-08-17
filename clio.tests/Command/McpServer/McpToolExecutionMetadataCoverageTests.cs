using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Catalog coverage guard for the six reflected execution-metadata fields (ENG-95262 Stage 1,
/// TC-U-101 … TC-U-108). It answers one question the routing work cannot proceed without: does every
/// enabled canonical MCP tool declare WHERE and HOW it executes, and are the declarations internally
/// consistent with each other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the allowlist exists.</b> Stage 1 lands the attribute, its reader and this test; the ~189
/// individual tool methods are annotated by a LATER wave, because several agents are editing tool files
/// concurrently. A coverage assertion over the live catalog would therefore fail on every tool today. The
/// gate is <see cref="NotYetClassifiedTools"/> — an explicit, reviewed list of the tools not yet
/// annotated — and it is deliberately NOT a skip/ignore: the suite stays green now and the assertions get
/// stronger with every name removed. Four rules keep the list from becoming a rug to sweep failures under:
/// </para>
/// <list type="number">
/// <item><description>A tool with NO attribute fails unless it is named in the list, so a NEW tool cannot ship unclassified.</description></item>
/// <item><description>A tool WITH the attribute must declare all six fields — being on the list does not excuse a partial annotation.</description></item>
/// <item><description>A tool WITH the attribute must NOT be on the list, so the list shrinks as the wave lands instead of going stale.</description></item>
/// <item><description>Every name on the list must still resolve to a registered tool, so a rename or removal cannot leave a rotten entry behind.</description></item>
/// </list>
/// <para>
/// The list is generated from this test's own discovery routine, not copied from the inventory: the
/// inventory was measured on <c>3fc50bf99</c> and the live catalog is the authority.
/// </para>
/// <para>
/// Every checking routine is factored out so it can be run against BOTH the production catalog (proving
/// the catalog is covered) and deliberately fabricated synthetic tools (proving the routine has teeth and
/// the green result is not vacuous — TC-U-102, risk R9 in the test plan).
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolExecutionMetadataCoverageTests {

	/// <summary>
	/// The tools that Stage 1 has not annotated yet. The later annotation wave REMOVES a name here as it
	/// adds the attribute; when the list is empty the coverage requirement is unconditional. Do not add a
	/// name to make a failure go away — a new tool is expected to ship with its metadata.
	/// </summary>
	private static readonly IReadOnlySet<string> NotYetClassifiedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
		"StopAllCreatio",
		"add-data-binding-row",
		"add-item-model",
		"add-knowledge-source",
		"add-package",
		"add-package-dependency",
		"advise-theme-palette",
		"assert-infrastructure",
		"build-theme",
		"check-auth-code-flow",
		"check-settings-health",
		"check-theming-access",
		"clear-browser-session",
		"clear-redis-db-by-credentials",
		"clear-redis-db-by-environment",
		"clear-themes-cache",
		"clio-run",
		"clio-run-destructive",
		"compile-creatio",
		"compile-status",
		"configure-knowledge-feedback-policy",
		"create-app",
		"create-app-section",
		"create-business-process",
		"create-client-unit-schema",
		"create-data-binding",
		"create-data-binding-db",
		"create-entity-business-rules",
		"create-entity-schema",
		"create-lookup",
		"create-oauth-technical-user",
		"create-page",
		"create-page-business-rules",
		"create-related-page-addon",
		"create-schema",
		"create-server-to-server-oauth-app",
		"create-sql-schema",
		"create-sys-setting",
		"create-theme",
		"create-user-task",
		"create-workspace",
		"dataforge-context",
		"dataforge-find-lookups",
		"dataforge-find-tables",
		"dataforge-get-relations",
		"dataforge-get-table-columns",
		"dataforge-initialize",
		"dataforge-status",
		"dataforge-update",
		"delete-app",
		"delete-app-section",
		"delete-entity-business-rules",
		"delete-knowledge",
		"delete-page-business-rules",
		"delete-schema",
		"delete-theme",
		"delete-toolkit",
		"deploy-creatio",
		"deploy-identity",
		"describe-business-process",
		"describe-environment",
		"disable-knowledge-source",
		"download-configuration-by-build",
		"download-configuration-by-environment",
		"enable-knowledge-source",
		"execute-esq",
		"experimental",
		"find-app",
		"find-empty-iis-port",
		"find-entity-schema",
		"finish-hotfix",
		"generate-process-model",
		"generate-source-code",
		"get-app-info",
		"get-browser-session",
		"get-classic-page-sources",
		"get-client-unit-schema",
		"get-component-info",
		"get-entity-schema-column-properties",
		"get-entity-schema-properties",
		"get-fsm-mode",
		"get-guidance",
		"get-identity-assertion",
		"get-identity-public-jwk",
		"get-identity-service-config",
		"get-knowledge-feedback-policy",
		"get-mobile-page-conversion-guide",
		"get-page",
		"get-page-hierarchy",
		"get-process-signature",
		"get-record-rights",
		"get-related-page-addon",
		"get-request-info",
		"get-schema",
		"get-schema-name-prefix",
		"get-sql-schema",
		"get-sys-setting",
		"get-target-package",
		"get-telemetry-consent",
		"get-tool-contract",
		"get-user-culture",
		"info-knowledge",
		"install-application",
		"install-gate",
		"install-knowledge",
		"install-process-builder",
		"install-sql-schema",
		"install-toolkit",
		"link-from-repository-by-env-package-path",
		"link-from-repository-by-environment",
		"link-from-repository-unlocked",
		"list-app-sections",
		"list-apps",
		"list-creatio-builds",
		"list-entity-client-schemas",
		"list-environments",
		"list-knowledge-examples",
		"list-knowledge-sources",
		"list-packages",
		"list-page-templates",
		"list-pages",
		"list-printables",
		"list-sys-settings",
		"list-themes",
		"list-user-tasks",
		"modify-business-process",
		"modify-entity-schema-column",
		"modify-user-task-parameters",
		"new-integration-test-project",
		"new-test-project",
		"new-ui-project",
		"odata-create",
		"odata-delete",
		"odata-read",
		"odata-update",
		"pkg-to-db",
		"pkg-to-file-system",
		"push-workspace",
		"read-data-binding-db",
		"read-entity-business-rules",
		"read-page-business-rules",
		"reg-web-app",
		"regenerate-identity-signing-key",
		"remove-data-binding-row",
		"remove-data-binding-row-db",
		"remove-knowledge-source",
		"remove-package-dependency",
		"resolve-oauth-system-user",
		"restart-by-credentials",
		"restart-by-environment-name",
		"restart-status",
		"restore-db-by-credentials",
		"restore-db-by-environment",
		"restore-db-to-local-server",
		"restore-workspace",
		"send-telemetry",
		"set-background-image",
		"set-entity-schema-properties",
		"set-fsm-mode",
		"set-logo",
		"set-record-rights",
		"set-user-theme",
		"show-passing-infrastructure",
		"start-creatio",
		"stop-all-creatio",
		"stop-creatio",
		"sync-pages",
		"sync-schemas",
		"uninstall-creatio",
		"unlock-for-hotfix",
		"update-app-section",
		"update-client-unit-schema",
		"update-entity-business-rules",
		"update-entity-schema",
		"update-knowledge",
		"update-page",
		"update-page-business-rules",
		"update-schema",
		"update-sql-schema",
		"update-sys-setting",
		"update-theme",
		"update-toolkit",
		"upload-image",
		"upsert-data-binding-row-db",
		"validate-page",
		"validate-process-graph",
		"verify-oauth-app",
		"watch-compilation",
		"withdraw-telemetry-consent",
	};

	/// <summary>
	/// The starter → status-poller pairs from the execution-metadata inventory (§5.1) that have BOTH ends.
	/// A poller must reach the very worker its starter is running in, so the two MUST agree on
	/// <c>OperationFamily</c> and <c>Lifetime</c>; a disagreement sends the poll to a different worker (or to
	/// no worker at all) and the operation becomes unreportable.
	/// </summary>
	private static readonly (string Starter, string StatusPoller)[] StarterStatusPairs = [
		("compile-creatio", "compile-status"),
		("restart-by-environment-name", "restart-status")
	];

	/// <summary>
	/// The remaining long-running starters from inventory §5.1. They have NO status poller and no operation
	/// registry, which is why a worker serving them needs a private completion signal (ADR rule 5). They are
	/// pinned here so their disappearance (or the appearance of a poller) is noticed.
	/// </summary>
	private static readonly string[] StartersWithoutStatusPoller = [
		"restart-by-credentials",
		"install-process-builder",
		"create-app-section"
	];

	/// <summary>
	/// Deprecated tool names registered as their OWN <c>[McpServerTool]</c> method that delegates to the
	/// canonical one. Both names run the same code, so their execution metadata must be identical. Pinned
	/// literally because the delegation is expressed only in C# today; the <c>AliasOf</c> property on the
	/// attribute is the machine-readable link the annotation wave should use, and pairs declared that way
	/// are checked in addition to this list.
	/// </summary>
	private static readonly (string Alias, string Canonical)[] MethodLevelAliasPairs = [
		("StopAllCreatio", "stop-all-creatio")
	];

	private static Assembly ProductionAssembly => typeof(McpFeatureToggleFilter).Assembly;

	// Reads the metadata catalog (tool name -> declared metadata, or null when unclassified) through the
	// PRODUCTION reader, over the feature-enabled tool types. The reader owns discovery, so this test's
	// notion of "a tool" cannot drift from the runtime's (same GetAttributedTypes call, same BindingFlags
	// as McpToolInvokerRegistry, including inherited [McpServerTool] methods).
	private static IReadOnlyDictionary<string, McpToolExecutionMetadata> ReadCatalog(Func<Type, bool> isEnabled) {
		return McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull(
			McpFeatureToggleFilter.GetEnabledTypes(
				ProductionAssembly, typeof(McpServerToolTypeAttribute), isEnabled));
	}

	private static IReadOnlyDictionary<string, McpToolExecutionMetadata> ReadEnabledCatalog() {
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		return ReadCatalog(featureToggle.IsEnabled);
	}

	private static McpToolInvokerRegistry BuildRegistryOverFullCatalog() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		return new McpToolInvokerRegistry(
			provider, ProductionAssembly, featureToggle, JsonSerializerOptions.Default);
	}

	#region Checking routines (run against the real catalog AND against synthetic input)

	/// <summary>
	/// The coverage routine. Returns one human-readable failure per problem found; empty means covered.
	/// Factored out so the synthetic-fixture tests can prove it reports what it claims to report.
	/// </summary>
	internal static IReadOnlyList<string> FindCoverageFailures(
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog,
		IReadOnlySet<string> notYetClassified) {
		List<string> failures = [];
		foreach ((string toolName, McpToolExecutionMetadata metadata) in catalog.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
			if (metadata is null) {
				if (!notYetClassified.Contains(toolName)) {
					failures.Add($"Tool '{toolName}' declares no [McpToolExecution] and is not in NotYetClassifiedTools: " +
						"a router cannot decide where it runs.");
				}
				continue;
			}
			if (!metadata.IsFullyClassified) {
				failures.Add($"Tool '{toolName}' declares [McpToolExecution] but leaves " +
					$"{string.Join(", ", metadata.UnspecifiedFieldNames)} unspecified.");
			}
			if (notYetClassified.Contains(toolName)) {
				failures.Add($"Tool '{toolName}' is classified but is still listed in NotYetClassifiedTools; " +
					"remove it from the list so the gate keeps shrinking.");
			}
		}
		foreach (string stale in notYetClassified
			.Where(name => !catalog.ContainsKey(name))
			.OrderBy(name => name, StringComparer.Ordinal)) {
			failures.Add($"NotYetClassifiedTools names '{stale}', which is not a registered tool: the entry is " +
				"stale (renamed or removed) and must be deleted.");
		}
		return failures;
	}

	/// <summary>The cross-field invariant routine (inventory §3) — TC-U-108.</summary>
	internal static IReadOnlyList<string> FindCrossFieldFailures(
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog) {
		return catalog
			.Where(pair => pair.Value is not null)
			.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.SelectMany(pair => pair.Value.CrossFieldViolations.Select(violation => $"Tool '{pair.Key}': {violation}"))
			.ToArray();
	}

	/// <summary>The starter/status agreement routine — TC-U-103.</summary>
	internal static IReadOnlyList<string> FindStarterStatusFailures(
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog,
		IReadOnlyList<(string Starter, string StatusPoller)> pairs) {
		List<string> failures = [];
		foreach ((string starter, string poller) in pairs) {
			if (!catalog.TryGetValue(starter, out McpToolExecutionMetadata starterMetadata)) {
				failures.Add($"Starter '{starter}' is not a registered tool, so its pairing with '{poller}' cannot be checked.");
				continue;
			}
			if (!catalog.TryGetValue(poller, out McpToolExecutionMetadata pollerMetadata)) {
				failures.Add($"Status poller '{poller}' is not a registered tool, so its pairing with '{starter}' cannot be checked.");
				continue;
			}
			if (starterMetadata is null && pollerMetadata is null) {
				continue;
			}
			if (starterMetadata is null || pollerMetadata is null) {
				failures.Add($"Only one of the pair '{starter}' / '{poller}' is classified; annotate both together, " +
					"otherwise the agreement between them is unchecked.");
				continue;
			}
			if (starterMetadata.OperationFamily != pollerMetadata.OperationFamily) {
				failures.Add($"'{starter}' declares OperationFamily = {starterMetadata.OperationFamily} but its status " +
					$"poller '{poller}' declares {pollerMetadata.OperationFamily}: the poll would not reach the worker " +
					"running the operation.");
			}
			if (starterMetadata.Lifetime != pollerMetadata.Lifetime) {
				failures.Add($"'{starter}' declares Lifetime = {starterMetadata.Lifetime} but its status poller " +
					$"'{poller}' declares {pollerMetadata.Lifetime}: one of them would not be served by a sticky worker.");
			}
		}
		return failures;
	}

	/// <summary>The explicit-budget routine for the hint-unbounded tools — TC-U-105.</summary>
	internal static IReadOnlyList<string> FindBudgetPolicyFailures(
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog,
		IReadOnlyCollection<string> hintUnboundedToolNames,
		IReadOnlySet<string> notYetClassified) {
		List<string> failures = [];
		foreach (string toolName in hintUnboundedToolNames.OrderBy(name => name, StringComparer.Ordinal)) {
			if (!catalog.TryGetValue(toolName, out McpToolExecutionMetadata metadata)) {
				failures.Add($"Hint-unbounded tool '{toolName}' is not present in the metadata catalog.");
				continue;
			}
			if (metadata is null) {
				if (!notYetClassified.Contains(toolName)) {
					failures.Add($"Hint-unbounded tool '{toolName}' declares no BudgetPolicy: neither ReadOnly nor " +
						"Destructive is set, so nothing else bounds it today.");
				}
				continue;
			}
			if (metadata.BudgetPolicy == McpToolBudgetPolicy.Unspecified) {
				failures.Add($"Hint-unbounded tool '{toolName}' leaves BudgetPolicy unspecified: it is bounded by " +
					"nothing today, so the budget must be declared explicitly rather than defaulted.");
			}
		}
		return failures;
	}

	/// <summary>The alias/canonical identity routine — TC-U-107.</summary>
	internal static IReadOnlyList<string> FindAliasMetadataFailures(
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog,
		IReadOnlyList<(string Alias, string Canonical)> pairs) {
		List<string> failures = [];
		foreach ((string alias, string canonical) in pairs) {
			if (!catalog.TryGetValue(alias, out McpToolExecutionMetadata aliasMetadata)) {
				failures.Add($"Deprecated alias '{alias}' is not a registered tool, so its metadata cannot be " +
					$"compared with canonical '{canonical}'.");
				continue;
			}
			if (!catalog.TryGetValue(canonical, out McpToolExecutionMetadata canonicalMetadata)) {
				failures.Add($"Canonical tool '{canonical}' is not registered, so alias '{alias}' points nowhere.");
				continue;
			}
			if (aliasMetadata is null && canonicalMetadata is null) {
				continue;
			}
			if (aliasMetadata is null || canonicalMetadata is null) {
				failures.Add($"Only one of '{alias}' / '{canonical}' is classified; the two run the SAME code and must " +
					"be annotated together.");
				continue;
			}
			// AliasOf legitimately differs (only the alias declares it); the six routing fields must not.
			if (aliasMetadata with { AliasOf = null } != canonicalMetadata with { AliasOf = null }) {
				failures.Add($"Deprecated alias '{alias}' and canonical '{canonical}' declare DIFFERENT execution " +
					$"metadata ({aliasMetadata} vs {canonicalMetadata}); they delegate to one body, so one name would " +
					"route to a worker and the other run in-process.");
			}
		}
		return failures;
	}

	// The alias pairs the annotation wave declared through the attribute's AliasOf link, so a pair added
	// that way is checked without editing this fixture.
	private static IReadOnlyList<(string Alias, string Canonical)> DiscoverDeclaredAliasPairs(
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog) {
		return catalog
			.Where(pair => !string.IsNullOrWhiteSpace(pair.Value?.AliasOf))
			.Select(pair => (Alias: pair.Key, Canonical: pair.Value.AliasOf))
			.OrderBy(pair => pair.Alias, StringComparer.Ordinal)
			.ToArray();
	}

	#endregion

	[Test]
	[Category("Unit")]
	[Description("TC-U-101 (AC-01/AC-02): every enabled canonical MCP tool either declares all six execution-metadata " +
		"fields or is named in the reviewed NotYetClassifiedTools gate; a partially annotated tool fails even while " +
		"gated, a classified tool must be removed from the gate, and a gate entry that no longer names a real tool " +
		"fails so the list cannot rot. The gate exists because Stage 1 ships the attribute while a later wave " +
		"annotates the ~189 tool methods — it is not a skip, and it shrinks to empty as the wave lands.")]
	public void EnabledCanonicalTools_ShouldDeclareExecutionMetadata_OrBeNamedInTheGate() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog = ReadEnabledCatalog();

		// Act
		IReadOnlyList<string> failures = FindCoverageFailures(catalog, NotYetClassifiedTools);

		// Assert — anti-vacuity first: an empty catalog would satisfy any of the checks below.
		catalog.Should().NotBeEmpty(
			because: "the coverage assertion is meaningless over an empty tool catalog");
		catalog.Count.Should().BeGreaterThan(150,
			because: "the enabled catalog is ~189 tools; a sudden collapse would mean discovery broke rather than " +
				"that the tools disappeared");
		failures.Should().BeEmpty(
			because: "every enabled canonical tool must declare where and how it executes (or be explicitly gated as " +
				"not-yet-annotated), so routing is decided by declared intent rather than inferred from safety hints");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-102 (AC-03): the coverage routine is NOT vacuous — run over synthetic tool types with an empty " +
		"gate it reports the unclassified tool and the partially annotated tool, and stays silent about the fully " +
		"classified one. This is the test that proves a newly added, unannotated tool would fail TC-U-101.")]
	public void CoverageRoutine_ShouldReportSyntheticUnclassifiedTools_WhenGateIsEmpty() {
		// Arrange — the production reader over synthetic tool types only, so the real catalog is not involved.
		IReadOnlyDictionary<string, McpToolExecutionMetadata> syntheticCatalog =
			McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull([
				typeof(ClassifiedFixtureTool),
				typeof(UnclassifiedFixtureTool),
				typeof(PartiallyClassifiedFixtureTool)
			]);
		IReadOnlySet<string> emptyGate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Act
		IReadOnlyList<string> failures = FindCoverageFailures(syntheticCatalog, emptyGate);

		// Assert
		syntheticCatalog.Keys.Should().BeEquivalentTo(
			new[] { ClassifiedFixtureTool.ToolName, UnclassifiedFixtureTool.ToolName, PartiallyClassifiedFixtureTool.ToolName },
			because: "the synthetic fixtures must actually be discovered, otherwise this proof is itself vacuous");
		failures.Should().HaveCount(2,
			because: "exactly the unclassified and the partially classified fixtures are problems");
		failures.Should().ContainSingle(failure => failure.Contains(UnclassifiedFixtureTool.ToolName, StringComparison.Ordinal),
			because: "a tool with no [McpToolExecution] must be reported — this is the R9 risk the gate could otherwise hide");
		failures.Should().ContainSingle(failure =>
				failure.Contains(PartiallyClassifiedFixtureTool.ToolName, StringComparison.Ordinal)
				&& failure.Contains(nameof(McpToolExecutionMetadata.SharedFileResource), StringComparison.Ordinal),
			because: "a tool that declares the attribute but omits one field must be reported, naming the missing field");
		failures.Should().NotContain(failure => failure.Contains(ClassifiedFixtureTool.ToolName, StringComparison.Ordinal),
			because: "a fully classified tool is not a failure, otherwise the routine would report everything and prove nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-102 (AC-03), gate half: a tool that IS classified but is still listed in the gate is reported, and " +
		"so is a gate entry naming a tool that does not exist — the two rules that stop the gate from silently rotting " +
		"as the annotation wave lands.")]
	public void CoverageRoutine_ShouldReportStaleGateEntries_WhenToolIsClassifiedOrMissing() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> syntheticCatalog =
			McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull([typeof(ClassifiedFixtureTool)]);
		IReadOnlySet<string> staleGate = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			ClassifiedFixtureTool.ToolName,
			"zz-metadata-tool-that-never-existed"
		};

		// Act
		IReadOnlyList<string> failures = FindCoverageFailures(syntheticCatalog, staleGate);

		// Assert
		failures.Should().HaveCount(2,
			because: "both stale-gate rules must fire: the classified-but-listed tool and the name with no tool behind it");
		failures.Should().ContainSingle(failure =>
				failure.Contains(ClassifiedFixtureTool.ToolName, StringComparison.Ordinal)
				&& failure.Contains("still listed", StringComparison.Ordinal),
			because: "the wave must remove a name from the gate when it annotates the tool, so the gate keeps shrinking");
		failures.Should().ContainSingle(failure =>
				failure.Contains("zz-metadata-tool-that-never-existed", StringComparison.Ordinal)
				&& failure.Contains("stale", StringComparison.Ordinal),
			because: "a gate entry for a renamed or removed tool must fail rather than quietly weaken the coverage check");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-103 (AC-04): a long-running starter and its status poller agree on OperationFamily and Lifetime " +
		"over the real catalog, and both ends of every inventory §5.1 pair still exist (a half-annotated pair also fails).")]
	public void StarterAndStatusPoller_ShouldAgree_OverTheRealCatalog() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog = ReadEnabledCatalog();

		// Act
		IReadOnlyList<string> failures = FindStarterStatusFailures(catalog, StarterStatusPairs);

		// Assert
		StarterStatusPairs.Should().NotBeEmpty(because: "an empty pair table would make this test vacuous");
		failures.Should().BeEmpty(
			because: "a status poller must be served by the same sticky worker as its starter, so the two rows must " +
				"never diverge on OperationFamily or Lifetime");
		foreach (string starter in StartersWithoutStatusPoller) {
			catalog.Should().ContainKey(starter,
				because: $"'{starter}' is a long-running starter with no status poller (inventory §5.1) and its worker " +
					"needs a private completion signal; if the name changed, the inventory row is stale");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-103 (AC-04) teeth: synthetic starter/status pairs that disagree on OperationFamily or on Lifetime " +
		"are both reported, proving the agreement check is not passing merely because nothing is annotated yet.")]
	public void StarterStatusRoutine_ShouldReportDisagreement_WhenSyntheticPairDiverges() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> syntheticCatalog =
			McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull([typeof(StarterStatusFixtureTools)]);

		// Act
		IReadOnlyList<string> familyFailures = FindStarterStatusFailures(
			syntheticCatalog, [(StarterStatusFixtureTools.StarterToolName, StarterStatusFixtureTools.WrongFamilyPollerToolName)]);
		IReadOnlyList<string> lifetimeFailures = FindStarterStatusFailures(
			syntheticCatalog, [(StarterStatusFixtureTools.StarterToolName, StarterStatusFixtureTools.WrongLifetimePollerToolName)]);
		IReadOnlyList<string> agreeingFailures = FindStarterStatusFailures(
			syntheticCatalog, [(StarterStatusFixtureTools.StarterToolName, StarterStatusFixtureTools.MatchingPollerToolName)]);

		// Assert
		familyFailures.Should().ContainSingle(failure => failure.Contains("OperationFamily", StringComparison.Ordinal),
			because: "a poller declaring a different family would be routed to a worker that is not running the operation");
		lifetimeFailures.Should().ContainSingle(failure => failure.Contains("Lifetime", StringComparison.Ordinal),
			because: "a per-call poller cannot reach the sticky worker its starter left behind");
		agreeingFailures.Should().BeEmpty(
			because: "an agreeing pair must produce no failure, otherwise the routine would flag everything and prove nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-105 (AC-06): every tool bounded by NOTHING today (neither ReadOnly nor Destructive, so the read " +
		"deadline never admits it) carries an explicit BudgetPolicy or is named in the gate. The set is derived from the " +
		"live registry rather than pinned at 37 names, so it cannot go stale.")]
	public void HintUnboundedTools_ShouldDeclareAnExplicitBudgetPolicy() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();
		string[] hintUnbounded = registry.ToolNames
			.Where(name => !registry.IsReadOnly(name) && !registry.IsDestructive(name))
			.ToArray();
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog = ReadEnabledCatalog();

		// Act
		IReadOnlyList<string> failures = FindBudgetPolicyFailures(catalog, hintUnbounded, NotYetClassifiedTools);

		// Assert — anti-vacuity: the whole point is that this set is large and unbounded today.
		hintUnbounded.Should().NotBeEmpty(
			because: "the hint-unbounded set is the cohort that produced the 1800 s call; an empty set would mean the " +
				"derivation broke, not that the problem went away");
		hintUnbounded.Should().Contain("get-schema",
			because: "get-schema is the canonical hint-unbounded tool from the inventory (§5.4) — its absence would mean " +
				"the derivation no longer selects what the inventory measured");
		failures.Should().BeEmpty(
			because: "nothing bounds these tools today, so the budget has to be declared rather than inferred");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-105 (AC-06) teeth: a synthetic hint-unbounded tool whose BudgetPolicy is left unspecified is " +
		"reported when it is not gated, and a synthetic tool that declares one is not.")]
	public void BudgetPolicyRoutine_ShouldReportMissingBudget_WhenSyntheticToolLeavesItUnspecified() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> syntheticCatalog =
			McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull([
				typeof(ClassifiedFixtureTool),
				typeof(PartiallyClassifiedFixtureTool),
				typeof(UnclassifiedFixtureTool)
			]);
		IReadOnlySet<string> emptyGate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		IReadOnlySet<string> gateWithUnclassified = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			UnclassifiedFixtureTool.ToolName
		};

		// Act
		IReadOnlyList<string> ungatedFailures = FindBudgetPolicyFailures(
			syntheticCatalog,
			[ClassifiedFixtureTool.ToolName, PartiallyClassifiedFixtureTool.ToolName, UnclassifiedFixtureTool.ToolName],
			emptyGate);
		IReadOnlyList<string> gatedFailures = FindBudgetPolicyFailures(
			syntheticCatalog, [UnclassifiedFixtureTool.ToolName], gateWithUnclassified);

		// Act & Assert
		ungatedFailures.Should().ContainSingle(failure => failure.Contains(UnclassifiedFixtureTool.ToolName, StringComparison.Ordinal),
			because: "an unclassified hint-unbounded tool has no declared budget at all and must be reported");
		ungatedFailures.Should().NotContain(failure => failure.Contains(ClassifiedFixtureTool.ToolName, StringComparison.Ordinal),
			because: "a tool that declares ParentKillDefault satisfies the requirement");
		gatedFailures.Should().BeEmpty(
			because: "the gate is what keeps the suite green while the annotation wave is still running");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-106 (AC-07): a feature-disabled tool type is excluded from the coverage REQUIREMENT (its tools are " +
		"absent from the enabled catalog) but stays in the toggle-blind catalog, where the same routine does report it. " +
		"Both halves are asserted, because only the second proves the exclusion is a gate and not a blind spot.")]
	public void FeatureDisabledTools_ShouldBeExcludedFromTheRequirement_ButStayInTheCatalog() {
		// Arrange — disable every tool type that carries a [FeatureToggle], enable the rest.
		bool IsEnabled(Type type) => type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false) is null;
		IReadOnlyDictionary<string, McpToolExecutionMetadata> gatedOffCatalog = ReadCatalog(IsEnabled);
		IReadOnlyDictionary<string, McpToolExecutionMetadata> toggleBlindCatalog =
			McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull(
				McpFeatureToggleFilter.GetAttributedTypes(ProductionAssembly, typeof(McpServerToolTypeAttribute)));
		string[] gatedToolNames = toggleBlindCatalog.Keys
			.Where(name => !gatedOffCatalog.ContainsKey(name))
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();
		// The gate minus the feature-gated names, so the assertions below are about feature gating only and not
		// about the not-yet-annotated allowlist.
		IReadOnlySet<string> gateWithoutFeatureGatedTools = new HashSet<string>(
			NotYetClassifiedTools.Where(name => !gatedToolNames.Contains(name, StringComparer.OrdinalIgnoreCase)),
			StringComparer.OrdinalIgnoreCase);

		// Act
		IReadOnlyList<string> enabledFailures = FindCoverageFailures(gatedOffCatalog, gateWithoutFeatureGatedTools);
		IReadOnlyList<string> blindFailures = FindCoverageFailures(toggleBlindCatalog, gateWithoutFeatureGatedTools);

		// Assert
		gatedToolNames.Should().NotBeEmpty(
			because: "clio ships feature-gated MCP tools (deploy-identity, process-designer, mobile-page-converter, " +
				"watch-compilation), so an empty difference would mean the gate stopped being applied");
		gatedToolNames.Should().Contain("get-mobile-page-conversion-guide",
			because: "the mobile-page-converter tool is feature-gated and is the concrete example this test is built on");
		enabledFailures.Should().BeEmpty(
			because: "a feature-disabled tool is not part of the coverage requirement — it cannot be called, so it has " +
				"no routing decision to make");
		blindFailures.Should().NotBeEmpty(
			because: "the same tools ARE still in the catalog, so the toggle-blind view must report them — otherwise the " +
				"exclusion would be a blind spot rather than a deliberate scope");
		blindFailures.Should().Contain(failure => failure.Contains("get-mobile-page-conversion-guide", StringComparison.Ordinal),
			because: "the feature-gated tool must remain discoverable and classifiable, just not required while gated off");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-107 (AC-08): a deprecated alias and its canonical carry identical execution metadata. Both alias " +
		"mechanisms are covered — the compatibility-catalog aliases (resolved by the reader) and the deprecated names " +
		"registered as their own [McpServerTool] method (StopAllCreatio vs stop-all-creatio), which have no " +
		"machine-readable link other than the attribute's AliasOf property.")]
	public void DeprecatedAliases_ShouldCarryTheSameMetadataAsTheirCanonical() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog = ReadEnabledCatalog();
		IMcpToolCompatibilityCatalog compatibilityCatalog = new McpToolCompatibilityCatalog();
		IMcpToolExecutionMetadataReader reader = new McpToolExecutionMetadataReader(
			ProductionAssembly, compatibilityCatalog);
		IReadOnlyList<(string Alias, string Canonical)> methodLevelPairs =
			[.. MethodLevelAliasPairs, .. DiscoverDeclaredAliasPairs(catalog)];

		// Act
		IReadOnlyList<string> failures = FindAliasMetadataFailures(catalog, methodLevelPairs);

		// Assert — method-level aliases (their own tool methods, so both rows exist in the catalog).
		methodLevelPairs.Should().NotBeEmpty(
			because: "at least the StopAllCreatio / stop-all-creatio pair must be checked, otherwise this test is vacuous");
		failures.Should().BeEmpty(
			because: "an alias and its canonical execute the same body, so divergent metadata would route one name to a " +
				"worker and the other in-process");

		// Assert — compatibility-catalog aliases resolve to the canonical's metadata through the reader.
		McpToolCompatibilityEntry[] clioAliases = compatibilityCatalog.Entries
			.Where(entry => entry.Kind == McpToolCompatibilityKind.DeprecatedAlias
				&& entry.Owner == McpToolSurfaceOwner.Clio)
			.ToArray();
		clioAliases.Should().NotBeEmpty(
			because: "the compatibility catalog carries clio-owned deprecated aliases, so this half must not be vacuous");
		foreach (McpToolCompatibilityEntry entry in clioAliases) {
			foreach (string alias in entry.Aliases) {
				bool aliasFound = reader.TryGetMetadata(alias, innerCommand: null, out McpToolExecutionMetadata viaAlias);
				bool canonicalFound = reader.TryGetMetadata(entry.CanonicalName, innerCommand: null,
					out McpToolExecutionMetadata viaCanonical);
				aliasFound.Should().Be(canonicalFound,
					because: $"'{alias}' is a declared alias of '{entry.CanonicalName}', so the reader must answer the " +
						"same question for both names");
				viaAlias.Should().Be(viaCanonical,
					because: $"routing on the unresolved alias '{alias}' is exactly the miss the reader canonicalises away");
			}
		}
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-107 (AC-08) teeth: synthetic alias pairs that diverge on a routing field are reported, a pair that " +
		"differs ONLY on the AliasOf link is not, and a half-annotated pair is reported too.")]
	public void AliasRoutine_ShouldReportDivergence_WhenSyntheticAliasDiffersFromItsCanonical() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> syntheticCatalog =
			McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull([typeof(AliasFixtureTools)]);

		// Act
		IReadOnlyList<string> divergentFailures = FindAliasMetadataFailures(
			syntheticCatalog, [(AliasFixtureTools.DivergentAliasToolName, AliasFixtureTools.CanonicalToolName)]);
		IReadOnlyList<string> identicalFailures = FindAliasMetadataFailures(
			syntheticCatalog, [(AliasFixtureTools.IdenticalAliasToolName, AliasFixtureTools.CanonicalToolName)]);
		IReadOnlyList<string> halfAnnotatedFailures = FindAliasMetadataFailures(
			syntheticCatalog, [(AliasFixtureTools.UnclassifiedAliasToolName, AliasFixtureTools.CanonicalToolName)]);

		// Assert
		divergentFailures.Should().ContainSingle(failure => failure.Contains("DIFFERENT execution", StringComparison.Ordinal),
			because: "an alias declaring Location = InProcess against a Worker canonical must fail the build");
		identicalFailures.Should().BeEmpty(
			because: "the AliasOf link itself legitimately differs between the two rows and must not count as divergence");
		halfAnnotatedFailures.Should().ContainSingle(failure => failure.Contains("annotated together", StringComparison.Ordinal),
			because: "annotating only one end of an alias pair leaves the agreement unchecked");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-108: the cross-field invariants from the execution-metadata inventory (§3) hold for every " +
		"classified tool — OperationFamily = Deploy implies Worker + TerminalStage, and Location = InProcess implies " +
		"OperationFamily None, Lifetime NotApplicable, BudgetPolicy None. A row that is valid field-by-field but " +
		"internally impossible fails in the build, not in review.")]
	public void ClassifiedTools_ShouldSatisfyTheCrossFieldInvariants() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> catalog = ReadEnabledCatalog();

		// Act
		IReadOnlyList<string> failures = FindCrossFieldFailures(catalog);

		// Assert
		catalog.Should().NotBeEmpty(because: "an empty catalog would satisfy the invariant check vacuously");
		failures.Should().BeEmpty(
			because: "an internally impossible row (a deploy-family tool classified in-process, say) would be a routing " +
				"decision nobody could implement");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-108 teeth: synthetic rows that violate each invariant are reported — a Deploy-family tool " +
		"classified in-process with no budget, and an in-process tool that claims a sticky worker.")]
	public void CrossFieldRoutine_ShouldReportImpossibleRows_WhenSyntheticMetadataContradictsItself() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> syntheticCatalog =
			McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull([
				typeof(ImpossibleRowFixtureTools),
				typeof(ClassifiedFixtureTool)
			]);

		// Act
		IReadOnlyList<string> failures = FindCrossFieldFailures(syntheticCatalog);

		// Assert
		failures.Should().Contain(failure =>
				failure.Contains(ImpossibleRowFixtureTools.InProcessDeployToolName, StringComparison.Ordinal)
				&& failure.Contains("Location = Worker", StringComparison.Ordinal),
			because: "a deploy-family tool must run in a worker — the original deploy-creatio row got this wrong");
		failures.Should().Contain(failure =>
				failure.Contains(ImpossibleRowFixtureTools.InProcessDeployToolName, StringComparison.Ordinal)
				&& failure.Contains("BudgetPolicy = TerminalStage", StringComparison.Ordinal),
			because: "a generic kill of a deploy can leave a half-installed environment, so the terminal-stage budget is " +
				"not optional for that family");
		failures.Should().Contain(failure =>
				failure.Contains(ImpossibleRowFixtureTools.StickyInProcessToolName, StringComparison.Ordinal)
				&& failure.Contains("Lifetime = NotApplicable", StringComparison.Ordinal),
			because: "a tool that never routes to a worker has no worker to outlive the response");
		failures.Should().NotContain(failure => failure.Contains(ClassifiedFixtureTool.ToolName, StringComparison.Ordinal),
			because: "a consistent row must not be reported, otherwise the routine would flag everything and prove nothing");
	}

	#region Synthetic fixtures (test assembly only — the production catalog is untouched)

	// These live in the TEST assembly so the coverage routines can be run against deliberately fabricated
	// input. Names are prefixed zz-metadata- and every method is static, parameterless and string-returning,
	// mirroring the existing McpToolInvokerRegistryTests fixtures, so the other tests that scan this
	// assembly keep behaving exactly as before.

	[McpServerToolType]
	private static class ClassifiedFixtureTool {
		internal const string ToolName = "zz-metadata-classified-tool";

		[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Fully classified synthetic tool.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.PerCall,
			OperationFamily = McpToolOperationFamily.None,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
			RequiresClientRequests = McpToolClientRequests.None,
			SharedFileResource = McpToolSharedFileResource.None)]
		public static string Run() => "classified";
	}

	[McpServerToolType]
	private static class UnclassifiedFixtureTool {
		internal const string ToolName = "zz-metadata-unclassified-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool deliberately carrying no [McpToolExecution].")]
		public static string Run() => "unclassified";
	}

	[McpServerToolType]
	private static class PartiallyClassifiedFixtureTool {
		internal const string ToolName = "zz-metadata-partial-tool";

		// SharedFileResource deliberately omitted: it must read back as Unspecified and fail.
		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool with one execution-metadata field omitted.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.PerCall,
			OperationFamily = McpToolOperationFamily.None,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
			RequiresClientRequests = McpToolClientRequests.None)]
		public static string Run() => "partial";
	}

	[McpServerToolType]
	private static class StarterStatusFixtureTools {
		internal const string StarterToolName = "zz-metadata-starter-tool";
		internal const string MatchingPollerToolName = "zz-metadata-status-matching-tool";
		internal const string WrongFamilyPollerToolName = "zz-metadata-status-wrong-family-tool";
		internal const string WrongLifetimePollerToolName = "zz-metadata-status-wrong-lifetime-tool";

		[McpServerTool(Name = StarterToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Synthetic sticky starter.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.Sticky,
			OperationFamily = McpToolOperationFamily.ConfigurationBuild,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
			RequiresClientRequests = McpToolClientRequests.Progress,
			SharedFileResource = McpToolSharedFileResource.ConfigurationBuild)]
		public static string Start() => "started";

		[McpServerTool(Name = MatchingPollerToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Synthetic status poller that agrees with the starter.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.Sticky,
			OperationFamily = McpToolOperationFamily.ConfigurationBuild,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
			RequiresClientRequests = McpToolClientRequests.None,
			SharedFileResource = McpToolSharedFileResource.ConfigurationBuild)]
		public static string PollMatching() => "polled";

		[McpServerTool(Name = WrongFamilyPollerToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Synthetic status poller declaring the wrong operation family.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.Sticky,
			OperationFamily = McpToolOperationFamily.Restart,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
			RequiresClientRequests = McpToolClientRequests.None,
			SharedFileResource = McpToolSharedFileResource.None)]
		public static string PollWrongFamily() => "polled";

		[McpServerTool(Name = WrongLifetimePollerToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Synthetic status poller declaring the wrong lifetime.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.PerCall,
			OperationFamily = McpToolOperationFamily.ConfigurationBuild,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
			RequiresClientRequests = McpToolClientRequests.None,
			SharedFileResource = McpToolSharedFileResource.ConfigurationBuild)]
		public static string PollWrongLifetime() => "polled";
	}

	[McpServerToolType]
	private static class AliasFixtureTools {
		internal const string CanonicalToolName = "zz-metadata-alias-canonical-tool";
		internal const string IdenticalAliasToolName = "zz-metadata-alias-identical-tool";
		internal const string DivergentAliasToolName = "zz-metadata-alias-divergent-tool";
		internal const string UnclassifiedAliasToolName = "zz-metadata-alias-unclassified-tool";

		[McpServerTool(Name = CanonicalToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Synthetic canonical tool.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.PerCall,
			OperationFamily = McpToolOperationFamily.None,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
			RequiresClientRequests = McpToolClientRequests.None,
			SharedFileResource = McpToolSharedFileResource.None)]
		public static string Canonical() => "canonical";

		[McpServerTool(Name = IdenticalAliasToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Synthetic alias whose six routing fields match its canonical.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.PerCall,
			OperationFamily = McpToolOperationFamily.None,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
			RequiresClientRequests = McpToolClientRequests.None,
			SharedFileResource = McpToolSharedFileResource.None,
			AliasOf = CanonicalToolName)]
		public static string IdenticalAlias() => Canonical();

		[McpServerTool(Name = DivergentAliasToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Synthetic alias that contradicts its canonical.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.InProcess,
			Lifetime = McpToolExecutionLifetime.NotApplicable,
			OperationFamily = McpToolOperationFamily.None,
			BudgetPolicy = McpToolBudgetPolicy.None,
			RequiresClientRequests = McpToolClientRequests.None,
			SharedFileResource = McpToolSharedFileResource.None,
			AliasOf = CanonicalToolName)]
		public static string DivergentAlias() => Canonical();

		[McpServerTool(Name = UnclassifiedAliasToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Synthetic alias left unannotated while its canonical is annotated.")]
		public static string UnclassifiedAlias() => Canonical();
	}

	[McpServerToolType]
	private static class ImpossibleRowFixtureTools {
		internal const string InProcessDeployToolName = "zz-metadata-inprocess-deploy-tool";
		internal const string StickyInProcessToolName = "zz-metadata-sticky-inprocess-tool";

		// Field-by-field valid, internally impossible: the deploy family cannot run in the host process and
		// cannot be bounded by a generic kill. This is the exact shape the original deploy-creatio row had.
		[McpServerTool(Name = InProcessDeployToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Synthetic deploy-family tool classified in-process.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.InProcess,
			Lifetime = McpToolExecutionLifetime.NotApplicable,
			OperationFamily = McpToolOperationFamily.Deploy,
			BudgetPolicy = McpToolBudgetPolicy.None,
			RequiresClientRequests = McpToolClientRequests.Progress,
			SharedFileResource = McpToolSharedFileResource.None)]
		public static string InProcessDeploy() => "impossible";

		[McpServerTool(Name = StickyInProcessToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Synthetic in-process tool claiming a sticky worker.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.InProcess,
			Lifetime = McpToolExecutionLifetime.Sticky,
			OperationFamily = McpToolOperationFamily.None,
			BudgetPolicy = McpToolBudgetPolicy.None,
			RequiresClientRequests = McpToolClientRequests.None,
			SharedFileResource = McpToolSharedFileResource.None)]
		public static string StickyInProcess() => "impossible";
	}

	#endregion
}
