using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Command.McpServer;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 16 (ENG-93347, FR-06/FR-07/OQ-04) guard tests: (1) every currently discovered
/// <c>[McpServerToolType]</c> tool has a <see cref="PassthroughToolClassificationRegistry.Classification"/>
/// row (completeness — AC-02), and (2) every <see cref="PassthroughClassification.Routed"/>/
/// <see cref="PassthroughClassification.Guarded"/> tool has a NAMED, EXISTING, test-attributed method for
/// each required (dependency-path, scenario) tuple (mapping presence — AC-03/AC-04/AC-ERR/AC-06).
/// </summary>
/// <remarks>
/// See <see cref="PassthroughToolClassificationRegistry"/>'s type-level remarks for the documented scope
/// decisions this guard enforces (nested-path scenario scope, <c>update-page</c>'s write-path exclusion,
/// the AC-01/AC-08 "this is not dataflow bypass detection" caveat).
/// </remarks>
[TestFixture]
[Category("Unit")]
public sealed class PassthroughToolClassificationGuardTests {

	// Mirrors McpToolInvokerRegistry.EnumerateToolMethods' BindingFlags exactly (Public + NonPublic +
	// Instance + Static, no DeclaredOnly) so this guard's notion of "discovered tool" matches the real
	// runtime tool set byte-for-byte, not an approximation.
	private const BindingFlags ToolMethodFlags =
		BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	[Test]
	[Description("AC-02: every [McpServerToolType] tool VERB name discovered via McpFeatureToggleFilter.GetAttributedTypes " +
		"(expanded through each type's [McpServerTool(Name=...)] methods, since some tool types — e.g. " +
		"LinkFromRepositoryTool, DataForgeTool, RestartTool — expose more than one verb per type) must equal " +
		"PassthroughToolClassificationRegistry.Classification.Keys EXACTLY. A newly added tool with no row fails this immediately.")]
	public void Completeness_ShouldMatchRegistryKeysExactly_WhenToolsAreDiscovered() {
		// Arrange
		IReadOnlyList<string> discoveredToolNames = DiscoverToolNames();

		// Act
		IReadOnlyList<string> missingFromRegistry = discoveredToolNames
			.Where(name => !PassthroughToolClassificationRegistry.Classification.ContainsKey(name))
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();
		IReadOnlyList<string> registryEntriesNoLongerDiscovered = PassthroughToolClassificationRegistry.Classification.Keys
			.Where(name => !discoveredToolNames.Contains(name, StringComparer.Ordinal))
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		// Assert
		missingFromRegistry.Should().BeEmpty(
			because: "every currently discovered MCP tool must have a PassthroughToolClassificationRegistry.Classification row " +
				"(FR-06 discovery-drift guard) — a newly added tool with no row must fail this test, not ship unclassified");
		registryEntriesNoLongerDiscovered.Should().BeEmpty(
			because: "a registry row for a tool that no longer exists is stale and must be removed so the registry stays an " +
				"accurate mirror of the current tree");
	}

	[Test]
	[Description("AC-03/AC-06: every Routed/Guarded tool's HAND-AUTHORED RequiredCoverage entry (independent of what " +
		"Coverage happens to contain) has, for each required (dependency-path, scenario) tuple, at least one Coverage " +
		"row whose FixtureType.GetMethod(MethodName) resolves to a real method carrying [Test] or [TestCase].")]
	public void MappingPresence_ShouldResolveNamedTestMethod_WhenTupleIsRequiredByRealRegistry() {
		// Arrange — the REAL registry.

		// Act
		IReadOnlyList<string> failures = FindMissingCoverage(
			PassthroughToolClassificationRegistry.Classification,
			PassthroughToolClassificationRegistry.RequiredCoverage,
			PassthroughToolClassificationRegistry.Coverage);

		// Assert
		failures.Should().BeEmpty(
			because: "every required (tool, dependency-path, scenario) tuple for a Routed/Guarded tool must resolve to a " +
				$"named, existing, test-attributed method; failures: {string.Join(" | ", failures)}");
	}

