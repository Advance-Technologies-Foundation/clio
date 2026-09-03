namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
		List<DroppedElement> searchFilters = DroppedSearchFilters(guide);
		searchFilters.Should().HaveCount(5,
			because: "each of the page's five search filters must be accounted for in droppedElements");
		SurvivingSearchFilters(guide).Should().BeEmpty(
			because: "every one of them is in the position the shipped rule bans, so none may reach the mobile page — an operation for one would put it on the canvas");
		searchFilters.Should().OnlyContain(
			e => e.Reason!.Any(r => r.Code == ReasonCodes.DropExcludedByRule),
			because: "the removal must be attributed to the RULE — an unsupported-type drop would satisfy the "
				+ "acceptance criterion by accident and hide the rule regressing");
		searchFilters.Should().OnlyContain(
			e => e.Reason!.Any(r => r.Code == ReasonCodes.DropExcludedByRule
				&& r.Params != null
				&& r.Params["hostType"]!.GetValue<string>() == "crt.ExpansionPanel"
				&& r.Params["slot"]!.GetValue<string>() == "tools"),
			because: "the reason names the host and the slot as PARAMS so a reader can trace the drop back to "
				+ "the rules file without parsing a sentence");
		VerbatimCarriersOfSearchFilter(guide).Should().BeEmpty(
			because: "the acceptance criterion is about the CANVAS, not the report: a drop entry plus a verbatim "
				+ "copy still inside a surviving host's mobileValues is exactly the shape that kept rendering the "
				+ "search on mobile, so the artifact has to be checked and not only the verdict");
	}

	[Test]
	[Description("The rule is surgical on a real page: removing the excludedComponents section from the rules changes the conversion of the five search filters and NOTHING else — no orphan cascade, no container emptied, no attribute pruned, no request reclassified.")]
	public void Analyze_ShouldChangeNothingBesideTheSearchFilters_OnTheRealLeadsFormPageShape() {
		// Arrange
		JsonObject fixture = LoadFixture();
		IReadOnlySet<string> mobileTypes = MobileTypesResolvingSearchFilter(fixture["viewConfig"]!);

		// Act
		MobilePageConversionGuide withRule = Convert(fixture, mobileTypes, BundledRules());
		MobilePageConversionGuide without = Convert(fixture, mobileTypes, WithoutExcludedComponents());

		// Assert
		IReadOnlyList<string> changed = OperationDifferences(without, withRule);
		changed.Should().BeEquivalentTo(
			DroppedSearchFilters(withRule).Select(e => e.WebName),
			because: "the exclusion must touch the banned components and nothing else on the page");
		(withRule.ElementMap.Count + (withRule.DroppedElements?.Count ?? 0)).Should()
			.Be(without.ElementMap.Count + (without.DroppedElements?.Count ?? 0),
				because: "every source element is still accounted for, in one list or the other — a removal that "
					+ "cascaded into containers or orphans would change the TOTAL, while the split alone only moves "
					+ "the five banned filters from one list to the other");
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
		DroppedSearchFilters(guide).Should().NotBeEmpty(
			because: "an unsupported type is dropped by the converter regardless of any exclusion rule");
		SurvivingSearchFilters(guide).Should().BeEmpty(
			because: "a dropped component must produce no operation at all");
		VerbatimCarriersOfSearchFilter(guide).Should().BeEmpty(
			because: "a dropped component must not survive as a node carried verbatim inside a surviving "
				+ "host's values — that is the shape that would still render it on the canvas");
	}

	[Test]
	[Description("ENG-96153 on the real page: the Next steps tab keeps its header in the web tab's tools strip, and retargeting it into the synthesized Area must re-slot it to items — a crt.GridContainer has no tools collection, so a carried-over slot renders an empty tab.")]
	public void Analyze_ShouldReslotToolsStripChildrenIntoTheAreaItems_OnTheRealLeadsFormPageShape() {
		// Arrange
		JsonObject fixture = LoadFixture();
		JsonNode viewConfig = fixture["viewConfig"]!;
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectTypes(viewConfig, mobileTypes);
		ChildrenOfTabToolsStrips(viewConfig).Should().Contain("NextStepsTabContainerHeaderContainer",
			because: "the pinned page shape is the reported one — the Next steps header sits in the tab's tools "
				+ "strip; without it every assertion below is vacuous and the fixture needs re-deriving");

		// Act
		MobilePageConversionGuide guide = Convert(fixture, mobileTypes, BundledRules());

		// Assert
		ElementMapEntry header = guide.ElementMap
			.Single(e => string.Equals(e.WebName, "NextStepsTabContainerHeaderContainer", StringComparison.Ordinal));
		header.Operation.Should().Be("insert",
			because: "the header container is a crt.FlexContainer, a mobile-supported type that must reach the page");
		header.ParentName.Should().StartWith("GridContainer_",
			because: "the tab-area pass retargets a tab's top-level content onto the synthesized Area card");
		header.PropertyName.Should().Be("items",
			because: "the Area is a crt.GridContainer, whose only child collection is items — keeping the web "
				+ "tab's tools slot makes the differ insert into a slot the component never renders");
	}

	[Test]
	[Description("ENG-96153 as an invariant: every container the tab-area pass synthesizes is a crt.GridContainer, so it may declare no child collection other than items — a stray tools/menuItems array on one is the empty-tab defect.")]
	public void Analyze_ShouldDeclareOnlyItemsOnSynthesizedTabLayers_OnTheRealLeadsFormPageShape() {
		// Arrange
		JsonObject fixture = LoadFixture();
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectTypes(fixture["viewConfig"]!, mobileTypes);

		// Act
		MobilePageConversionGuide guide = Convert(fixture, mobileTypes, BundledRules());

		// Assert
		List<ElementMapEntry> layers = SynthesizedTabLayers(guide);
		layers.Should().NotBeEmpty(
			because: "the pinned page has converted tabs, so the pass must have synthesized their body/Area layers");
		layers.Should().OnlyContain(e => DeclaredChildSlots(e).SequenceEqual(new[] { "items" }),
			because: "a synthesized layer holds its children in items alone; any other declared collection is a "
				+ "slot carried over from the web parent that the mobile component does not render. Offenders: "
				+ string.Join(", ", layers
					.Where(e => !DeclaredChildSlots(e).SequenceEqual(new[] { "items" }))
					.Select(e => $"{e.MobileName} [{string.Join("|", DeclaredChildSlots(e))}]")));
	}

	[Test]
	[Description("ENG-96153 row order: a web tab's tools strip is its header and renders above the tab body, so after the retarget it must occupy a LOWER Area row than the tab's items content — element-map order alone stacks it last, because the walk always reaches items children before tools children.")]
	public void Analyze_ShouldStackToolsStripAboveItemsContent_OnTheRealLeadsFormPageShape() {
		// Arrange
		JsonObject fixture = LoadFixture();
		JsonNode viewConfig = fixture["viewConfig"]!;
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectTypes(viewConfig, mobileTypes);
		IReadOnlyList<string> headerNames = ChildrenOfTabToolsStrips(viewConfig);
		headerNames.Should().HaveCountGreaterThan(1,
			because: "the pinned page must still carry several tabs whose header sits in the tools strip, or "
				+ "this test proves the ordering rule on a single accidental case");

		// Act
		MobilePageConversionGuide guide = Convert(fixture, mobileTypes, BundledRules());

		// Assert
		var checkedAreas = new List<string>();
		foreach (string headerName in headerNames) {
			ElementMapEntry header = guide.ElementMap.SingleOrDefault(
				e => string.Equals(e.WebName, headerName, StringComparison.Ordinal)
					&& string.Equals(e.Operation, "insert", StringComparison.Ordinal));
			if (header?.ParentName is not { Length: > 0 } area) {
				continue; // a header the converter dropped entirely carries no row to compare
			}
			List<ElementMapEntry> bodySiblings = guide.ElementMap
				.Where(e => string.Equals(e.Operation, "insert", StringComparison.Ordinal)
					&& string.Equals(e.ParentName, area, StringComparison.OrdinalIgnoreCase)
					&& !headerNames.Contains(e.WebName, StringComparer.Ordinal))
				.ToList();
			if (bodySiblings.Count == 0) {
				continue; // nothing to sit above — a header-only tab cannot express the ordering
			}
			checkedAreas.Add(area);
			int headerRow = AssignedRow(header);
			headerRow.Should().BeGreaterThan(0,
				because: $"the retarget gives every moved child a single-column layoutConfig, so '{headerName}' must carry a row");
			bodySiblings.Select(AssignedRow).Should().OnlyContain(bodyRow => bodyRow > headerRow,
				because: $"the tools strip is the tab's header: in Area '{area}' it must render above "
					+ $"[{string.Join(", ", bodySiblings.Select(e => $"{e.WebName ?? e.MobileName}@row{AssignedRow(e)}"))}], "
					+ $"but it was placed at row {headerRow}");
		}
		checkedAreas.Should().HaveCountGreaterThan(1,
			because: "the ordering rule must have been exercised on more than one tab of the pinned page — "
				+ "zero or one comparable Area means the assertions above were mostly skipped");
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────────

	/// <summary>The grid row the tab-area pass assigned to a moved child, or -1 when it carries no placement.</summary>
	private static int AssignedRow(ElementMapEntry entry) =>
		entry.MobileValues is JsonObject values
		&& values["layoutConfig"] is JsonObject layoutConfig
		&& layoutConfig["row"] is JsonValue row
		&& row.TryGetValue(out int parsed)
			? parsed
			: -1;

	/// <summary>Names of the elements sitting directly in a <c>crt.TabContainer</c>'s <c>tools</c> strip.</summary>
	private static IReadOnlyList<string> ChildrenOfTabToolsStrips(JsonNode node) {
		var names = new List<string>();
		Collect(node);
		return names;

		void Collect(JsonNode current) {
			switch (current) {
				case JsonArray array:
					foreach (JsonNode item in array.Where(i => i is not null)) {
						Collect(item!);
					}
					break;
				case JsonObject obj:
					if (string.Equals(obj["type"]?.ToString(), "crt.TabContainer", StringComparison.OrdinalIgnoreCase)
						&& obj["tools"] is JsonArray tools) {
						names.AddRange(tools.OfType<JsonObject>()
							.Select(tool => tool["name"]?.ToString())
							.Where(name => name is { Length: > 0 })!);
					}
					foreach (KeyValuePair<string, JsonNode> pair in obj.Where(p => p.Value is not null)) {
						Collect(pair.Value!);
					}
					break;
			}
		}
	}

	/// <summary>The tab-body and Area containers the tab-area pass synthesizes (no web counterpart).</summary>
	private static List<ElementMapEntry> SynthesizedTabLayers(MobilePageConversionGuide guide) =>
		guide.ElementMap
			.Where(e => string.Equals(e.Operation, "insert", StringComparison.Ordinal)
				&& e.WebName is null or { Length: 0 }
				&& e.MobileName is { Length: > 0 }
				&& (e.MobileName.StartsWith("MainTabContainer_", StringComparison.Ordinal)
					|| e.MobileName.StartsWith("GridContainer_", StringComparison.Ordinal)))
			.ToList();

	/// <summary>The child-collection slots an entry's prebuilt <c>mobileValues</c> physically declares, ordered.</summary>
	private static IReadOnlyList<string> DeclaredChildSlots(ElementMapEntry entry) =>
		entry.MobileValues is JsonObject values
			? values.Where(pair => pair.Value is JsonArray)
				.Select(pair => pair.Key)
				.OrderBy(slot => slot, StringComparer.Ordinal)
				.ToList()
			: [];

	private static JsonObject LoadFixture() {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures", FixtureName);
		return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
	}

	private static WebToMobilePageConversionRules BundledRules() =>
		WebToMobilePageConversionRulesCatalog.LoadBundled();

	/// <summary>
	/// The bundled rules with ONLY the <c>excludedComponents</c> section emptied, for the A/B baseline.
	/// Built by editing the bundled JSON and re-parsing it through the production parser, deliberately NOT by
	/// copying properties onto a new <see cref="WebToMobilePageConversionRules"/>: a hand-copied baseline
	/// silently loses any property added to that class later, and the A/B would then compare two rule sets
	/// that differ in more than the one section under test — while staying green, which is the worst kind of
	/// wrong. Re-parsing is immune by construction, because the parser is the same one production uses.
	/// </summary>
	private static WebToMobilePageConversionRules WithoutExcludedComponents() {
		JsonObject rules = JsonNode.Parse(BundledRulesJson())!.AsObject();
		rules.ContainsKey("excludedComponents").Should().BeTrue(
			because: "the baseline is defined as 'the shipped rules minus this section' — if the section is not "
				+ "there under this name, the A/B below compares a rule set against itself and proves nothing");
		rules["excludedComponents"] = new JsonArray();
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rules.ToJsonString()));
		WebToMobilePageConversionRules parsed = WebToMobilePageConversionRulesCatalog.ParseStream(stream);
		parsed.ExcludedComponents.Should().BeEmpty(
			because: "the baseline must genuinely carry no exclusion, or the comparison is between two identical runs");
		return parsed;
	}

	/// <summary>The bundled rules file as shipped, read from the assembly the production catalog reads it from.</summary>
	private static string BundledRulesJson() {
		Assembly assembly = typeof(WebToMobileAnalysisService).Assembly;
		string resourceName = assembly.GetManifestResourceNames()
			.Single(name => name.EndsWith("WebToMobilePageConversionRules.json", StringComparison.OrdinalIgnoreCase));
		using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
		using var reader = new StreamReader(stream, Encoding.UTF8);
		return reader.ReadToEnd();
	}

	/// <summary>Surviving entries whose prebuilt <c>mobileValues</c> still carry a <c>crt.SearchFilter</c> node.</summary>
	private static IReadOnlyList<string> VerbatimCarriersOfSearchFilter(MobilePageConversionGuide guide) =>
		guide.ElementMap
			.Where(e => e.MobileValues is not null
				&& e.MobileValues.ToJsonString().Contains("crt.SearchFilter", StringComparison.Ordinal))
			.Select(e => e.MobileName ?? e.WebName ?? "<unnamed>")
			.ToList();

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

	/// <summary>Every search filter the converter DROPPED, with its reason.</summary>
	private static List<DroppedElement> DroppedSearchFilters(MobilePageConversionGuide guide) =>
		[.. (guide.DroppedElements ?? [])
			.Where(e => string.Equals(e.WebType, "crt.SearchFilter", StringComparison.OrdinalIgnoreCase))];

	/// <summary>
	/// Every search filter that SURVIVED into an operation. Must always be empty on this page — the point of
	/// the acceptance criterion is that none reaches the canvas.
	/// </summary>
	private static List<ElementMapEntry> SurvivingSearchFilters(MobilePageConversionGuide guide) =>
		[.. guide.ElementMap
			.Where(e => string.Equals(e.WebType, "crt.SearchFilter", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e.MobileType, "crt.SearchFilter", StringComparison.OrdinalIgnoreCase))];

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
