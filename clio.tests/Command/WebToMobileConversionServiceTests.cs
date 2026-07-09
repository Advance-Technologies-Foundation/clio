using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebToMobileConversionServiceTests {

	private static readonly IReadOnlySet<string> MobileTypes =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.Input", "crt.Toggle", "crt.RichTextEditor", "crt.List", "crt.FolderTreeActions", "crt.GridContainer", "crt.Label", "crt.IndicatorWidget", "crt.CommunicationOptions"
		};

	private static readonly IReadOnlySet<string> WebTypes =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.Input", "crt.Checkbox", "crt.HtmlEditor", "crt.DataGrid", "crt.DataTable",
			"crt.ColorButton", "crt.FolderTree", "crt.FolderTreeActions"
		};

	private static readonly WebToMobilePageConversionRules Rules = new() {
		Templates = [
			new TemplateMappingRule {
				Web = "PageWithTabsFreedomTemplate", Mobile = "MobilePageWithTabsFreedomTemplate",
				Note = "Tabbed record page.",
				Containers = [
					new ContainerMappingRule { Web = "SideAreaProfileContainer", Mobile = "AreaProfileContainer", Note = "profile fields" }
				]
			}
		],
		Components = [
			new ComponentEquivalenceRule { Web = ["crt.Checkbox"], Mobile = ["crt.Toggle"], Category = "AlternativeAvailable" },
			new ComponentEquivalenceRule { Web = ["crt.HtmlEditor"], Mobile = ["crt.RichTextEditor"], Category = "AlternativeAvailable" },
			new ComponentEquivalenceRule { Web = ["crt.DataGrid", "crt.DataTable"], Mobile = ["crt.List"], Category = "AlternativeAvailable" },
			new ComponentEquivalenceRule {
				Web = ["crt.FolderTree", "crt.FolderTreeActions"], Mobile = ["crt.FolderTreeActions"],
				Category = "AlternativeAvailable", PrimaryWeb = "crt.FolderTree"
			}
		]
	};

	private static IReadOnlyDictionary<string, ComponentRegistryEntry> Reg(params (string type, bool container)[] entries) {
		var d = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase);
		foreach ((string type, bool container) in entries) {
			d[type] = new ComponentRegistryEntry { ComponentType = type, Container = container };
		}
		return d;
	}

	private static PageBundleInfo Bundle(
		string viewConfigJson, string modelConfigJson = null,
		string handlers = null, string validators = null, string converters = null,
		string viewModelConfigJson = null, string resourcesJson = null) =>
		new() {
			ViewConfig = JsonNode.Parse(viewConfigJson)!.AsArray(),
			ModelConfig = modelConfigJson is null ? new JsonObject() : JsonNode.Parse(modelConfigJson)!.AsObject(),
			ViewModelConfig = viewModelConfigJson is null ? new JsonObject() : JsonNode.Parse(viewModelConfigJson)!.AsObject(),
			Resources = resourcesJson is null ? new PageResourceInfo() : new PageResourceInfo { Strings = JsonNode.Parse(resourcesJson)!.AsObject() },
			Handlers = handlers,
			Validators = validators,
			Converters = converters
		};

	private static ElementMapEntry Element(MobilePageConversionGuide guide, string webName) =>
		guide.ElementMap.Single(e => e.WebName == webName);

	private static MobilePageConversionGuide Analyze(
		PageBundleInfo bundle,
		IReadOnlyDictionary<string, ComponentRegistryEntry> webByType = null,
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType = null,
		TemplateMappingRule templateRule = null,
		IReadOnlyDictionary<string, string> containerNameMap = null,
		IReadOnlySet<string> templateComponentNames = null,
		IReadOnlyDictionary<string, ComponentMappingRule> componentNameMap = null) =>
		WebToMobileAnalysisService.Analyze(
			bundle, MobileTypes, WebTypes,
			webByType ?? new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType,
			Rules, templateRule,
			sourcePage: "UsrApp_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrApp_MobileFormPage", containerNameMap: containerNameMap,
			templateComponentNames: templateComponentNames,
			componentNameMap: componentNameMap);

	private static ComponentSuggestion ForType(MobilePageConversionGuide guide, string sourceType) =>
		guide.ComponentSuggestions.Single(s => s.SourceType == sourceType);

	[Test]
	[Description("The merged tree (including inherited template components) is surfaced as sourceStructure with parent + container flags.")]
	public void Analyze_SourceStructure_SurfacesMergedTreeWithContainerFlags() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "UsrName", "type": "crt.Input" },
				{ "name": "ListContainer", "type": "crt.FlexContainer", "items": [
					{ "name": "DataTable", "type": "crt.DataGrid" } ] } ] } ]
			""");
		var web = Reg(("crt.FlexContainer", true), ("crt.Input", false), ("crt.DataGrid", false));

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web);

		guide.SourceStructure.Should().Contain(s => s.Name == "Main" && s.IsContainer && s.ParentName == null);
		guide.SourceStructure.Should().Contain(s => s.Name == "UsrName" && !s.IsContainer && s.ParentName == "Main");
		guide.SourceStructure.Should().Contain(s => s.Name == "ListContainer" && s.IsContainer && s.ParentName == "Main");
		guide.SourceStructure.Should().Contain(s => s.Name == "DataTable" && !s.IsContainer && s.ParentName == "ListContainer");
	}

	[Test]
	[Description("Component suggestions classify each present type via the matrix first, then registry membership (direct / unsupported / manual).")]
	public void Analyze_ComponentSuggestions_ClassifyViaMatrixAndRegistry() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "UsrName", "type": "crt.Input" },
				{ "name": "UsrFlag", "type": "crt.Checkbox" },
				{ "name": "Grid", "type": "crt.DataGrid" },
				{ "name": "Color", "type": "crt.ColorButton" },
				{ "name": "Custom", "type": "usr.MyWidget" } ] } ]
			""");
		var web = Reg(("crt.FlexContainer", true));

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web);

		ForType(guide,"crt.Input").Category.Should().Be("DirectMapping");
		ForType(guide,"crt.Input").SuggestedMobileTypes.Should().Equal("crt.Input");
		ForType(guide,"crt.Checkbox").Category.Should().Be("AlternativeAvailable");
		ForType(guide,"crt.Checkbox").SuggestedMobileTypes.Should().Equal("crt.Toggle");
		ForType(guide,"crt.DataGrid").Category.Should().Be("AlternativeAvailable");
		ForType(guide,"crt.DataGrid").SuggestedMobileTypes.Should().Equal("crt.List");
		ForType(guide,"crt.ColorButton").Category.Should().Be("Unsupported");
		ForType(guide,"crt.ColorButton").SuggestedMobileTypes.Should().BeEmpty();
		ForType(guide,"usr.MyWidget").Category.Should().Be("RequiresManualDecision");
		ForType(guide,"usr.MyWidget").SuggestedMobileTypes.Should().BeEmpty();
	}

	[Test]
	[Description("A grid rule mapping to [crt.List, crt.ListItem] surfaces both suggested mobile types and the conversion note; the element map inserts the primary type (crt.List), and the model adds the crt.ListItem row into its itemLayout per the note.")]
	public void Analyze_ComponentSuggestions_GridMapsToListAndListItem() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "DataTable", "type": "crt.DataGrid" } ] } ]
			""");
		var rules = new WebToMobilePageConversionRules {
			Components = [
				new ComponentEquivalenceRule {
					Web = ["crt.DataGrid"], Mobile = ["crt.List", "crt.ListItem"], Category = "AlternativeAvailable",
					Note = "Add a crt.ListItem into the crt.List itemLayout (title + body)."
				}
			]
		};

		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, MobileTypes, WebTypes,
			new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, rules, templateRule: null,
			sourcePage: "UsrApp_ListPage", sourceTemplate: "ListPageV3Template",
			suggestedTarget: "UsrApp_MobileListPage", containerNameMap: null);

		ComponentSuggestion grid = ForType(guide, "crt.DataGrid");
		grid.Category.Should().Be("AlternativeAvailable");
		grid.SuggestedMobileTypes.Should().Equal("crt.List", "crt.ListItem");
		grid.Note.Should().Contain("itemLayout");
		// Element map inserts the primary mobile type; the model adds the ListItem row into its itemLayout.
		Element(guide, "DataTable").Operation.Should().Be("insert");
		Element(guide, "DataTable").MobileType.Should().Be("crt.List");
	}

	[Test]
	[Description("Many->one mappings (FolderTree + FolderTreeActions -> FolderTreeActions) carry a primaryWebMerge note so the model emits a single merged component.")]
	public void Analyze_ManyToOne_ProducesPrimaryWebMergeNote() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "FolderTree", "type": "crt.FolderTree" },
				{ "name": "FolderTreeActions", "type": "crt.FolderTreeActions" } ] } ]
			""");
		var web = Reg(("crt.FlexContainer", true));

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web);

		ComponentSuggestion primary = ForType(guide,"crt.FolderTree");
		primary.SuggestedMobileTypes.Should().Equal("crt.FolderTreeActions");
		primary.PrimaryWebMerge.Should().NotBeNull();
		primary.PrimaryWebMerge.Should().Contain("crt.FolderTree");
		primary.PrimaryWebMerge.Should().Contain("crt.FolderTreeActions");
	}

	[Test]
	[Description("Inline mobile contracts expose allowedProperties + example + designerDefaults for each suggested mobile type.")]
	public void Analyze_MobileContracts_InlineForSuggestedTypes() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "UsrFlag", "type": "crt.Checkbox" } ] } ]
			""");
		var web = Reg(("crt.FlexContainer", true));
		var toggle = new ComponentRegistryEntry {
			ComponentType = "crt.Toggle",
			Description = "Boolean toggle.",
			Inputs = new Dictionary<string, JsonElement> { ["keepMe"] = JsonSerializer.SerializeToElement(new { }) },
			Example = JsonSerializer.SerializeToElement(new { type = "crt.Toggle" }),
			DesignerDefaults = JsonSerializer.SerializeToElement(new { caption = "" })
		};
		var mobileByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.Toggle"] = toggle
		};

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web, mobileByType: mobileByType);

		MobileComponentContract contract = guide.MobileContracts.Single(c => c.ComponentType == "crt.Toggle");
		contract.AllowedProperties.Should().Contain("keepMe");
		contract.Example.HasValue.Should().BeTrue();
		contract.DesignerDefaults.HasValue.Should().BeTrue();
		contract.Description.Should().Be("Boolean toggle.");
	}

	[Test]
	[Description("The matched template rule produces recommendedMobileTemplate and a web->mobile containerMap.")]
	public void Analyze_TemplateRule_ProducesRecommendedTemplateAndContainerMap() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "UsrName", "type": "crt.Input" } ] } ]
			""");
		TemplateMappingRule rule = Rules.Templates[0];

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true)), templateRule: rule);

		guide.RecommendedMobileTemplate.Should().Be("MobilePageWithTabsFreedomTemplate");
		guide.ContainerMap.Should().ContainSingle(c => c.Web == "SideAreaProfileContainer" && c.Mobile == "AreaProfileContainer");
	}

	[Test]
	[Description("Web-only sections and ALL data sources are surfaced (not stripped); mobile supports the same multi-data-source structure, so there is no 'keep only the primary' constraint.")]
	public void Analyze_WebOnlySectionsAndDataSources_AreSurfaced() {
		PageBundleInfo bundle = Bundle(
			"""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "UsrName", "type": "crt.Input" } ] } ]
			""",
			modelConfigJson: """
			{ "dataSources": { "PDS": { "type": "crt.EntityDataSource" }, "SecondDS": { "type": "crt.EntityDataSource" } } }
			""",
			handlers: "[{ request: 'crt.HandleViewModelInitRequest' }]",
			validators: "{ UsrName: ['required'] }",
			converters: "{}");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true)));

		guide.WebOnlySections.Should().Contain("handlers").And.Contain("validators");
		guide.WebOnlySections.Should().NotContain("converters");
		guide.DataSources.Should().BeEquivalentTo("PDS", "SecondDS");
		guide.Constraints.Should().NotContain(c => c.Contains("MULTIPLE data sources") || c.Contains("SINGLE data source"),
			because: "mobile supports the same data-source structure as web — no multi-DS limitation is imposed");
		guide.Constraints.Should().Contain(c => c.Contains("business rules"));
	}

	[Test]
	[Description("The guide always carries the detected source type, guidance article name, ordered nextSteps, and the hard mobile constraints.")]
	public void Analyze_GuideCarriesSourceTypeGuidanceNextStepsAndConstraints() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "UsrName", "type": "crt.Input" } ] } ]
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true)));

		guide.SourceType.Should().Be("freedom-web");
		guide.GuidanceArticle.Should().Be("freedom-page-web-to-mobile-conversion");
		guide.SuggestedTargetSchemaName.Should().Be("UsrApp_MobileFormPage");
		guide.NextSteps.Should().NotBeEmpty();
		guide.NextSteps.Should().Contain(s => s.Contains("create-page"));
		guide.Constraints.Should().Contain(c => c.Contains("Scaffold"));
	}

	[Test]
	[Description("A supplied SectionRegistrationInfo is carried into the guide unchanged.")]
	public void Analyze_CarriesSectionRegistrationIntoGuide() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "DataTable", "type": "crt.DataGrid" } ] } ]
			""");
		var registration = new SectionRegistrationInfo { SourcePageIsSection = true, SysModuleId = "abc", ProbeOk = true };

		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, MobileTypes, WebTypes, Reg(("crt.FlexContainer", true)), null, Rules, templateRule: null,
			sourcePage: "UsrApp_ListPage", sourceTemplate: "ListPageV3Template",
			suggestedTarget: "UsrApp_MobileListPage", containerNameMap: null, sectionRegistration: registration);

		guide.SectionRegistration.Should().BeSameAs(registration);
	}

	[Test]
	[Description("Source-type detection maps the platform schema-type: web -> freedom-web, mobile -> mobile, anything else verbatim (lowercased) as not-yet-supported.")]
	public void DetectSourceType_MapsPlatformSchemaType() {
		MobilePageConversionGuideTool.DetectSourceType("web").Should().Be("freedom-web");
		MobilePageConversionGuideTool.DetectSourceType("WEB").Should().Be("freedom-web");
		MobilePageConversionGuideTool.DetectSourceType("mobile").Should().Be("mobile");
		MobilePageConversionGuideTool.DetectSourceType("classic").Should().Be("classic");
		MobilePageConversionGuideTool.DetectSourceType(null).Should().Be("unknown");
	}

	private static MobilePageConversionGuideArgs ArgsFor(string schemaName) =>
		new(schemaName, null, null, null, null, null, null);

	[Test]
	[Description("A Classic UI source page is rejected: no conversion runs and the failure explains a Freedom UI web migration is required first.")]
	public void RejectUnsupportedSourceType_ShouldReturnMigrationFailure_WhenSourceIsClassicUi() {
		// Arrange
		MobilePageConversionGuideArgs args = ArgsFor("UsrApp_FormPage");

		// Act
		MobilePageConversionGuideResponse rejection =
			MobilePageConversionGuideTool.RejectUnsupportedSourceType(args, "classic");

		// Assert
		rejection.Should().NotBeNull(
			because: "a Classic UI page must never start mobile conversion (hard acceptance criterion)");
		rejection.Success.Should().BeFalse(
			because: "an unsupported source type is a failed conversion request, not a partial success");
		rejection.SourceType.Should().Be("classic",
			because: "the response echoes the detected source type so the caller can explain what was found");
		rejection.Error.Should().Contain("Freedom UI web",
			because: "the message must direct the user to convert Classic UI to Freedom UI web first");
	}

	[Test]
	[Description("A page that is already a mobile page is rejected as nothing to convert.")]
	public void RejectUnsupportedSourceType_ShouldReturnAlreadyMobileFailure_WhenSourceIsMobile() {
		// Arrange
		MobilePageConversionGuideArgs args = ArgsFor("UsrApp_MobileFormPage");

		// Act
		MobilePageConversionGuideResponse rejection =
			MobilePageConversionGuideTool.RejectUnsupportedSourceType(args, "mobile");

		// Assert
		rejection.Should().NotBeNull(
			because: "an already-mobile page has nothing to convert and must short-circuit");
		rejection.Success.Should().BeFalse(
			because: "there is no conversion to perform on a mobile page");
		rejection.Error.Should().Contain("already a mobile page",
			because: "the message must tell the user the source is already mobile");
	}

	[Test]
	[Description("An unknown/undetectable source type is rejected rather than converted on a guess.")]
	public void RejectUnsupportedSourceType_ShouldReturnFailure_WhenSourceIsUnknown() {
		// Arrange
		MobilePageConversionGuideArgs args = ArgsFor("UsrApp_FormPage");

		// Act
		MobilePageConversionGuideResponse rejection =
			MobilePageConversionGuideTool.RejectUnsupportedSourceType(args, "unknown");

		// Assert
		rejection.Should().NotBeNull(
			because: "the converter must not silently proceed when the source type could not be classified");
		rejection.Success.Should().BeFalse(
			because: "an unclassified source is not a supported Freedom UI web page");
	}

	[Test]
	[Description("A Freedom UI web source is accepted (no rejection) so conversion can proceed.")]
	public void RejectUnsupportedSourceType_ShouldReturnNull_WhenSourceIsFreedomWeb() {
		// Arrange
		MobilePageConversionGuideArgs args = ArgsFor("UsrApp_FormPage");

		// Act
		MobilePageConversionGuideResponse rejection =
			MobilePageConversionGuideTool.RejectUnsupportedSourceType(args, WebToMobileAnalysisService.SourceTypeFreedomWeb);

		// Assert
		rejection.Should().BeNull(
			because: "a Freedom UI web page is the one supported source today and must be allowed to convert");
	}

	[Test]
	[Description("Mobile schema name is derived from the web page name with the correct suffix.")]
	public void DeriveMobileSchemaName_AppliesMobileSuffix() {
		MobilePageConversionGuideTool.DeriveMobileSchemaName("UsrApp_FormPage").Should().Be("UsrApp_MobileFormPage");
		MobilePageConversionGuideTool.DeriveMobileSchemaName("UsrApp_ListPage").Should().Be("UsrApp_MobileListPage");
		MobilePageConversionGuideTool.DeriveMobileSchemaName("UsrApp_Custom").Should().Be("UsrApp_Custom_Mobile");
	}

	[Test]
	[Description("Container detection uses the registry container flag; an unknown type falls back to a name-suffix heuristic.")]
	public void Analyze_ContainerDetection_UsesRegistryFlagThenNameSuffix() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Wrapper", "type": "crt.SomeNewContainer", "items": [
				{ "name": "Field", "type": "crt.SomeField" },
				{ "name": "ExtraPanel", "type": "usr.Unknown", "items": [
					{ "name": "Inner", "type": "usr.Widget" } ] } ] } ]
			""");
		var web = Reg(("crt.SomeNewContainer", true), ("crt.SomeField", false));

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web);

		guide.SourceStructure.Single(s => s.Name == "Wrapper").IsContainer.Should().BeTrue(because: "registry flag");
		guide.SourceStructure.Single(s => s.Name == "Field").IsContainer.Should().BeFalse(because: "registry flag");
		guide.SourceStructure.Single(s => s.Name == "ExtraPanel").IsContainer.Should().BeTrue(because: "name-suffix fallback");
		guide.SourceStructure.Single(s => s.Name == "Inner").IsContainer.Should().BeFalse();
	}

	// ── elementMap (instance-level mapping) ───────────────────────────────────────────────────

	private static readonly IReadOnlySet<string> TabbedMobileTypes =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.TabContainer", "crt.Input", "crt.ComboBox", "crt.DateTimeEdit", "crt.Feed", "crt.AttachmentList"
		};

	private static readonly WebToMobilePageConversionRules GridRule = new() {
		Components = [new ComponentEquivalenceRule { Web = ["crt.DataGrid"], Mobile = ["crt.List"], Category = "AlternativeAvailable" }]
	};

	private static readonly IReadOnlyDictionary<string, string> TabbedContainerMap =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["Tabs"] = "Tabs",
			["FeedTabContainer"] = "FeedContainer",
			["AttachmentsTabContainer"] = "AttachmentsContainer",
			["GeneralInfoTabContainer"] = "GeneralTabContainer",
			["SideAreaProfileContainer"] = "AreaProfileContainer"
		};

	private static MobilePageConversionGuide AnalyzeTabbed(
		PageBundleInfo bundle,
		IReadOnlyDictionary<string, string> containerNameMap = null,
		IReadOnlyList<WebToMobileAnalysisService.PositionalPlacement> positionalPlacements = null,
		IReadOnlyDictionary<string, string> mobileContainerParents = null) =>
		WebToMobileAnalysisService.Analyze(
			bundle, TabbedMobileTypes,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crt.DataGrid", "crt.IndicatorWidget", "crt.Timeline" },
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, GridRule, templateRule: null,
			sourcePage: "Leads_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrLeads_MobileFormPage", containerNameMap: containerNameMap ?? TabbedContainerMap,
			positionalPlacements: positionalPlacements,
			mobileContainerParents: mobileContainerParents);

	[Test]
	[Description("Golden Leads_FormPage: Tabs merges; EVERY web tab inserts as its OWN new mobile tab (no general-tab collapsing); a tab with a caption keeps it; multi-DS/unsupported children drop; template twins merge.")]
	public void Analyze_ElementMap_GoldenLeadsFormPage() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input", "caption": "$Resources.Strings.LeadName_caption" },
					{ "name": "Status", "type": "crt.ComboBox" },
					{ "name": "IndicatorWidget", "type": "crt.IndicatorWidget" },
					{ "name": "SimilarLeadList", "type": "crt.DataGrid", "dataSourceName": "SimilarLeadsDS" } ] },
				{ "name": "FeedTabContainer", "type": "crt.TabContainer", "items": [ { "name": "Feed", "type": "crt.Feed" } ] },
				{ "name": "AttachmentsTabContainer", "type": "crt.TabContainer", "items": [ { "name": "AttachmentList", "type": "crt.AttachmentList" } ] },
				{ "name": "SalesTab", "type": "crt.TabContainer", "caption": "$Resources.Strings.SalesTab_caption", "items": [
					{ "name": "Budget", "type": "crt.Input" },
					{ "name": "DecisionDate", "type": "crt.DateTimeEdit" },
					{ "name": "SalesOwner", "type": "crt.Input" },
					{ "name": "ProductsList", "type": "crt.DataGrid", "dataSourceName": "ProductsListDS" } ] },
				{ "name": "ProcessingTab", "type": "crt.TabContainer", "items": [ { "name": "Timeline", "type": "crt.Timeline" } ] },
				{ "name": "HistoryTab", "type": "crt.TabContainer", "items": [ { "name": "HistGrid", "type": "crt.DataGrid", "dataSourceName": "HistoryDS" } ] }
			] } ]
			""",
			modelConfigJson: """
			{ "dataSources": { "PDS": {}, "ProductsListDS": {}, "SimilarLeadsDS": {}, "HistoryDS": {} } }
			""",
			resourcesJson: """
			{ "SalesTab_caption": { "en-US": "Sales" }, "LeadName_caption": { "en-US": "Lead name" } }
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle);

		// Tabs and template twins → merge (no insert).
		Element(guide, "Tabs").Operation.Should().Be("merge");
		Element(guide, "Tabs").MobileName.Should().Be("Tabs");
		Element(guide, "FeedTabContainer").Operation.Should().Be("merge");
		Element(guide, "AttachmentsTabContainer").Operation.Should().Be("merge");
		Element(guide, "Feed").Operation.Should().Be("insert");
		Element(guide, "Feed").ParentName.Should().Be("FeedContainer");
		Element(guide, "AttachmentList").ParentName.Should().Be("AttachmentsContainer");

		// Every web tab (including the first) → insert as its OWN new mobile tab under Tabs; its children
		// carry that tab's name (no general-tab collapse into AreaProfileContainer).
		ElementMapEntry overview = Element(guide, "OverviewTab");
		overview.Operation.Should().Be("insert");
		overview.ParentName.Should().Be("Tabs");
		overview.Index.Should().BeNull(because: "a web tab is not a positional insert");
		Element(guide, "LeadName").Operation.Should().Be("insert");
		Element(guide, "LeadName").ParentName.Should().Be("OverviewTab");
		Element(guide, "Status").ParentName.Should().Be("OverviewTab");
		// Unsupported / foreign-DS children of the tab → drop.
		Element(guide, "IndicatorWidget").Operation.Should().Be("drop");
		Element(guide, "SimilarLeadList").Operation.Should().Be("drop");
		Element(guide, "SimilarLeadList").Reason.Should().Contain("SimilarLeadsDS");

		// Page-specific tab → insert with caption; multi-DS child dropped.
		ElementMapEntry sales = Element(guide, "SalesTab");
		sales.Operation.Should().Be("insert");
		sales.ParentName.Should().Be("Tabs");
		sales.PropertyName.Should().Be("items");
		sales.CaptionResource.Key.Should().Be("SalesTab_caption");
		sales.CaptionResource.SourceValue.Should().Be("Sales");
		Element(guide, "Budget").Operation.Should().Be("insert");
		Element(guide, "Budget").ParentName.Should().Be("SalesTab");
		Element(guide, "ProductsList").Operation.Should().Be("drop");
		Element(guide, "ProductsList").Reason.Should().Contain("ProductsListDS");

		// Empty tabs are still inserted (the user can delete them); only their unsupported content drops.
		Element(guide, "ProcessingTab").Operation.Should().Be("insert");
		Element(guide, "Timeline").Operation.Should().Be("drop");
		Element(guide, "HistoryTab").Operation.Should().Be("insert");
		Element(guide, "HistGrid").Operation.Should().Be("drop");
	}

	[Test]
	[Description("Positional rule: a sibling ABOVE the anchor inserts into the mobile Tabs' parent at index 0 (above Tabs); a sibling BELOW appends (no index); the anchor's own non-tab content goes to GeneralTabContainer and each web tab becomes a new mobile tab.")]
	public void Analyze_ElementMap_PositionalSiblings_PlacedAroundMobileTabs() {
		PageBundleInfo bundle = Bundle("""
			[
			  { "name": "ProgressBarContainer", "type": "crt.FlexContainer", "items": [ { "name": "ProgressBar", "type": "crt.Input" } ] },
			  { "name": "CardContentWrapper", "type": "crt.GridContainer", "items": [
			      { "name": "SideField", "type": "crt.Input" },
			      { "name": "Tabs", "type": "crt.TabPanel", "items": [
			          { "name": "OverviewTab", "type": "crt.TabContainer", "items": [ { "name": "LeadName", "type": "crt.Input" } ] } ] } ] },
			  { "name": "FooterField", "type": "crt.Input" }
			]
			""");
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.GridContainer", "crt.FlexContainer", "crt.TabContainer", "crt.Input"
		};
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["CardContentWrapper"] = "GeneralTabContainer", ["Tabs"] = "Tabs"
		};
		var placements = new List<WebToMobileAnalysisService.PositionalPlacement> {
			new("CardContentWrapper", "Tabs")
		};
		var mobileParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tabs"] = "MainContainer" };

		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, mobileTypes, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, GridRule, templateRule: null,
			sourcePage: "Leads_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrLeads_MobileFormPage", containerNameMap: map,
			positionalPlacements: placements, mobileContainerParents: mobileParents);

		// Anchor wrapper merges into the general tab's grid; its non-tab content lands there.
		Element(guide, "CardContentWrapper").Operation.Should().Be("merge");
		Element(guide, "CardContentWrapper").MobileName.Should().Be("GeneralTabContainer");
		Element(guide, "SideField").Operation.Should().Be("insert");
		Element(guide, "SideField").ParentName.Should().Be("GeneralTabContainer");

		// Web tab becomes its own new mobile tab.
		Element(guide, "OverviewTab").Operation.Should().Be("insert");
		Element(guide, "OverviewTab").ParentName.Should().Be("Tabs");
		Element(guide, "LeadName").ParentName.Should().Be("OverviewTab");

		// Sibling ABOVE the wrapper → inserted into the mobile Tabs' parent at index 0 (above Tabs).
		ElementMapEntry progress = Element(guide, "ProgressBarContainer");
		progress.Operation.Should().Be("insert");
		progress.ParentName.Should().Be("MainContainer");
		progress.Index.Should().Be(0);
		Element(guide, "ProgressBar").ParentName.Should().Be("ProgressBarContainer");

		// Sibling BELOW the wrapper → appended (no index) into the same parent.
		ElementMapEntry footer = Element(guide, "FooterField");
		footer.Operation.Should().Be("insert");
		footer.ParentName.Should().Be("MainContainer");
		footer.Index.Should().BeNull();
	}

	[Test]
	[Description("Positional fallback: with no mobile-template parent map, a positional sibling still routes to the default MainContainer.")]
	public void Analyze_ElementMap_PositionalSiblings_FallbackParentWhenAnchorParentUnknown() {
		PageBundleInfo bundle = Bundle("""
			[
			  { "name": "ProgressBarContainer", "type": "crt.FlexContainer", "items": [ { "name": "ProgressBar", "type": "crt.Input" } ] },
			  { "name": "CardContentWrapper", "type": "crt.GridContainer", "items": [
			      { "name": "Tabs", "type": "crt.TabPanel", "items": [] } ] }
			]
			""");
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.GridContainer", "crt.FlexContainer", "crt.Input"
		};
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["CardContentWrapper"] = "GeneralTabContainer", ["Tabs"] = "Tabs"
		};
		var placements = new List<WebToMobileAnalysisService.PositionalPlacement> { new("CardContentWrapper", "Tabs") };

		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, mobileTypes, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, GridRule, templateRule: null,
			sourcePage: "Leads_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrLeads_MobileFormPage", containerNameMap: map,
			positionalPlacements: placements, mobileContainerParents: null);

		ElementMapEntry progress = Element(guide, "ProgressBarContainer");
		progress.ParentName.Should().Be("MainContainer", because: "the anchor's mobile parent is unknown → default");
		progress.Index.Should().Be(0);
	}

	[Test]
	[Description("The template rule's positional (:top/:bottom) entries are parsed into placements (deduped by anchor) and excluded from the plain container-name map; a mobile bundle yields child→parent for anchor resolution.")]
	public void ContainerRule_PositionalEntries_ParsedAndExcludedFromNameMap() {
		var rule = new TemplateMappingRule {
			Web = "PageWithTabsFreedomTemplate",
			Mobile = "MobilePageWithTabsFreedomTemplate",
			Containers = [
				new ContainerMappingRule { Web = "Tabs", Mobile = "Tabs" },
				new ContainerMappingRule { Web = "CardContentWrapper", Mobile = "GeneralTabContainer" },
				new ContainerMappingRule { Web = "CardContentWrapper:top", Mobile = "Tabs:top" },
				new ContainerMappingRule { Web = "CardContentWrapper:bottom", Mobile = "Tabs:bottom" }
			]
		};

		IReadOnlyDictionary<string, string> nameMap = MobilePageConversionGuideTool.BuildContainerNameMap(rule);
		nameMap.Should().ContainKey("Tabs");
		nameMap.Should().ContainKey("CardContentWrapper");
		nameMap.Keys.Should().NotContain(k => k.Contains(':'), because: "positional entries are not element-name twins");

		IReadOnlyList<WebToMobileAnalysisService.PositionalPlacement> placements =
			MobilePageConversionGuideTool.BuildPositionalPlacements(rule);
		placements.Should().ContainSingle(because: ":top and :bottom of one anchor dedupe to a single placement");
		placements[0].WebAnchor.Should().Be("CardContentWrapper");
		placements[0].MobileAnchor.Should().Be("Tabs");

		var mobileTree = System.Text.Json.Nodes.JsonNode.Parse("""
			[ { "name": "MainContainer", "items": [ { "name": "Tabs", "items": [] } ] } ]
			""").AsArray();
		WebToMobileAnalysisService.CollectParentByName(mobileTree)["Tabs"].Should().Be("MainContainer");
	}

	[Test]
	[Description("Regression: elementMap is additive — a list-like page still produces componentSuggestions/containerMap and now an elementMap.")]
	public void Analyze_ElementMap_IsAdditive_ListPage() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "DataTable", "type": "crt.DataGrid" } ] } ]
			""");

		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Main"] = "MainContainer" };
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true)), templateRule: Rules.Templates[0], containerNameMap: map);

		guide.ContainerMap.Should().NotBeEmpty(because: "containerMap is unchanged (backward compatible)");
		guide.ComponentSuggestions.Should().Contain(s => s.SourceType == "crt.DataGrid");
		guide.ElementMap.Should().NotBeNull();
		Element(guide, "Main").Operation.Should().Be("merge");
		Element(guide, "DataTable").Operation.Should().Be("insert");
		Element(guide, "DataTable").MobileType.Should().Be("crt.List");
		Element(guide, "DataTable").ParentName.Should().Be("MainContainer");
	}

	// ── data sections (modelConfig / viewModelConfig) ─────────────────────────────────────────

	[Test]
	[Description("modelConfig is passed through verbatim, preserving lookup-path attribute types (ForwardReference) so the binding resolves in Mobile Designer.")]
	public void Analyze_ModelConfig_PassedThroughPreservingForwardReference() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "JobTitle", "type": "crt.Input", "value": "$QualifiedContactJobTitle" } ] } ]
			""",
			modelConfigJson: """
			{ "dataSources": { "PDS": { "config": { "attributes": {
				"QualifiedContactJobTitle": { "path": "QualifiedContact.JobTitle", "type": "ForwardReference" } } } } } }
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true)));

		guide.ModelConfig.Should().NotBeNull();
		string type = guide.ModelConfig!.AsObject()["dataSources"]!["PDS"]!["config"]!["attributes"]!
			["QualifiedContactJobTitle"]!["type"]!.GetValue<string>();
		type.Should().Be("ForwardReference", because: "modelConfig is passed through verbatim — attribute properties are preserved as-is");
		guide.Constraints.Should().Contain(c => c.Contains("VERBATIM") && c.Contains("modelConfig"));
		guide.NextSteps.Should().Contain(s => s.Contains("modelConfigDiff"));
	}

	[Test]
	[Description("viewModelConfig drops attributes referenced only by dropped components; keeps attributes with a surviving consumer or no consumer at all.")]
	public void Analyze_ViewModelConfig_DropsAttributesOfUnsupportedComponentsOnly() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "NameField", "type": "crt.Input", "value": "$AttrB" },
				{ "name": "Color", "type": "crt.ColorButton", "value": "$AttrA" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": {
				"AttrA": { "modelConfig": { "path": "PDS.SomeColumn" } },
				"AttrB": { "modelConfig": { "path": "PDS.Name" } },
				"AttrC": { "modelConfig": { "path": "PDS.Other" } } } }
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true)));

		Element(guide, "Color").Operation.Should().Be("drop", because: "crt.ColorButton is unsupported on mobile");
		guide.ViewModelConfig.Should().NotBeNull();
		JsonObject attrs = guide.ViewModelConfig!.AsObject()["attributes"]!.AsObject();
		attrs.ContainsKey("AttrA").Should().BeFalse(because: "referenced only by the dropped ColorButton");
		attrs.ContainsKey("AttrB").Should().BeTrue(because: "referenced by the surviving NameField");
		attrs.ContainsKey("AttrC").Should().BeTrue(because: "no consumer → kept");
	}

	[Test]
	[Description("An attribute a SURVIVING element only captions off via $Resources.Strings.<attr> is KEPT even when a dropped element also references it — the resource reference counts as a consumer (better to keep a spare attribute than drop a needed one).")]
	public void Analyze_ViewModelConfig_KeepsAttributeReferencedBySurvivingCaption() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Lookup", "type": "crt.Input", "label": "$Resources.Strings.LookupAttribute_ivqsxmp", "control": "$SomeControl" },
				{ "name": "Color", "type": "crt.ColorButton", "value": "$LookupAttribute_ivqsxmp" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": {
				"LookupAttribute_ivqsxmp": { "modelConfig": { "path": "PDS.QualifiedContact" } } } }
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true)));

		Element(guide, "Color").Operation.Should().Be("drop", because: "crt.ColorButton is unsupported on mobile");
		JsonObject attrs = guide.ViewModelConfig!.AsObject()["attributes"]!.AsObject();
		attrs.ContainsKey("LookupAttribute_ivqsxmp").Should().BeTrue(
			because: "the surviving Lookup field auto-captions off it via $Resources.Strings.<attr>, so it is still used");
	}

	[Test]
	[Description("The guide emits ready-to-paste modelConfigDiff/viewModelConfigDiff as a single root merge carrying the full config verbatim (attribute types preserved).")]
	public void Analyze_PrebuiltDiffs_RootMergeCarriesConfigVerbatim() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "JobTitle", "type": "crt.Input", "value": "$QualifiedContactJobTitle" } ] } ]
			""",
			modelConfigJson: """
			{ "dataSources": { "PDS": { "config": { "attributes": {
				"QualifiedContactJobTitle": { "path": "QualifiedContact.JobTitle", "type": "ForwardReference" } } } } } }
			""",
			viewModelConfigJson: """
			{ "attributes": { "QualifiedContactJobTitle": { "modelConfig": { "path": "PDS.QualifiedContactJobTitle" } } } }
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true)));

		// modelConfigDiff: single root merge that carries the attribute type verbatim.
		guide.ModelConfigDiff.Should().NotBeNull();
		JsonArray mcd = guide.ModelConfigDiff!.AsArray();
		mcd.Should().HaveCount(1);
		JsonObject op = mcd[0]!.AsObject();
		op["operation"]!.GetValue<string>().Should().Be("merge");
		op["path"]!.AsArray().Should().BeEmpty();
		op["values"]!["dataSources"]!["PDS"]!["config"]!["attributes"]!
			["QualifiedContactJobTitle"]!["type"]!.GetValue<string>().Should().Be("ForwardReference");

		// viewModelConfigDiff: single root merge carrying the (filtered) viewModelConfig.
		guide.ViewModelConfigDiff.Should().NotBeNull();
		JsonObject vop = guide.ViewModelConfigDiff!.AsArray()[0]!.AsObject();
		vop["operation"]!.GetValue<string>().Should().Be("merge");
		vop["values"]!["attributes"]!["QualifiedContactJobTitle"].Should().NotBeNull();
	}

	[Test]
	[Description("insert mobileValues carries the type, the field label, and every source property the mobile component supports; web-only props and the value binding are not carried.")]
	public void Analyze_FieldInsert_MobileValues_CarriesSupportedPropsAndLabel() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "LeadName", "type": "crt.Input", "caption": "$Resources.Strings.LeadName_caption",
				  "control": "$LeadName", "readonly": "$IsReadonly", "placeholder": "Enter name", "usrWebOnly": "x" },
				{ "name": "JobTitle", "type": "crt.Input", "value": "$QualifiedContactJobTitle" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "QualifiedContactJobTitle": { "modelConfig": { "path": "PDS.JobTitle" } } } }
			""",
			resourcesJson: """
			{ "LeadName_caption": { "en-US": "Lead name" } }
			""");
		var crtInput = new ComponentRegistryEntry {
			ComponentType = "crt.Input",
			Inputs = new Dictionary<string, JsonElement> {
				["label"] = JsonSerializer.SerializeToElement(new { }),
				["readonly"] = JsonSerializer.SerializeToElement(new { }),
				["placeholder"] = JsonSerializer.SerializeToElement(new { }),
				["control"] = JsonSerializer.SerializeToElement(new { })
			}
		};
		var mobileByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.Input"] = crtInput
		};
		// The web registry declares usrWebOnly (and not the mobile one) — that is what makes it droppable.
		var webByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.FlexContainer"] = new ComponentRegistryEntry { ComponentType = "crt.FlexContainer", Container = true },
			["crt.Input"] = new ComponentRegistryEntry {
				ComponentType = "crt.Input",
				Inputs = new Dictionary<string, JsonElement> { ["usrWebOnly"] = JsonSerializer.SerializeToElement(new { }) }
			}
		};

		MobilePageConversionGuide guide = Analyze(bundle, webByType: webByType, mobileByType: mobileByType);

		JsonObject leadVals = Element(guide, "LeadName").MobileValues!.AsObject();
		leadVals["type"]!.GetValue<string>().Should().Be("crt.Input");
		// Caption present → label references the registered <name>_caption resource.
		leadVals["label"]!.GetValue<string>().Should().Be("$Resources.Strings.LeadName_caption");
		// Every source property the mobile component supports is carried verbatim …
		leadVals.ContainsKey("readonly").Should().BeTrue(because: "readonly is a supported mobile input");
		leadVals.ContainsKey("placeholder").Should().BeTrue(because: "placeholder is a supported mobile input");
		// … a web-registry-specific prop the mobile component lacks is dropped, and the value binding is left out.
		leadVals.ContainsKey("usrWebOnly").Should().BeFalse(because: "the web registry declares it and mobile does not");
		leadVals.ContainsKey("control").Should().BeFalse(because: "the value binding is added by the caller, not prebuilt");

		// No caption but bound to PDS.JobTitle → auto-provided column-code label.
		Element(guide, "JobTitle").MobileValues!.AsObject()["label"]!.GetValue<string>().Should().Be("$Resources.Strings.JobTitle");
	}

	[Test]
	[Description("A carried property whose mobile registry input declares an object shape (type 'unknown' + object default) is coerced from the web one-element array to a single object; other props are untouched.")]
	public void Analyze_ListInsert_ItemLayoutArray_CoercedToObjectByRegistryShape() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "SimilarLeadList", "type": "crt.List", "items": "$SimilarLeadList",
				  "itemLayout": [ { "type": "crt.ListItem", "title": "$DS_LeadName",
				                    "body": [ { "value": "$DS_Status" } ] } ] } ] } ]
			""");
		var crtList = new ComponentRegistryEntry {
			ComponentType = "crt.List",
			Inputs = new Dictionary<string, JsonElement> {
				["items"] = JsonSerializer.SerializeToElement(new { }),
				// The mobile registry declares itemLayout with an UNKNOWN type and an OBJECT default —
				// the expected shape is inferred from the default (a map), so the web array must be unwrapped.
				["itemLayout"] = JsonSerializer.SerializeToElement(new {
					type = "unknown",
					@default = new { name = "'ListItem_' + GENERATE_GUID_MACRO", type = "crt.ListItem", body = Array.Empty<object>() }
				})
			}
		};
		var mobileByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.List"] = crtList
		};
		var webByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.FlexContainer"] = new ComponentRegistryEntry { ComponentType = "crt.FlexContainer", Container = true }
		};

		MobilePageConversionGuide guide = Analyze(bundle, webByType: webByType, mobileByType: mobileByType);

		JsonObject vals = Element(guide, "SimilarLeadList").MobileValues!.AsObject();
		vals["type"]!.GetValue<string>().Should().Be("crt.List");
		// itemLayout is now a single object (the array wrapper was dropped), carrying the row config.
		vals["itemLayout"]!.GetValueKind().Should().Be(JsonValueKind.Object);
		vals["itemLayout"]!.AsObject()["title"]!.GetValue<string>().Should().Be("$DS_LeadName");
		// The string collection binding is carried unchanged.
		vals["items"]!.GetValue<string>().Should().Be("$SimilarLeadList");
	}

	#region ConvertPageBusinessRules

	private static ElementMapEntry El(string web, string operation, string mobile = null) =>
		new() { WebName = web, Operation = operation, MobileName = mobile };

	private static SourcePageRuleAction ElementAction(string actionType, params string[] items) =>
		new() { ActionType = actionType, ElementItems = items.ToList() };

	private static SourcePageBusinessRule SourceRule(string caption, params SourcePageRuleAction[] actions) =>
		new() {
			Caption = caption,
			Condition = JsonNode.Parse("""{"logicalOperation":"AND","conditions":[]}"""),
			Actions = actions.ToList()
		};

	private static PageBusinessRuleProbeResult ProbeOf(params SourcePageBusinessRule[] rules) =>
		new() { ProbeOk = true, Rules = rules };

	[Test]
	[Description("An action on a surviving element converts; its item is remapped web→mobile and the condition is carried verbatim.")]
	public void ConvertPageBusinessRules_SurvivingElement_RemapsAndKeepsCondition() {
		PageBusinessRuleProbeResult probe = ProbeOf(
			SourceRule("Lock title", ElementAction("make-read-only", "UsrName")));
		var elementMap = new List<ElementMapEntry> { El("UsrName", "merge", "AreaName") };

		PageBusinessRuleConversionInfo result = WebToMobileAnalysisService.ConvertPageBusinessRules(probe, elementMap);

		result.DroppedRules.Should().BeEmpty();
		result.ConvertedRules.Should().HaveCount(1);
		ConvertedPageBusinessRule converted = result.ConvertedRules[0];
		JsonArray actions = converted.Rule!["actions"]!.AsArray();
		actions.Should().HaveCount(1);
		actions[0]!["type"]!.GetValue<string>().Should().Be("make-read-only");
		actions[0]!["items"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("AreaName");
		converted.Rule!["condition"].Should().NotBeNull();
	}

	[Test]
	[Description("Both visibility actions (hide-element / show-element) convert for surviving elements.")]
	public void ConvertPageBusinessRules_HideAndShowElement_Convert() {
		PageBusinessRuleProbeResult probe = ProbeOf(
			SourceRule("Toggle warning", ElementAction("hide-element", "Warn"), ElementAction("show-element", "Hint")));
		var elementMap = new List<ElementMapEntry> { El("Warn", "insert", "Warn"), El("Hint", "insert", "Hint") };

		PageBusinessRuleConversionInfo result = WebToMobileAnalysisService.ConvertPageBusinessRules(probe, elementMap);

		result.ConvertedRules.Should().HaveCount(1);
		JsonArray actions = result.ConvertedRules[0].Rule!["actions"]!.AsArray();
		actions.Select(a => a!["type"]!.GetValue<string>()).Should().Equal("hide-element", "show-element");
	}

	[Test]
	[Description("An action whose only referenced element drops on mobile makes the whole rule drop (with its condition).")]
	public void ConvertPageBusinessRules_DroppedElement_DropsRule() {
		PageBusinessRuleProbeResult probe = ProbeOf(
			SourceRule("Lock ghost", ElementAction("make-read-only", "GhostField")));
		var elementMap = new List<ElementMapEntry> { El("GhostField", "drop") };

		PageBusinessRuleConversionInfo result = WebToMobileAnalysisService.ConvertPageBusinessRules(probe, elementMap);

		result.ConvertedRules.Should().BeEmpty();
		result.DroppedRules.Should().HaveCount(1);
		result.DroppedRules[0].Caption.Should().Be("Lock ghost");
	}

	[Test]
	[Description("A multi-element action keeps only the surviving elements (web→mobile) and drops the rest.")]
	public void ConvertPageBusinessRules_MultiElementAction_KeepsSurvivingOnly() {
		PageBusinessRuleProbeResult probe = ProbeOf(
			SourceRule("Require pair", ElementAction("make-required", "Kept", "Gone")));
		var elementMap = new List<ElementMapEntry> {
			El("Kept", "insert", "Kept"),
			El("Gone", "drop")
		};

		PageBusinessRuleConversionInfo result = WebToMobileAnalysisService.ConvertPageBusinessRules(probe, elementMap);

		result.ConvertedRules.Should().HaveCount(1);
		JsonArray items = result.ConvertedRules[0].Rule!["actions"]!.AsArray()[0]!["items"]!.AsArray();
		items.Select(n => n!.GetValue<string>()).Should().Equal("Kept");
	}

	[Test]
	[Description("A condition operand referencing the source DS path (full 'DS.Column' or bare column) is remapped to the mobile viewModel attribute name; an unresolvable path is left as-is.")]
	public void ConvertPageBusinessRules_RemapsConditionOperandPathToAttributeName() {
		var probe = new PageBusinessRuleProbeResult {
			ProbeOk = true,
			Rules = [
				new SourcePageBusinessRule {
					Caption = "Hide account fields when account not filled in",
					Condition = JsonNode.Parse("""
						{ "logicalOperation": "AND", "conditions": [
							{ "leftExpression": { "type": "AttributeValue", "path": "PDS.QualifiedAccount" }, "comparisonType": "is-not-filled-in" },
							{ "leftExpression": { "type": "AttributeValue", "path": "QualifiedContact" }, "comparisonType": "is-not-filled-in" },
							{ "leftExpression": { "type": "AttributeValue", "path": "PDS.Unknownia" }, "comparisonType": "is-not-filled-in" } ] }
						"""),
					Actions = [ElementAction("hide-element", "AccountFieldsFlexContainer")]
				}
			]
		};
		var elementMap = new List<ElementMapEntry> {
			El("AccountFieldsFlexContainer", "insert", "AccountFieldsFlexContainer")
		};
		JsonNode viewModelConfig = JsonNode.Parse("""
			{ "attributes": {
				"Parameter_3pxm4wn": { "modelConfig": { "path": "PDS.QualifiedAccount" } },
				"Parameter_r8t9n2f": { "modelConfig": { "path": "PDS.QualifiedContact" } } } }
			""");

		PageBusinessRuleConversionInfo result =
			WebToMobileAnalysisService.ConvertPageBusinessRules(probe, elementMap, viewModelConfig);

		result.ConvertedRules.Should().HaveCount(1);
		JsonArray conditions = result.ConvertedRules[0].Rule!["condition"]!["conditions"]!.AsArray();
		// Full "DS.Column" path → attribute name.
		conditions[0]!["leftExpression"]!["path"]!.GetValue<string>().Should().Be("Parameter_3pxm4wn");
		// Bare column → attribute name.
		conditions[1]!["leftExpression"]!["path"]!.GetValue<string>().Should().Be("Parameter_r8t9n2f");
		// Unresolvable path → left untouched (condition still converts).
		conditions[2]!["leftExpression"]!["path"]!.GetValue<string>().Should().Be("PDS.Unknownia");
	}

	[Test]
	[Description("A failed probe yields a not-OK conversion info carrying the note; a null probe yields null.")]
	public void ConvertPageBusinessRules_ProbeFailedOrNull_DegradesGracefully() {
		PageBusinessRuleConversionInfo failed = WebToMobileAnalysisService.ConvertPageBusinessRules(
			new PageBusinessRuleProbeResult { ProbeOk = false, Note = "boom" }, new List<ElementMapEntry>());
		failed.ProbeOk.Should().BeFalse();
		failed.Note.Should().Be("boom");
		failed.ConvertedRules.Should().BeEmpty();

		WebToMobileAnalysisService.ConvertPageBusinessRules(null, new List<ElementMapEntry>()).Should().BeNull();
	}

	[Test]
	[Description("A condition operand of ANY type — including SysSetting (compare against a system setting) — converts verbatim; SysSetting is supported in a mobile page-rule condition, so the rule is never dropped for its condition.")]
	public void ConvertPageBusinessRules_SysSettingCondition_ConvertsVerbatim() {
		var probe = new PageBusinessRuleProbeResult {
			ProbeOk = true,
			Rules = [
				new SourcePageBusinessRule {
					Caption = "Show new analytics when setting on",
					Condition = JsonNode.Parse("""
						{ "logicalOperation": "AND", "conditions": [
							{ "leftExpression": { "type": "SysSetting" }, "comparisonType": "equal",
							  "rightExpression": { "type": "AttributeValue", "value": "1" } } ] }
						"""),
					Actions = [ElementAction("show-element", "OverviewNewAnalyticsContainer")]
				}
			]
		};
		var elementMap = new List<ElementMapEntry> {
			El("OverviewNewAnalyticsContainer", "insert", "OverviewNewAnalyticsContainer")
		};

		PageBusinessRuleConversionInfo result = WebToMobileAnalysisService.ConvertPageBusinessRules(probe, elementMap);

		result.DroppedRules.Should().BeEmpty();
		result.ConvertedRules.Should().HaveCount(1);
		JsonArray conditions = result.ConvertedRules[0].Rule!["condition"]!["conditions"]!.AsArray();
		conditions.Should().HaveCount(1);
		conditions[0]!["leftExpression"]!["type"]!.GetValue<string>().Should().Be("SysSetting",
			because: "the SysSetting operand is carried verbatim");
	}

	#endregion

	#region Template component pruning (read-time exclusion of inherited web-template chrome)

	private static IReadOnlySet<string> Names(params string[] names) =>
		new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

	[Test]
	[Description("Components inherited from the web template (its full chrome subtree) are excluded from the guide; the page's own delta is kept.")]
	public void Analyze_TemplateComponents_AreExcludedFromStructureAndElementMap() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "MainHeader", "type": "crt.FlexContainer", "items": [
					{ "name": "TitleContainer", "type": "crt.FlexContainer", "items": [
						{ "name": "BackButton", "type": "crt.Button" },
						{ "name": "PageTitle", "type": "crt.Label" } ] } ] },
				{ "name": "ContentContainer", "type": "crt.FlexContainer", "items": [
					{ "name": "UsrName", "type": "crt.Input" } ] } ] } ]
			""");
		var web = Reg(("crt.FlexContainer", true), ("crt.Input", false), ("crt.Button", false), ("crt.Label", false));
		// Everything the web template (and its bases) declares: the page-specific ContentContainer/UsrName are NOT here.
		IReadOnlySet<string> templateNames = Names("Main", "MainHeader", "TitleContainer", "BackButton", "PageTitle");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web, templateComponentNames: templateNames);

		foreach (string chrome in new[] { "Main", "MainHeader", "TitleContainer", "BackButton", "PageTitle" }) {
			guide.SourceStructure.Should().NotContain(s => s.Name == chrome, because: $"{chrome} is provided by the web template");
			guide.ElementMap.Should().NotContain(e => e.WebName == chrome);
		}
		// The page's own field survives (hoisted out of the dropped Main wrapper) and is converted.
		guide.SourceStructure.Should().Contain(s => s.Name == "UsrName");
		guide.ElementMap.Should().Contain(e => e.WebName == "UsrName" && e.Operation == "insert");
		// The advisory constraint announces the exclusion.
		guide.Constraints.Should().Contain(c => c.Contains("inherited from the source page's web template"));
	}

	[Test]
	[Description("A container twin listed in the containerMap is kept even though it is in the template baseline (it is the merge target); its application children survive.")]
	public void Analyze_TemplateTwinInContainerMap_IsKeptNotPruned() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.Tabs", "items": [
				{ "name": "UsrName", "type": "crt.Input" } ] } ]
			""");
		var web = Reg(("crt.Tabs", true), ("crt.Input", false));
		var containerNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tabs"] = "Tabs" };

		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: web, containerNameMap: containerNameMap,
			templateComponentNames: Names("Tabs"));

		guide.SourceStructure.Should().Contain(s => s.Name == "Tabs", because: "a containerMap twin is a merge target, not chrome");
		guide.ElementMap.Should().Contain(e => e.WebName == "Tabs" && e.Operation == "merge");
		guide.ElementMap.Should().Contain(e => e.WebName == "UsrName");
	}

	[Test]
	[Description("With no template baseline the tree is untouched (backward-compatible): a would-be-chrome element is still surfaced.")]
	public void Analyze_NoTemplateBaseline_LeavesTreeUnchanged() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "MainHeader", "type": "crt.FlexContainer", "items": [
				{ "name": "UsrName", "type": "crt.Input" } ] } ]
			""");
		var web = Reg(("crt.FlexContainer", true), ("crt.Input", false));

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web, templateComponentNames: null);

		guide.SourceStructure.Should().Contain(s => s.Name == "MainHeader");
		guide.Constraints.Should().NotContain(c => c.Contains("inherited from the source page's web template"));
	}

	[Test]
	[Description("CollectComponentNames gathers every named node across the nested tree (case-insensitive set).")]
	public void CollectComponentNames_GathersAllNestedNames() {
		JsonArray tree = JsonNode.Parse("""
			[ { "name": "Root", "items": [
				{ "name": "Header", "items": [ { "name": "Title" } ] },
				{ "type": "crt.Anonymous" },
				{ "name": "Body" } ] } ]
			""")!.AsArray();

		HashSet<string> names = WebToMobileAnalysisService.CollectComponentNames(tree);

		names.Should().BeEquivalentTo("Root", "Header", "Title", "Body");
	}

	[Test]
	[Description("A component mapped in the template's components block (DataTable→List) is KEPT through baseline subtraction and recorded as a merge-by-name twin; no duplicate is inserted. clio adds no component-specific transform — the row how-to is type-driven and surfaced in componentSuggestions.")]
	public void Analyze_TemplateComponentTwin_IsKeptAndMergedByName_NoHardcodedTransform() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "ListContainer", "type": "crt.FlexContainer", "items": [
				{ "name": "DataTable", "type": "crt.DataGrid", "columns": [
					{ "code": "PDS_LeadName", "sticky": true },
					{ "code": "PDS_Status", "path": "Status", "referenceSchemaName": "LeadStatus" } ] } ] } ]
			""");
		var web = Reg(("crt.FlexContainer", true), ("crt.DataGrid", false));
		var containerNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ListContainer"] = "ListContainer" };
		var componentNameMap = new Dictionary<string, ComponentMappingRule>(StringComparer.OrdinalIgnoreCase) {
			["DataTable"] = new ComponentMappingRule { Web = "DataTable", Mobile = "List", Note = "Primary list component." }
		};
		// DataTable is provided by the web list template (it is in the baseline). Without the components map it
		// would be pruned as chrome; the map keeps it so it can be converted.
		IReadOnlySet<string> templateNames = Names("ListContainer", "DataTable");

		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: web, containerNameMap: containerNameMap,
			templateComponentNames: templateNames, componentNameMap: componentNameMap);

		// Kept (not pruned) and surfaced in the structure.
		guide.SourceStructure.Should().Contain(s => s.Name == "DataTable");
		// Recorded as a single merge-by-name twin into the template-provided mobile element.
		ElementMapEntry twin = guide.ElementMap.Single(e => e.WebName == "DataTable");
		twin.Operation.Should().Be("merge");
		twin.MobileName.Should().Be("List");
		// No component-specific values are prebuilt by clio; the how-to is delegated to componentSuggestions.
		twin.MobileValues.Should().BeNull();
		twin.Reason.Should().Contain("Primary list component.").And.Contain("componentSuggestions");
		// No duplicate insert for the grid; the conversion detail lives in the general components rule.
		guide.ElementMap.Should().NotContain(e => e.WebName == "DataTable" && e.Operation == "insert");
		guide.ComponentSuggestions.Should().Contain(s => s.SourceType == "crt.DataGrid");
	}

	#endregion

	#region Request (action) conversion

	private static readonly IReadOnlySet<string> RequestMobileTypes =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crt.Button", "crt.FlexContainer" };

	private static readonly WebToMobilePageConversionRules RequestRules = new() {
		Requests = [
			new RequestMappingRule { Web = "crt.SaveRecordRequest", Mobile = "crt.SaveRecordRequest", Category = "DirectMapping" },
			new RequestMappingRule { Web = "crt.PrintablesRequest", Mobile = null, Category = "Unsupported", Note = "Printables are web-only." },
			new RequestMappingRule { Web = "crt.LegacyOpenRequest", Mobile = "crt.OpenPageRequest", Category = "WithAdaptation" },
			// Optimistically mapped by the rules, but NOT in the authoritative mobile-supported set.
			new RequestMappingRule { Web = "crt.QuickFilterRequest", Mobile = "crt.QuickFilterRequest", Category = "DirectMapping" }
		]
	};

	private static MobilePageConversionGuide AnalyzeRequests(PageBundleInfo bundle) =>
		WebToMobileAnalysisService.Analyze(
			bundle, RequestMobileTypes, WebTypes,
			webByType: Reg(("crt.FlexContainer", true)),
			mobileByType: null,
			RequestRules, templateRule: null,
			sourcePage: "UsrApp_FormPage", sourceTemplate: null,
			suggestedTarget: "UsrApp_MobileFormPage", containerNameMap: null);

	private static PageBundleInfo ButtonBundle(string buttonName, string request, string @params = """{ "preventCardClose": false }""") =>
		Bundle($$"""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "{{buttonName}}", "type": "crt.Button", "caption": "Act",
				  "clicked": { "request": "{{request}}", "params": {{@params}} } } ] } ]
			""");

	private static JsonObject ClickedOf(MobilePageConversionGuide guide, string buttonName) =>
		Element(guide, buttonName).MobileValues!.AsObject();

	[Test]
	[Description("A supported event-binding request is kept in mobileValues with the same request, params preserved, and recorded as converted.")]
	public void Analyze_SupportedRequest_KeptAndRecorded() {
		MobilePageConversionGuide guide = AnalyzeRequests(ButtonBundle("SaveButton", "crt.SaveRecordRequest"));

		JsonObject vals = ClickedOf(guide, "SaveButton");
		JsonObject clicked = vals["clicked"]!.AsObject();
		clicked["request"]!.GetValue<string>().Should().Be("crt.SaveRecordRequest");
		clicked["params"]!["preventCardClose"]!.GetValue<bool>().Should().BeFalse(because: "params are carried verbatim");

		guide.RequestConversions.Should().NotBeNull();
		guide.RequestConversions!.ConvertedRequests.Should().ContainSingle(r =>
			r.ElementName == "SaveButton" && r.Binding == "clicked"
			&& r.WebRequest == "crt.SaveRecordRequest" && r.MobileRequest == "crt.SaveRecordRequest");
		guide.RequestConversions.DroppedRequests.Should().BeEmpty();
		guide.RequestConversions.FlaggedRequests.Should().BeEmpty();
	}

	[Test]
	[Description("A component whose event-binding request is not supported on mobile (and does not remap to a supported one) is DROPPED entirely — not shipped with a dead action.")]
	public void Analyze_UnsupportedRequest_ComponentDropped() {
		MobilePageConversionGuide guide = AnalyzeRequests(ButtonBundle("PrintButton", "crt.PrintablesRequest"));

		ElementMapEntry entry = Element(guide, "PrintButton");
		entry.Operation.Should().Be("drop");
		entry.Reason.Should().Contain("crt.PrintablesRequest");
	}

	[Test]
	[Description("A component with an unknown/custom request (not in the supported set and not remapped) is DROPPED.")]
	public void Analyze_UnknownRequest_ComponentDropped() {
		MobilePageConversionGuide guide = AnalyzeRequests(ButtonBundle("CustomButton", "usr.MyCustomRequest"));

		Element(guide, "CustomButton").Operation.Should().Be("drop");
	}

	[Test]
	[Description("The authoritative mobile-supported set overrides an optimistic rules DirectMapping: a request the rules map keeps 1:1 but that is NOT actually supported on mobile still drops the component.")]
	public void Analyze_OptimisticDirectMappingNotSupportedOnMobile_ComponentDropped() {
		MobilePageConversionGuide guide = AnalyzeRequests(ButtonBundle("FilterButton", "crt.QuickFilterRequest"));

		Element(guide, "FilterButton").Operation.Should().Be("drop");
	}

	[Test]
	[Description("A request whose mobile name differs is remapped in mobileValues (params verbatim) and recorded with both web and mobile names.")]
	public void Analyze_RenamedRequest_RemappedInBinding() {
		MobilePageConversionGuide guide = AnalyzeRequests(ButtonBundle("OpenButton", "crt.LegacyOpenRequest"));

		JsonObject clicked = ClickedOf(guide, "OpenButton")["clicked"]!.AsObject();
		clicked["request"]!.GetValue<string>().Should().Be("crt.OpenPageRequest");
		clicked["params"]!["preventCardClose"]!.GetValue<bool>().Should().BeFalse();

		guide.RequestConversions!.ConvertedRequests.Should().ContainSingle(r =>
			r.WebRequest == "crt.LegacyOpenRequest" && r.MobileRequest == "crt.OpenPageRequest");
	}

	[Test]
	[Description("A page with no event-binding requests yields a null requestConversions section.")]
	public void Analyze_NoRequests_RequestConversionsNull() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Plain", "type": "crt.Button", "caption": "Act" } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeRequests(bundle);

		guide.RequestConversions.Should().BeNull();
	}

	#endregion

	#region Adaptive (per-breakpoint) layout

	private static JsonObject AdaptiveOf(MobilePageConversionGuide guide, string fieldName) =>
		Element(guide, fieldName).MobileValues!.AsObject()["layoutConfig"]!.AsObject()["adaptive"]!.AsObject();

	[Test]
	[Description("A multi-column crt.GridContainer converts ONLY the phone (small) breakpoint to a single column; medium/large keep the web column count and each child's web placement — baked into both the container's and the children's mobileValues.")]
	public void Analyze_MultiColumnGrid_ConvertsSmallToOneColumn_KeepsWebForTablet() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "OverviewFieldsContainer", "type": "crt.GridContainer",
			    "columns": [ "minmax(32px, 1fr)", "minmax(32px, 1fr)" ], "items": [
				{ "name": "Name", "type": "crt.Input", "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 } },
				{ "name": "CreatedOn", "type": "crt.Input", "layoutConfig": { "column": 2, "row": 1, "colSpan": 1, "rowSpan": 1 } } ] } ]
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.GridContainer", true), ("crt.Input", false)));

		AdaptiveLayoutGroup group = guide.AdaptiveLayout!.Single();
		group.ContainerName.Should().Be("OverviewFieldsContainer");
		group.ColumnsByBreakpoint["small"].Should().Equal("1fr");
		group.ColumnsByBreakpoint["medium"].Should().Equal("1fr", "1fr");

		// Container-side adaptive is baked into the container's OWN mobileValues (deterministic).
		JsonObject container = Element(guide, "OverviewFieldsContainer").MobileValues!.AsObject()["adaptive"]!.AsObject();
		container["small"]!["columns"]!.AsArray().Should().HaveCount(1);
		container["medium"]!["columns"]!.AsArray().Should().HaveCount(2);
		container["large"]!["columns"]!.AsArray().Should().HaveCount(2);

		// Child CreatedOn (2nd): phone stacks (col 1, row 2); tablet/desktop keep the web cell (col 2, row 1).
		JsonObject co = AdaptiveOf(guide, "CreatedOn");
		co["small"]!["column"]!.GetValue<int>().Should().Be(1);
		co["small"]!["row"]!.GetValue<int>().Should().Be(2);
		co["medium"]!["column"]!.GetValue<int>().Should().Be(2);
		co["medium"]!["row"]!.GetValue<int>().Should().Be(1);
		co["large"]!["column"]!.GetValue<int>().Should().Be(2);
		// The child's layoutConfig is the adaptive form ONLY (base placement folded into medium/large).
		Element(guide, "CreatedOn").MobileValues!.AsObject()["layoutConfig"]!.AsObject()
			.Select(kv => kv.Key).Should().Equal("adaptive");
	}

	[Test]
	[Description("A single-column crt.GridContainer gets NO adaptive (the mobile client renders the plain config); its children keep the carried base layoutConfig, not an adaptive one.")]
	public void Analyze_SingleColumnGrid_NoAdaptive() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "OneColGrid", "type": "crt.GridContainer", "columns": [ "1fr" ], "items": [
				{ "name": "FieldA", "type": "crt.Input", "layoutConfig": { "column": 1, "row": 1 } },
				{ "name": "FieldB", "type": "crt.Input", "layoutConfig": { "column": 1, "row": 2 } } ] } ]
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.GridContainer", true), ("crt.Input", false)));

		guide.AdaptiveLayout.Should().BeNull();
		JsonObject lc = Element(guide, "FieldB").MobileValues!.AsObject()["layoutConfig"]!.AsObject();
		lc.ContainsKey("adaptive").Should().BeFalse("a 1-column grid needs no adaptive");
		lc["column"]!.GetValue<int>().Should().Be(1, "the carried base placement is kept as-is");
		lc["row"]!.GetValue<int>().Should().Be(2);
	}

	[Test]
	[Description("A property in NEITHER registry (system/framework prop, e.g. layoutConfig) is carried verbatim; a property the WEB registry declares but the MOBILE registry lacks is dropped; a mobile-supported property is carried.")]
	public void Analyze_SystemProp_Carried_WebSpecificProp_Dropped() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Widget", "type": "crt.Input",
				  "layoutConfig": { "column": 2, "row": 1 },
				  "webOnlyProp": true,
				  "readonly": true } ] } ]
			""");
		var webByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.FlexContainer"] = new ComponentRegistryEntry { ComponentType = "crt.FlexContainer", Container = true },
			["crt.Input"] = new ComponentRegistryEntry {
				ComponentType = "crt.Input",
				Inputs = new Dictionary<string, JsonElement> {
					["webOnlyProp"] = JsonSerializer.SerializeToElement(new { }),
					["readonly"] = JsonSerializer.SerializeToElement(new { })
				}
			}
		};
		var mobileByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.Input"] = new ComponentRegistryEntry {
				ComponentType = "crt.Input",
				Inputs = new Dictionary<string, JsonElement> { ["readonly"] = JsonSerializer.SerializeToElement(new { }) }
			}
		};

		MobilePageConversionGuide guide = Analyze(bundle, webByType: webByType, mobileByType: mobileByType);

		JsonObject values = Element(guide, "Widget").MobileValues!.AsObject();
		values.Should().ContainKey("layoutConfig", "layoutConfig is declared by neither registry — a system property");
		values.Should().ContainKey("readonly", "the mobile registry declares it");
		values.Should().NotContainKey("webOnlyProp", "the web registry declares it and the mobile registry does not");
	}

	#endregion

	#region Captions (localized resources)

	[Test]
	[Description("A non-field caption (a resource token in any form) is carried into mobileValues VERBATIM (a system property), and its referenced resource is resolved so the caller can register it: captionResource.key is the token's key, sourceValue its en-US text.")]
	public void Analyze_NonFieldCaption_CarriedVerbatimAndResourceResolved() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "ContactLabel", "type": "crt.Label",
				  "caption": "#MacrosTemplateString(#ResourceString(ContactLabel_caption)#)#" } ] } ]
			""",
			resourcesJson: """
			{ "ContactLabel_caption": { "en-US": "Contact person" } }
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.Label", false)));

		ElementMapEntry label = Element(guide, "ContactLabel");
		// caption carried verbatim (its original web token) — no hardcoded exclusion or normalization.
		label.MobileValues!.AsObject()["caption"]!.GetValue<string>()
			.Should().Be("#MacrosTemplateString(#ResourceString(ContactLabel_caption)#)#");
		// the referenced resource is resolved so the caller registers the SAME key the token uses.
		label.CaptionResource!.Key.Should().Be("ContactLabel_caption");
		label.CaptionResource.SourceValue.Should().Be("Contact person");
	}

	[Test]
	[Description("A caption that is a data binding ($HeaderCaption) is carried verbatim (a system property) but yields no captionResource — there is no resource to register.")]
	public void Analyze_DataBindingCaption_CarriedButNotAResource() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "TitleLabel", "type": "crt.Label", "caption": "$HeaderCaption" } ] } ]
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.Label", false)));

		ElementMapEntry label = Element(guide, "TitleLabel");
		label.CaptionResource.Should().BeNull();
		label.MobileValues!.AsObject()["caption"]!.GetValue<string>().Should().Be("$HeaderCaption");
	}

	[Test]
	[Description("Localized strings referenced ANYWHERE in an element's carried values — including NESTED ones (config.title, text.template) — are collected and resolved into guide.resourceStrings for registration, and the tokens stay verbatim in mobileValues.")]
	public void Analyze_NestedResourceStrings_CollectedIntoResourceStrings() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "EmailsSentNewMetric", "type": "crt.IndicatorWidget",
				  "caption": "#ResourceString(EmailsSentNewMetric_caption)#",
				  "config": { "title": "#ResourceString(EmailsSentNewMetric_title)#",
				              "text": { "template": "#ResourceString(EmailsSentNewMetric_template)#" } } } ] } ]
			""",
			resourcesJson: """
			{
			  "EmailsSentNewMetric_caption": { "en-US": "Emails sent metric" },
			  "EmailsSentNewMetric_title": { "en-US": "Emails sent" },
			  "EmailsSentNewMetric_template": { "en-US": "{0} sent" }
			}
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.IndicatorWidget", false)));

		guide.ResourceStrings.Should().NotBeNull();
		guide.ResourceStrings!["EmailsSentNewMetric_title"].Should().Be("Emails sent", "a NESTED config.title token must be collected");
		guide.ResourceStrings["EmailsSentNewMetric_template"].Should().Be("{0} sent", "a deeply nested text.template token must be collected");
		guide.ResourceStrings["EmailsSentNewMetric_caption"].Should().Be("Emails sent metric");
		// tokens stay verbatim in the carried values.
		Element(guide, "EmailsSentNewMetric").MobileValues!.ToJsonString()
			.Should().Contain("#ResourceString(EmailsSentNewMetric_title)#");
	}

	[Test]
	[Description("Caption key collision: a web tab bound to an INHERITED key (OverviewTab → GeneralInfoTab_caption, a key the mobile template owns with a different value) is re-keyed to the element-unique OverviewTab_caption — the token, captionResource.Key and resourceStrings all use it, so update-page registers it and the web value ('Overview') renders instead of the template's 'Details'.")]
	public void Analyze_CaptionKeyCollision_RekeyedToElementUniqueKey() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "caption": "#ResourceString(GeneralInfoTab_caption)#", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""",
			resourcesJson: """
			{ "GeneralInfoTab_caption": { "en-US": "Overview" } }
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle);

		ElementMapEntry overview = Element(guide, "OverviewTab");
		overview.Operation.Should().Be("insert");
		overview.CaptionResource!.Key.Should().Be("OverviewTab_caption",
			because: "re-keyed to the element, not the inherited GeneralInfoTab_caption");
		overview.CaptionResource.SourceValue.Should().Be("Overview");
		overview.MobileValues!.AsObject()["caption"]!.GetValue<string>()
			.Should().Be("#ResourceString(OverviewTab_caption)#");
		guide.ResourceStrings!["OverviewTab_caption"].Should().Be("Overview");
		guide.ResourceStrings.Should().NotContainKey("GeneralInfoTab_caption",
			because: "the converter never registers the colliding template-owned key");
	}

	[Test]
	[Description("No collision: a caption whose source key already matches the element (SalesTab → SalesTab_caption) keeps its source token verbatim (wrappers preserved) and is registered unchanged.")]
	public void Analyze_CaptionKeyMatchesElement_TokenKeptVerbatim() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "SalesTab", "type": "crt.TabContainer", "caption": "#MacrosTemplateString(#ResourceString(SalesTab_caption)#)#", "items": [
					{ "name": "Budget", "type": "crt.Input" } ] } ] } ]
			""",
			resourcesJson: """
			{ "SalesTab_caption": { "en-US": "Sales" } }
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle);

		Element(guide, "SalesTab").CaptionResource!.Key.Should().Be("SalesTab_caption");
		Element(guide, "SalesTab").MobileValues!.AsObject()["caption"]!.GetValue<string>()
			.Should().Be("#MacrosTemplateString(#ResourceString(SalesTab_caption)#)#",
				because: "the key already matches the element, so the source token (with its wrapper) is kept verbatim");
		guide.ResourceStrings!["SalesTab_caption"].Should().Be("Sales");
	}

	[Test]
	[Description("`items` as a STRING is a real collection binding and is carried into mobileValues (e.g. crt.CommunicationOptions items: \"$Attr\"); `items` as an ARRAY of child elements is structural and is not carried (the tree walk emits the children).")]
	public void Analyze_ItemsStringBinding_IsCarried_NotTreatedAsStructuralChildren() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "ContactCommunicationOptions", "type": "crt.CommunicationOptions",
				  "items": "$CommunicationOptions_f87c6ae", "columnsCount": 1, "masterRecordColumnName": "Contact" } ] } ]
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.CommunicationOptions", false)));

		JsonObject vals = Element(guide, "ContactCommunicationOptions").MobileValues!.AsObject();
		vals["items"]!.GetValue<string>().Should().Be("$CommunicationOptions_f87c6ae", "a string items binding is a real collection property, not structural children");
		vals.Should().ContainKey("columnsCount");
		vals.Should().ContainKey("masterRecordColumnName");
	}

	[Test]
	[Description("A field's OWN web label (e.g. $Resources.Strings.<attribute>, which auto-resolves to the bound column caption) is carried verbatim and NOT overwritten with a synthesized column-code key — that overwrite is only a fallback for fields with no label.")]
	public void Analyze_FieldWithWebLabel_CarriesItVerbatim_NotOverwritten() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "QualifiedContact", "type": "crt.Input",
				  "label": "$Resources.Strings.Parameter_r8t9n2f", "control": "$Parameter_r8t9n2f" } ] } ]
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.Input", false)));

		Element(guide, "QualifiedContact").MobileValues!.AsObject()["label"]!.GetValue<string>()
			.Should().Be("$Resources.Strings.Parameter_r8t9n2f", "the field's own web label must survive, not be replaced by a guessed key");
	}

	#endregion
}