	[Test]
	[Description("AC-03 consistency: every Routed/Guarded classified tool has a RequiredCoverage entry, and every " +
		"RequiredCoverage entry is classified Routed or Guarded — the two maps must not drift apart.")]
	public void RequiredCoverageKeys_ShouldMatchRoutedAndGuardedClassifications_WhenRegistryIsInspected() {
		// Arrange
		IReadOnlyList<string> routedOrGuarded = PassthroughToolClassificationRegistry.Classification
			.Where(pair => pair.Value is PassthroughClassification.Routed or PassthroughClassification.Guarded)
			.Select(pair => pair.Key)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();
		IReadOnlyList<string> requiredCoverageKeys = PassthroughToolClassificationRegistry.RequiredCoverage.Keys
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		// Act / Assert
		requiredCoverageKeys.Should().BeEquivalentTo(routedOrGuarded,
			because: "RequiredCoverage must have an entry for every Routed/Guarded tool, and no entry for anything else " +
				"— otherwise a Routed/Guarded tool could silently carry no coverage requirement at all");
	}

	[Test]
	[Description("AC-05: every tool named in the PRD's literal out-of-scope audit is classified NotApplicable or " +
		"NotEnvironmentSensitive and is absent from RequiredCoverage — proving it does not trip the guard.")]
	public void OutOfScopeTools_ShouldBeExcludedFromRequiredCoverage_WhenClassified() {
		// Arrange — PRD "Out of scope — not environment-sensitive" audit, matched to CURRENT tool names
		// (see PassthroughToolClassificationRegistry.Classification's inline notes for the PRD→actual-name
		// reconciliation, e.g. "install-creatio" → deploy-creatio).
		string[] outOfScopeToolNames = [
			"get-telemetry-consent", "send-telemetry", "withdraw-telemetry-consent",
			"get-guidance", "get-tool-contract",
			"assert-infrastructure", "show-passing-infrastructure", "list-environments", "find-empty-iis-port",
			"list-creatio-builds",
			"advise-theme-palette", "reg-web-app",
			"deploy-creatio", "uninstall-creatio",
			"install-toolkit", "update-toolkit", "delete-toolkit",
			"add-data-binding-row", "remove-data-binding-row",
			"check-settings-health"
		];

		// Act
		Dictionary<string, PassthroughClassification> actualClassifications = outOfScopeToolNames
			.ToDictionary(name => name, name => PassthroughToolClassificationRegistry.Classification[name]);
		IReadOnlyList<string> presentInRequiredCoverage = outOfScopeToolNames
			.Where(name => PassthroughToolClassificationRegistry.RequiredCoverage.ContainsKey(name))
			.ToArray();

		// Assert
		actualClassifications.Values.Should().OnlyContain(
			classification => classification == PassthroughClassification.NotApplicable
				|| classification == PassthroughClassification.NotEnvironmentSensitive,
			because: "every PRD-audited out-of-scope tool must classify as NotApplicable or NotEnvironmentSensitive (AC-05)");
		presentInRequiredCoverage.Should().BeEmpty(
			because: "an out-of-scope tool must never carry a per-path coverage requirement — that would incorrectly " +
				"treat a tool with no Creatio credential involved as an audited passthrough defect");
	}

