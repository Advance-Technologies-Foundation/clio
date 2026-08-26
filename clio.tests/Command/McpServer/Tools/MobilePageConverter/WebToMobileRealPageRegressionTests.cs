namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using FluentAssertions;
using NUnit.Framework;

/// <summary>
/// Regression coverage for the bundled conversion rules against a REAL production page shape, rather than a
/// hand-written fixture that only contains what the rule already expects. The pinned page is the OOTB
/// <c>Leads_FormPage</c> (package <c>CrtOCMInLeadOppMgmt</c>), captured with <c>clio get-page</c>; its five
/// expansion panels are the exact shape ENG-95081 was reported against.
/// <para>
/// This exists because the only other end-to-end check of the <c>excludedComponents</c> rules —
/// <c>MobilePageConversionGuideSandboxE2ETests</c> — depends on a seeded application carrying a banned
/// component in a banned position, and degrades to <see cref="Assert.Ignore(string)"/> when the seed does
/// not. That skip is honest but it means the shipped rule's own acceptance criterion would otherwise never
/// be enforced by a run that always executes. This fixture removes the seed dependency: it is hermetic,
/// runs on every build, and asserts the criterion on production-shaped metadata.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebToMobileRealPageRegressionTests {

	private const string FixtureName = "LeadsFormPage.live-snapshot.json";

	/// <summary>
	/// The mobile types the conversion is run against. Deliberately DERIVED FROM THE PAGE plus an explicit
	/// statement that <c>crt.SearchFilter</c> resolves, rather than read from
	/// <c>MobileComponentRegistry.live-snapshot.json</c>: that snapshot is a separate pin with its own refresh
	/// cadence, and at the time of writing it lags the published catalog (35 entries vs 47, missing
	/// <c>crt.SearchFilter</c>). Binding this test to it would silently invert what the test proves — with
	/// <c>crt.SearchFilter</c> absent from the type set the converter drops it as an unsupported type and the
	/// exclusion rule never runs, so the test would pass while asserting nothing about the rule.
	/// <para>
	/// The condition this set encodes — the banned type IS mobile-supported — is the only condition under
	/// which the rule has any work to do, and it is what the live catalog reports today. When it does not
	/// hold, the acceptance criterion still holds through the unsupported-type drop, which
	/// <see cref="Analyze_ShouldKeepSearchFilterOffTheCanvas_EvenWhenItIsNotAMobileType"/> pins separately.
	/// </para>
	/// </summary>
	private static IReadOnlySet<string> MobileTypesResolvingSearchFilter(JsonNode viewConfig) {
		var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectTypes(viewConfig, types);
		types.Contains("crt.SearchFilter").Should().BeTrue(
			because: "the pinned page must still carry the banned type, or every assertion below is vacuous");
		return types;
	}

	[Test]
	[Description("ENG-95081 on the real page: every crt.SearchFilter on the OOTB Leads_FormPage sits in a crt.ExpansionPanel's tools strip, and the shipped excludedComponents rule drops all five — each reported as a drop entry naming the rule, the host type and the slot.")]
	public void Analyze_ShouldDropEverySearchFilter_OnTheRealLeadsFormPageShape() {
		// Arrange
		JsonObject fixture = LoadFixture();
		JsonNode viewConfig = fixture["viewConfig"]!;
		IReadOnlySet<string> mobileTypes = MobileTypesResolvingSearchFilter(viewConfig);
		int searchFiltersInPanelTools = CountSearchFiltersUnderExpansionPanelTools(viewConfig);
		searchFiltersInPanelTools.Should().Be(5,
			because: "the pinned page shape is the reported one — five panels, each with a search filter in its "
				+ "tools strip; a different count means the fixture was refreshed against a changed page and the "
				+ "expectations below need re-deriving rather than silently relaxing");

		// Act
		MobilePageConversionGuide guide = Convert(fixture, mobileTypes, BundledRules());

		// Assert
		List<ElementMapEntry> searchFilters = SearchFilterEntries(guide);
		searchFilters.Should().HaveCount(5,
			because: "each of the page's five search filters must be accounted for in the element map");
		searchFilters.Should().OnlyContain(e => e.Operation == "drop",
			because: "every one of them is in the position the shipped rule bans, so none may reach the mobile page");
		searchFilters.Should().OnlyContain(e => e.Reason!.Contains("excludedComponents"),
			because: "the removal must be attributed to the RULE — an unsupported-type drop would satisfy the "
				+ "acceptance criterion by accident and hide the rule regressing");
		searchFilters.Should().OnlyContain(e => e.Reason!.Contains("crt.ExpansionPanel") && e.Reason.Contains("tools"),
			because: "the reason names the host and the slot so a reader can trace the drop back to the rules file");
	}

	[Test]
	[Description("The rule is surgical on a real page: removing the excludedComponents section from the rules changes the conversion of the five search filters and NOTHING else — no orphan cascade, no container emptied, no attribute pruned, no request reclassified.")]
	public void Analyze_ShouldChangeNothingBesideTheSearchFilters_OnTheRealLeadsFormPageShape() {
		// Arrange
		JsonObject fixture = LoadFixture();
		IReadOnlySet<string> mobileTypes = MobileTypesResolvingSearchFilter(fixture["viewConfig"]!);

		// Act
		MobilePageConversionGuide withRule = Convert(fixture, mobileTypes, BundledRules());
		MobilePageConversionGuide without = Convert(fixture, mobileTypes, WithoutExcludedComponents(BundledRules()));

		// Assert
		IReadOnlyList<string> changed = OperationDifferences(without, withRule);
		changed.Should().BeEquivalentTo(
			SearchFilterEntries(withRule).Select(e => e.WebName),
			because: "the exclusion must touch the banned components and nothing else on the page");
		withRule.ElementMap.Count.Should().Be(without.ElementMap.Count,
			because: "a removal that cascaded into containers or orphans would change the entry count");
		AttributeCount(withRule).Should().Be(AttributeCount(without),
			because: "the removal is layout cleanup, not attribute cleanup — no attribute may be pruned by it");
		withRule.RequestConversions?.ConvertedRequests?.Count.Should()
			.Be(without.RequestConversions?.ConvertedRequests?.Count,
				because: "none of the removed components carries a binding, so no conversion may be reclassified");
		withRule.RequestConversions?.DroppedRequests?.Count.Should()
			.Be(without.RequestConversions?.DroppedRequests?.Count,
				because: "the request report must be identical when no removed element had a binding");
	}

	[Test]
	[Description("The acceptance criterion survives the other branch too: when crt.SearchFilter is NOT in the mobile type set, the exclusion rule never runs and the pre-existing unsupported-type drop keeps it off the canvas — and it is not left behind as a verbatim-carried node inside the panel either.")]
	public void Analyze_ShouldKeepSearchFilterOffTheCanvas_EvenWhenItIsNotAMobileType() {
		// Arrange
		JsonObject fixture = LoadFixture();
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectTypes(fixture["viewConfig"]!, mobileTypes);
		mobileTypes.Remove("crt.SearchFilter");

		// Act
		MobilePageConversionGuide guide = Convert(fixture, mobileTypes, BundledRules());

		// Assert
		SearchFilterEntries(guide).Should().OnlyContain(e => e.Operation == "drop",
			because: "an unsupported type is dropped by the converter regardless of any exclusion rule");
		guide.ElementMap
			.Where(e => e.MobileValues is not null
				&& e.MobileValues.ToJsonString().Contains("crt.SearchFilter", StringComparison.Ordinal))
			.Should().BeEmpty(
				because: "a dropped component must not survive as a node carried verbatim inside a surviving "
					+ "host's values — that is the shape that would still render it on the canvas");
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────────

	private static JsonObject LoadFixture() {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures", FixtureName);
		return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
	}

	private static WebToMobilePageConversionRules BundledRules() =>
		WebToMobilePageConversionRulesCatalog.LoadBundled();

	/// <summary>The same rules with only the <c>excludedComponents</c> section emptied, for the A/B comparison.</summary>
	private static WebToMobilePageConversionRules WithoutExcludedComponents(WebToMobilePageConversionRules rules) =>
		new() {
			Version = rules.Version,
			Components = rules.Components,
			Requests = rules.Requests,
			Templates = rules.Templates,
			EmptyContainerRemoval = rules.EmptyContainerRemoval,
			TabAreaLayers = rules.TabAreaLayers,
			ComponentPropertyOverrides = rules.ComponentPropertyOverrides,
			NonConvertingScopeContainers = rules.NonConvertingScopeContainers,
			Extensions = rules.Extensions,
			ExcludedComponents = []
		};

	private static MobilePageConversionGuide Convert(
		JsonObject fixture, IReadOnlySet<string> mobileTypes, WebToMobilePageConversionRules rules) {
		// The analysis mutates the bundle it is given, so each run gets its own copy of the pinned page.
		var bundle = new PageBundleInfo {
			ViewConfig = fixture["viewConfig"]!.DeepClone().AsArray(),
			ViewModelConfig = fixture["viewModelConfig"]?.DeepClone().AsObject() ?? new JsonObject(),
			ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo()
		};
		return WebToMobileAnalysisService.Analyze(
			bundle, mobileTypes, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, rules, templateRule: null,
			sourcePage: "Leads_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrLeads_MobileFormPage",
			containerNameMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
	}

	private static List<ElementMapEntry> SearchFilterEntries(MobilePageConversionGuide guide) =>
		guide.ElementMap
			.Where(e => string.Equals(e.WebType, "crt.SearchFilter", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e.MobileType, "crt.SearchFilter", StringComparison.OrdinalIgnoreCase))
			.ToList();

	private static int AttributeCount(MobilePageConversionGuide guide) =>
		guide.ViewModelConfig?["attributes"] is JsonObject attributes ? attributes.Count : -1;

	/// <summary>Web names whose element-map operation differs between the two conversions.</summary>
	private static IReadOnlyList<string> OperationDifferences(
		MobilePageConversionGuide before, MobilePageConversionGuide after) {
		Dictionary<string, string> a = OperationsByWebName(before);
		Dictionary<string, string> b = OperationsByWebName(after);
		return a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase)
			.Where(name => Operation(a, name) != Operation(b, name))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static Dictionary<string, string> OperationsByWebName(MobilePageConversionGuide guide) =>
		guide.ElementMap
			.Where(e => e.WebName is { Length: > 0 })
			.GroupBy(e => e.WebName!, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First().Operation, StringComparer.OrdinalIgnoreCase);

	private static string Operation(Dictionary<string, string> map, string name) =>
		map.TryGetValue(name, out string operation) ? operation : "<absent>";

	/// <summary>Counts <c>crt.SearchFilter</c> nodes sitting anywhere below a <c>crt.ExpansionPanel</c>'s <c>tools</c>.</summary>
	private static int CountSearchFiltersUnderExpansionPanelTools(JsonNode node, bool insidePanelTools = false) {
		switch (node) {
			case JsonArray array:
				return array.Where(item => item is not null)
					.Sum(item => CountSearchFiltersUnderExpansionPanelTools(item!, insidePanelTools));
			case JsonObject obj: {
				bool isPanel = string.Equals(
					obj["type"]?.ToString(), "crt.ExpansionPanel", StringComparison.OrdinalIgnoreCase);
				int found = insidePanelTools && string.Equals(
					obj["type"]?.ToString(), "crt.SearchFilter", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
				foreach (KeyValuePair<string, JsonNode> pair in obj) {
					if (pair.Value is null) {
						continue;
					}
					bool childScope = insidePanelTools
						|| (isPanel && string.Equals(pair.Key, "tools", StringComparison.OrdinalIgnoreCase));
					found += CountSearchFiltersUnderExpansionPanelTools(pair.Value, childScope);
				}
				return found;
			}
			default:
				return 0;
		}
	}

	private static void CollectTypes(JsonNode node, HashSet<string> types) {
		switch (node) {
			case JsonArray array:
				foreach (JsonNode item in array.Where(i => i is not null)) {
					CollectTypes(item!, types);
				}
				break;
			case JsonObject obj:
				if (obj["type"]?.ToString() is { Length: > 0 } type) {
					types.Add(type);
				}
				foreach (KeyValuePair<string, JsonNode> pair in obj.Where(p => p.Value is not null)) {
					CollectTypes(pair.Value!, types);
				}
				break;
		}
	}
}
