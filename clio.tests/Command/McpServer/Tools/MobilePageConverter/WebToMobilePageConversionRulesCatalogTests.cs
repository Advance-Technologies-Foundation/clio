using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebToMobilePageConversionRulesCatalogTests {

	private static Stream JsonStream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

	[Test]
	[Description("Every override rule carries ONLY data: a component type, the values to stamp and whether they merge. No rule may carry caller-facing prose, because the rules file is resolved at runtime and the guide's constraints/nextSteps are the caller's instruction channel.")]
	public void LoadBundled_OverridesCarryDataOnly() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		rules.ComponentPropertyOverrides.Should().OnlyContain(o => o.Values.Count > 0,
			because: "a rule without values cannot stamp anything");
		rules.ComponentPropertyOverrides.Should().OnlyContain(o => o.Filters != null && o.Filters.Count > 0,
			because: "filters are the rule's ONLY selector — an ABSENT list makes the pass skip the rule "
				+ "outright, and an EMPTY one would stamp onto every insert of every type; no standard wants "
				+ "either, so the bundled file must always name what it targets");
		rules.ComponentPropertyOverrides
			.SelectMany(o => o.Filters ?? [])
			.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Type),
				because: "a bundled filter that names no type would widen its standard across component types");
	}

	[Test]
	[Description("A rules file without the excludedComponents section parses to an empty list — the pass is then a no-op (data-switched feature), matching how emptyContainerRemoval/tabAreaLayers degrade when absent.")]
	public void ParseStream_WithoutExcludedComponents_ParsesToEmptyList() {
		const string json = """{ "version": "8.3.3", "templates": [], "components": [] }""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.ExcludedComponents.Should().BeEmpty(
			because: "an absent section must switch the removal pass off rather than throw or default to null");
	}

	[Test]
	[Description("ParseStream parses a excludedComponents group into the typed filter rule: type, parentType and the optional propertiesContainerName all round-trip.")]
	public void ParseStream_WithExcludedComponents_ParsesTypedFilterRule() {
		const string json = """
			{
			  "version": "8.3.3",
			  "excludedComponents": [
			    {
			      "filters": [
			        { "type": "crt.SearchFilter", "parentType": "crt.ExpansionPanel", "propertiesContainerName": "tools" }
			      ]
			    }
			  ]
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		ExcludedComponentFilterRule filter = rules.ExcludedComponents.Single().Filters.Single();
		filter.Type.Should().Be("crt.SearchFilter",
			because: "the banned type must survive parsing verbatim — the pass matches on this string");
		filter.ParentType.Should().Be("crt.ExpansionPanel",
			because: "the host type is what scopes the search; losing it would widen the ban to the whole page");
		filter.PropertiesContainerName.Should().Be("tools",
			because: "the optional slot must bind when present — dropping it would widen the ban to the host's other properties");
	}

	[Test]
	[Description("propertiesContainerName is genuinely optional on a excludedComponents filter — it parses to null when omitted, so the runtime pass can fall back to searching the whole host subtree instead of one named property.")]
	public void ParseStream_ExcludedComponentsWithoutPropertiesContainerName_ParsesToNull() {
		const string json = """
			{
			  "version": "8.3.3",
			  "excludedComponents": [
			    { "filters": [ { "type": "usr.Foo", "parentType": "usr.Bar" } ] }
			  ]
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.ExcludedComponents.Single().Filters.Single().PropertiesContainerName.Should().BeNull(
			because: "an absent propertiesContainerName means 'search the whole host subtree', not 'match nothing'");
	}

	[Test]
	[Description("A rules file without the tabAreaLayers group parses to null — the tab-area pass is then a no-op (data-switched feature).")]
	public void ParseStream_WithoutTabAreaLayers_ParsesToNull() {
		const string json = """{ "version": "8.3.3", "templates": [], "components": [] }""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.TabAreaLayers.Should().BeNull();
	}

	[Test]
	[Description("ParseStream parses the tabAreaLayers group into the typed rule (nested tab-body → Area chain, prefixes + verbatim values).")]
	public void ParseStream_WithTabAreaLayers_ParsesTypedRule() {
		const string json = """
			{
			  "version": "8.3.3",
			  "tabAreaLayers": {
			    "note": "n",
			    "tabComponentType": "usr.CustomTab",
			    "mainTabContainer": {
			      "namePrefix": "MainTabContainer_",
			      "values": { "type": "crt.GridContainer", "alignItems": "stretch" },
			      "areaContainer": { "namePrefix": "GridContainer_", "values": { "type": "crt.GridContainer", "color": "primary" } }
			    }
			  }
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.TabAreaLayers.Should().NotBeNull();
		rules.TabAreaLayers.Note.Should().Be("n");
		rules.TabAreaLayers.TabComponentType.Should().Be("usr.CustomTab");
		rules.TabAreaLayers.MainTabContainer.NamePrefix.Should().Be("MainTabContainer_");
		rules.TabAreaLayers.MainTabContainer.Values["alignItems"].GetString().Should().Be("stretch");
		rules.TabAreaLayers.MainTabContainer.AreaContainer.NamePrefix.Should().Be("GridContainer_");
		rules.TabAreaLayers.MainTabContainer.AreaContainer.Values["color"].GetString().Should().Be("primary");
	}

	[Test]
	[Description("A tabAreaLayers group that omits tabComponentType falls back to the platform's own tab type, so an older rules file keeps working.")]
	public void ParseStream_TabAreaLayersWithoutTabComponentType_FallsBackToPlatformTabType() {
		const string json = """
			{
			  "version": "8.3.3",
			  "tabAreaLayers": {
			    "mainTabContainer": {
			      "namePrefix": "MainTabContainer_",
			      "values": { "type": "crt.GridContainer" },
			      "areaContainer": { "namePrefix": "GridContainer_", "values": { "type": "crt.GridContainer" } }
			    }
			  }
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.TabAreaLayers.TabComponentType.Should().Be("crt.TabContainer");
	}

	[Test]
	[Description("ParseStream supports many-to-many component equivalence rules (lists on both sides).")]
	public void ParseStream_SupportsManyToManyComponentRule() {
		const string json = """
			{
			  "version": "8.3.3",
			  "templates": [],
			  "components": [
			    { "web": ["crt.A", "crt.B"], "mobile": ["crt.X", "crt.Y"], "category": "WithAdaptation", "note": "n" }
			  ]
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.Version.Should().Be("8.3.3");
		ComponentEquivalenceRule rule = rules.Components.Single();
		rule.Web.Should().BeEquivalentTo("crt.A", "crt.B");
		rule.Mobile.Should().BeEquivalentTo("crt.X", "crt.Y");
		rule.Category.Should().Be("WithAdaptation");
	}

	[Test]
	[Description("GetRulesAsync falls back to the bundled rules when the registry client cannot serve them (CDN not published yet).")]
	public async Task GetRulesAsync_WhenClientUnavailable_FallsBackToBundled() {
		var client = Substitute.For<IWebToMobilePageConversionRulesRegistryClient>();
		client.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<ComponentRegistryFetchResult>(
				new ComponentRegistryUnavailableException("latest", "https://cdn.example")));
		var catalog = new WebToMobilePageConversionRulesCatalog(client);

		WebToMobilePageConversionRules rules = await catalog.GetRulesAsync("latest");

		rules.Should().NotBeNull();
		rules.Components.Should().Contain(c => c.Filters.Any(f => f.Type == "crt.DataGrid"),
			because: "the bundled rules are the fallback source today");
	}

	[Test]
	[Description("GetRulesAsync returns the rules served by the registry client when available (CDN/cache/local override).")]
	public async Task GetRulesAsync_WhenClientServesRules_ReturnsThem() {
		const string json = """{ "version": "9.9.9", "templates": [], "components": [] }""";
		var client = Substitute.For<IWebToMobilePageConversionRulesRegistryClient>();
		client.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new ComponentRegistryFetchResult(
				JsonStream(json), "9.9.9", ComponentRegistrySource.Cdn)));
		var catalog = new WebToMobilePageConversionRulesCatalog(client);

		WebToMobilePageConversionRules rules = await catalog.GetRulesAsync("9.9.9");

		rules.Version.Should().Be("9.9.9",
			because: "when the client serves rules, the catalog must use them rather than the bundled fallback");
	}

}