	[Test]
	[Description("AC-04 (the ADR's rejected 'coarse fixture' alternative): a Coverage row that names a REAL, " +
		"test-attributed method belonging to the WRONG (dependency-path, scenario) tuple does not satisfy the " +
		"required tuple — proving this guard checks the EXACT tuple, not merely 'a test exists somewhere in the fixture'.")]
	public void MappingPresence_ShouldFail_WhenOnlyAnUnrelatedTestCoversTheFixture() {
		// Arrange — a fabricated tool requires HeaderOnly on path "outer", but Coverage only carries a row
		// for the SAME tool/path under MixedInput (pointing at a real, existing, [Test]-attributed method).
		// The ADR's rejected alternative ("a test exists in fixture X") would pass this; the tuple-exact
		// design must not.
		const string fakeTool = "fake-diagnostic-tool";
		Dictionary<string, PassthroughClassification> classification = new(StringComparer.Ordinal) {
			[fakeTool] = PassthroughClassification.Routed
		};
		Dictionary<string, IReadOnlyList<PassthroughRequiredPath>> requiredCoverage = new(StringComparer.Ordinal) {
			[fakeTool] = [new PassthroughRequiredPath("outer", [PassthroughScenario.HeaderOnly])]
		};
		IReadOnlyList<PassthroughCoverageEntry> coverage = [
			new(fakeTool, "outer", PassthroughScenario.MixedInput, typeof(PassthroughToolClassificationGuardTests),
				nameof(SentinelUnrelatedTest))
		];

		// Act
		IReadOnlyList<string> failures = FindMissingCoverage(classification, requiredCoverage, coverage);

		// Assert
		failures.Should().ContainSingle(
			because: "the required HeaderOnly/outer tuple has no matching row, so the guard must fail even though a " +
				"real test exists for the SAME tool under a different scenario");
		string failureMessage = failures.Single();
		failureMessage.Should().Contain(fakeTool, because: "the failure message must name the affected tool");
		failureMessage.Should().Contain("outer", because: "the failure message must name the affected dependency path");
		failureMessage.Should().Contain(nameof(PassthroughScenario.HeaderOnly),
			because: "the failure message must name the affected scenario");
	}

	[Test]
	[Description("AC-ERR: a Coverage row naming a method that does not exist on FixtureType fails the guard with a " +
		"clear message identifying the tool/path/scenario — not a silent pass or an unrelated reflection exception.")]
	public void MappingPresence_ShouldFail_WhenRowNamesNonexistentMethod() {
		// Arrange
		const string fakeTool = "fake-diagnostic-tool";
		Dictionary<string, PassthroughClassification> classification = new(StringComparer.Ordinal) {
			[fakeTool] = PassthroughClassification.Guarded
		};
		Dictionary<string, IReadOnlyList<PassthroughRequiredPath>> requiredCoverage = new(StringComparer.Ordinal) {
			[fakeTool] = [new PassthroughRequiredPath("outer", [PassthroughScenario.HeaderOnly])]
		};
		IReadOnlyList<PassthroughCoverageEntry> coverage = [
			new(fakeTool, "outer", PassthroughScenario.HeaderOnly, typeof(PassthroughToolClassificationGuardTests),
				"Method_That_Does_Not_Exist_On_This_Fixture")
		];

		// Act
		Action act = () => FindMissingCoverage(classification, requiredCoverage, coverage);

		// Assert
		act.Should().NotThrow(
			because: "a nonexistent method name must surface as a reported failure, not an unhandled reflection exception");
		IReadOnlyList<string> failures = FindMissingCoverage(classification, requiredCoverage, coverage);
		failures.Should().ContainSingle(
			because: "exactly one required tuple is affected");
		string failureMessage = failures.Single();
		failureMessage.Should().Contain(fakeTool, because: "the failure message must name the affected tool");
		failureMessage.Should().Contain("outer", because: "the failure message must name the affected dependency path");
		failureMessage.Should().Contain("Method_That_Does_Not_Exist_On_This_Fixture",
			because: "the failure message must name the offending method so a developer can act on it directly");
	}

	[Test]
	[Description("Sentinel used by MappingPresence_ShouldFail_WhenOnlyAnUnrelatedTestCoversTheFixture to prove a " +
		"real, existing, [Test]-attributed method for the WRONG tuple does not satisfy the guard.")]
	public void SentinelUnrelatedTest() {
		// Intentionally empty — only its existence and [Test] attribute matter to the fixture above.
	}

	/// <summary>
	/// Enumerates every currently discovered <c>[McpServerToolType]</c> tool VERB name (one per
	/// <c>[McpServerTool(Name = ...)]</c>-attributed method), mirroring <c>McpToolInvokerRegistry</c>'s own
	/// discovery exactly (same assembly, same <see cref="McpFeatureToggleFilter.GetAttributedTypes"/> call,
	/// same <see cref="ToolMethodFlags"/>) so this guard's notion of "discovered" cannot silently diverge
	/// from what the MCP server itself would register.
	/// </summary>
	private static IReadOnlyList<string> DiscoverToolNames() {
		Assembly assembly = typeof(McpFeatureToggleFilter).Assembly;
		Type[] toolTypes = McpFeatureToggleFilter.GetAttributedTypes(assembly, typeof(McpServerToolTypeAttribute));
		return toolTypes
			.SelectMany(type => type.GetMethods(ToolMethodFlags))
			.Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
			.Where(attribute => attribute is not null && !string.IsNullOrWhiteSpace(attribute.Name))
			.Select(attribute => attribute!.Name!)
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	/// <summary>
	/// The mapping-presence verification routine (OQ-04's second guard test), factored out so it can be
	/// exercised against BOTH the real registry (proving the feature's actual coverage is complete) and
	/// deliberately fabricated inputs (proving the routine itself has teeth — AC-04/AC-ERR).
	/// </summary>
	private static IReadOnlyList<string> FindMissingCoverage(
		IReadOnlyDictionary<string, PassthroughClassification> classification,
		IReadOnlyDictionary<string, IReadOnlyList<PassthroughRequiredPath>> requiredCoverage,
		IReadOnlyList<PassthroughCoverageEntry> coverage) {
		List<string> failures = [];

		foreach ((string toolName, PassthroughClassification value) in classification) {
			if (value is PassthroughClassification.NotEnvironmentSensitive or PassthroughClassification.NotApplicable) {
				continue;
			}
			if (!requiredCoverage.TryGetValue(toolName, out IReadOnlyList<PassthroughRequiredPath>? paths)) {
				failures.Add($"Tool '{toolName}' is classified {value} but has no entry in RequiredCoverage.");
				continue;
			}
			foreach (PassthroughRequiredPath path in paths) {
				foreach (PassthroughScenario scenario in path.RequiredScenarios) {
					PassthroughCoverageEntry? match = coverage.FirstOrDefault(entry =>
						string.Equals(entry.ToolName, toolName, StringComparison.Ordinal)
						&& string.Equals(entry.DependencyPath, path.DependencyPath, StringComparison.Ordinal)
						&& entry.Scenario == scenario);
					if (match is null) {
						failures.Add(
							$"Missing Coverage row for tool '{toolName}', path '{path.DependencyPath}', scenario '{scenario}'.");
						continue;
					}
					MethodInfo? method = match.FixtureType.GetMethod(match.MethodName, ToolMethodFlags);
					if (method is null) {
						failures.Add(
							$"Coverage row for tool '{toolName}', path '{path.DependencyPath}', scenario '{scenario}' " +
							$"names method '{match.MethodName}' which does not exist on {match.FixtureType.FullName}.");
						continue;
					}
					bool isTest = method.GetCustomAttributes<TestAttribute>(inherit: true).Any()
						|| method.GetCustomAttributes<TestCaseAttribute>(inherit: true).Any();
					if (!isTest) {
						failures.Add(
							$"Coverage row for tool '{toolName}', path '{path.DependencyPath}', scenario '{scenario}' " +
							$"names method '{match.MethodName}' on {match.FixtureType.FullName}, which carries neither " +
							"[Test] nor [TestCase].");
					}
				}
			}
		}

		return failures;
	}
}
