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
			"crt.Input", "crt.Toggle", "crt.RichTextEditor", "crt.List", "crt.FolderTreeActions", "crt.GridContainer", "crt.Label", "crt.IndicatorWidget", "crt.CommunicationOptions", "crt.QuickFilter"
		};

	private static readonly IReadOnlySet<string> WebTypes =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.Input", "crt.Checkbox", "crt.HtmlEditor", "crt.DataGrid", "crt.DataTable",
			"crt.ColorButton", "crt.FolderTree", "crt.FolderTreeActions", "crt.QuickFilter"
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
		IReadOnlyDictionary<string, ComponentMappingRule> componentNameMap = null,
		IReadOnlyDictionary<string, JsonArray> mobileTemplateArraysByPath = null,
		bool mobileTemplateArraysUnavailable = false,
		IReadOnlySet<string> mobileTemplateCollectionKeys = null,
		IReadOnlyDictionary<string, JsonArray> mobileTemplateModelArraysByPath = null) =>
		WebToMobileAnalysisService.Analyze(
			bundle, MobileTypes, WebTypes,
			webByType ?? new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType,
			Rules, templateRule,
			sourcePage: "UsrApp_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrApp_MobileFormPage", containerNameMap: containerNameMap,
			templateComponentNames: templateComponentNames,
			componentNameMap: componentNameMap,
			mobileTemplateArraysByPath: mobileTemplateArraysByPath,
			mobileTemplateArraysUnavailable: mobileTemplateArraysUnavailable,
			mobileTemplateCollectionKeys: mobileTemplateCollectionKeys,
			mobileTemplateModelArraysByPath: mobileTemplateModelArraysByPath);

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
			"crt.TabContainer", "crt.Input", "crt.ComboBox", "crt.DateTimeEdit", "crt.Feed", "crt.AttachmentList",
			"crt.ExpansionPanel", "crt.GridContainer"
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
		IReadOnlyDictionary<string, string> mobileContainerParents = null,
		WebToMobilePageConversionRules rules = null) =>
		WebToMobileAnalysisService.Analyze(
			bundle, TabbedMobileTypes,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crt.DataGrid", "crt.IndicatorWidget", "crt.Timeline" },
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, rules ?? GridRule, templateRule: null,
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

		// Empty tabs are still inserted HERE because these rules carry no emptyContainerRemoval section —
		// the removal pass is switched by data (see the "Empty container removal" region for the on-state).
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
	[Description("Both diffs are SPLIT into focused targeted merges with no path-[] root merge remaining: modelConfigDiff (no arrays here) becomes a single [\"dataSources\"] merge carrying the attribute type verbatim; viewModelConfigDiff's page-owned attribute lands in an [\"attributes\"] merge.")]
	public void Analyze_PrebuiltDiffs_BothConfigsSplitIntoTargetedMerges() {
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

		// modelConfigDiff: the whole-config root merge is split — with no arrays, its single top-level key
		// becomes a focused ["dataSources"] merge (no path-[] operation) carrying the attribute type verbatim.
		guide.ModelConfigDiff.Should().NotBeNull();
		JsonArray mcd = guide.ModelConfigDiff!.AsArray();
		mcd.Should().NotContain(n => n!.AsObject()["path"]!.AsArray().Count == 0,
			because: "the whole-config root merge is split into targeted merges");
		JsonObject op = mcd.Single(n =>
			n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>()).SequenceEqual(new[] { "dataSources" }))!.AsObject();
		op["operation"]!.GetValue<string>().Should().Be("merge");
		op["values"]!["PDS"]!["config"]!["attributes"]!
			["QualifiedContactJobTitle"]!["type"]!.GetValue<string>().Should().Be("ForwardReference");

		// viewModelConfigDiff: the whole-config root merge is SPLIT into targeted merges — the page-owned
		// attribute lands in a focused ["attributes"] merge and no path-[] operation remains.
		guide.ViewModelConfigDiff.Should().NotBeNull();
		JsonArray vcd = guide.ViewModelConfigDiff!.AsArray();
		vcd.Should().NotContain(n => n!.AsObject()["path"]!.AsArray().Count == 0,
			because: "the whole-config root merge is split into targeted merges");
		JsonObject vop = vcd.Single(n =>
			n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>()).SequenceEqual(new[] { "attributes" }))!.AsObject();
		vop["operation"]!.GetValue<string>().Should().Be("merge");
		vop["values"]!["QualifiedContactJobTitle"].Should().NotBeNull();
	}

	[Test]
	[Description("A converted quick filter's _Items attribute is wired into the list collection's template-owned modelConfig.filterAttributes. The single root merge is SPLIT into focused targeted merges (no path-[] operation remains): the template-owned Items collection's filterAttributes becomes a TARGETED merge at [attributes,Items,modelConfig] carrying the full array (template natives + quick filters), and the page-owned QuickFilter_x_Items attribute lands in the [\"attributes\"] merge. The mobile diff engine replaces arrays on a root merge, so the template baseline would otherwise win and drop the quick filter; the targeted merge overrides the baseline.")]
	public void Analyze_QuickFilter_FilterAttributesHoistedToTargetedMerge() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" },
				{ "name": "QuickFilter_x", "type": "crt.QuickFilter", "filterType": "lookup",
				  "config": { "caption": "Category", "entitySchemaName": "ProductCategory" },
				  "_filterOptions": { "from": "QuickFilter_x_Value", "expose": [
					{ "attribute": "QuickFilter_x_Items", "converters": [
					  { "converter": "crt.QuickFilterAttributeConverter", "args": [
						{ "target": { "viewAttributeName": "Items", "filterColumn": "Category" }, "quickFilterType": "lookup" } ] } ] } ] } } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": {
				"Items": { "isCollection": true, "modelConfig": { "path": "PDS", "filterAttributes": [
					{ "name": "QuickFilterGroup_Filters", "loadOnChange": true },
					{ "name": "QuickFilter_x_Items", "loadOnChange": true } ] } },
				"QuickFilter_x_Items": { "from": "QuickFilter_x_Value" } } }
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false), ("crt.QuickFilter", false)));

		JsonArray diff = guide.ViewModelConfigDiff!.AsArray();

		// The root merge is fully split — no path-[] operation remains.
		diff.Should().NotContain(n => n!.AsObject()["path"]!.AsArray().Count == 0);

		// The page-owned quick-filter _Items attribute lands in the focused ["attributes"] merge; the
		// template-owned Items collection is NOT dumped there (it is split into targeted merges instead).
		JsonObject bucket = diff.Single(n =>
			n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>()).SequenceEqual(new[] { "attributes" }))!.AsObject();
		bucket["values"]!["QuickFilter_x_Items"].Should().NotBeNull();
		bucket["values"]!.AsObject().Should().NotContainKey("Items");

		// A targeted merge at [attributes,Items,modelConfig] carries the FULL array (template native
		// QuickFilterGroup_Filters + the converted QuickFilter_x_Items), overriding the template baseline.
		JsonObject targeted = diff.Single(n =>
			n!.AsObject()["operation"]!.GetValue<string>() == "merge"
			&& n.AsObject()["path"]!.AsArray().Count == 3)!.AsObject();
		targeted["path"]!.AsArray().Select(n => n!.GetValue<string>())
			.Should().Equal("attributes", "Items", "modelConfig");
		targeted["values"]!["filterAttributes"]!.AsArray().Select(n => n!["name"]!.GetValue<string>())
			.Should().Contain("QuickFilterGroup_Filters").And.Contain("QuickFilter_x_Items");
	}

	[Test]
	[Description("A template-owned collection whose only viewModelConfig content is template-inherited scalars (no arrays, no added attributes) contributes nothing to the split — its scalars are dropped and viewModelConfigDiff ends up empty (no page-specific change to merge).")]
	public void Analyze_CollectionWithOnlyTemplateScalars_ViewModelConfigDiffEmpty() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "Items": { "isCollection": true, "modelConfig": { "path": "PDS" } } } }
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)));

		JsonArray diff = guide.ViewModelConfigDiff!.AsArray();
		diff.Should().BeEmpty(because: "the collection carries only template-owned scalars — nothing page-specific to merge");
	}

	[Test]
	[Description("An array that is NOT under an attribute's modelConfig (e.g. a combobox's own static default 'value' list) is never owned by the mobile template, so it is left inline on its page-owned attribute in the [\"attributes\"] merge instead of being hoisted into its own targeted merge — hoisting it would only fragment the diff without fixing anything.")]
	public void Analyze_ArrayOutsideModelConfig_IsNotHoisted_StaysInAttributesMerge() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "UsrOptions", "type": "crt.Input" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": {
				"UsrOptions": { "modelConfig": { "path": "PDS.UsrOptions" },
					"value": [ "Option1", "Option2" ] } } }
			""");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.Input", false)));

		JsonArray diff = guide.ViewModelConfigDiff!.AsArray();
		diff.Should().HaveCount(1, because: "one page-owned attribute → a single [\"attributes\"] merge; the 'value' array is not under modelConfig so it is not hoisted");
		JsonObject bucket = diff[0]!.AsObject();
		bucket["path"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("attributes");
		bucket["values"]!["UsrOptions"]!["value"]!.AsArray()
			.Select(n => n!.GetValue<string>()).Should().Equal("Option1", "Option2");
	}

	[Test]
	[Description("When the mobile template's own filterAttributes array is supplied (mobileTemplateArraysByPath), the hoisted targeted merge UNIONS the template natives with the page's converted entries — natives first, page entries after — so the template baseline is preserved instead of being replaced.")]
	public void Analyze_TemplateNativesSupplied_TargetedMergeUnionsNativesWithPageEntries() {
		// Arrange: the page carries ONLY its own converted quick-filter entry; the template's native
		// entry (QuickFilterGroup_Filters) is provided separately via mobileTemplateArraysByPath.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "Items": { "isCollection": true, "modelConfig": { "path": "PDS",
				"filterAttributes": [ { "name": "QuickFilter_x_Items", "loadOnChange": true } ] } } } }
			""");
		var natives = new Dictionary<string, JsonArray>(StringComparer.OrdinalIgnoreCase) {
			["Items/modelConfig/filterAttributes"] =
				JsonNode.Parse("""[ { "name": "QuickFilterGroup_Filters", "loadOnChange": true } ]""")!.AsArray()
		};

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)),
			mobileTemplateArraysByPath: natives);

		// Assert
		JsonArray diff = guide.ViewModelConfigDiff!.AsArray();
		JsonObject targeted = diff.Single(n =>
			n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>())
				.SequenceEqual(new[] { "attributes", "Items", "modelConfig" }))!.AsObject();
		targeted["values"]!["filterAttributes"]!.AsArray().Select(n => n!["name"]!.GetValue<string>())
			.Should().Equal(new[] { "QuickFilterGroup_Filters", "QuickFilter_x_Items" },
				because: "the template native is unioned first, followed by the page's converted entry");
	}

	[Test]
	[Description("On a name collision between a template native and a page entry, the union keeps a single entry and the NATIVE wins (natives are added first), so the template's baseline shape is preserved.")]
	public void Analyze_UnionArrays_NameCollision_NativeWins() {
		// Arrange: both the template native and the page carry an entry named 'Shared' with a different flag.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "Items": { "isCollection": true, "modelConfig": { "path": "PDS",
				"filterAttributes": [ { "name": "Shared", "loadOnChange": false } ] } } } }
			""");
		var natives = new Dictionary<string, JsonArray>(StringComparer.OrdinalIgnoreCase) {
			["Items/modelConfig/filterAttributes"] =
				JsonNode.Parse("""[ { "name": "Shared", "loadOnChange": true } ]""")!.AsArray()
		};

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)),
			mobileTemplateArraysByPath: natives);

		// Assert
		JsonArray filterAttributes = guide.ViewModelConfigDiff!.AsArray().Single(n =>
			n!.AsObject()["path"]!.AsArray().Count == 3)!.AsObject()["values"]!["filterAttributes"]!.AsArray();
		filterAttributes.Should().HaveCount(1, because: "the two 'Shared' entries deduplicate by name into one");
		filterAttributes[0]!["loadOnChange"]!.GetValue<bool>().Should().BeTrue(
			because: "the native is added first, so it wins the name collision");
	}

	[Test]
	[Description("Union dedup is CASE-SENSITIVE (ordinal): two nameless array entries whose serialized JSON differs only by letter case are genuinely distinct data and are BOTH kept — they must not be coalesced by a case-insensitive identity.")]
	public void Analyze_UnionArrays_CaseOnlyDifferingEntries_AreBothKept() {
		// Arrange: two nameless objects under a modelConfig array differing only by the case of a value.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "Items": { "isCollection": true, "modelConfig": { "path": "PDS",
				"filterAttributes": [ { "column": "abc" }, { "column": "ABC" } ] } } } }
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)));

		// Assert
		JsonArray filterAttributes = guide.ViewModelConfigDiff!.AsArray().Single(n =>
			n!.AsObject()["path"]!.AsArray().Count == 3)!.AsObject()["values"]!["filterAttributes"]!.AsArray();
		filterAttributes.Select(n => n!["column"]!.GetValue<string>())
			.Should().Equal(new[] { "abc", "ABC" },
				because: "case-only-differing entries are distinct under an ordinal dedup and both survive");
	}

	[Test]
	[Description("A malformed non-string 'name' on a union entry (e.g. { \"name\": 123 }) does not throw out of the whole conversion — the entry degrades to the deep-JSON identity path and is still carried in the hoisted array.")]
	public void Analyze_UnionArrays_NonStringName_DegradesGracefully_DoesNotThrow() {
		// Arrange: a filterAttributes entry whose 'name' is a number instead of a string.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "Items": { "isCollection": true, "modelConfig": { "path": "PDS",
				"filterAttributes": [ { "name": 123, "loadOnChange": true } ] } } } }
			""");

		// Act
		Action act = () => Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)));

		// Assert
		act.Should().NotThrow(because: "a non-string 'name' must degrade to the deep-JSON identity path, not fail the guide");
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)));
		JsonArray filterAttributes = guide.ViewModelConfigDiff!.AsArray().Single(n =>
			n!.AsObject()["path"]!.AsArray().Count == 3)!.AsObject()["values"]!["filterAttributes"]!.AsArray();
		filterAttributes.Should().HaveCount(1,
			because: "the malformed entry is still carried, deduplicated by its deep-JSON identity");
	}

	[Test]
	[Description("A collection is split (its arrays hoisted, scalars dropped) when mobileTemplateCollectionKeys marks it as template-owned even though the page body itself does NOT carry isCollection:true — the template's own collection metadata drives the decision.")]
	public void Analyze_TemplateCollectionKeys_DriveSplit_WhenPageLacksIsCollectionMarker() {
		// Arrange: the page's Items attribute is NOT marked isCollection; the template says it is a collection.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "Items": { "modelConfig": { "path": "PDS",
				"filterAttributes": [ { "name": "QuickFilter_x_Items", "loadOnChange": true } ] } } } }
			""");
		var collectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Items" };

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)),
			mobileTemplateCollectionKeys: collectionKeys);

		// Assert
		JsonArray diff = guide.ViewModelConfigDiff!.AsArray();
		diff.Should().Contain(n =>
			n!.AsObject()["path"]!.AsArray().Count == 3
			&& n.AsObject()["path"]!.AsArray()[1]!.GetValue<string>() == "Items",
			because: "the template collection key hoists Items.modelConfig.filterAttributes into a targeted merge");
		diff.Where(n => n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>())
				.SequenceEqual(new[] { "attributes" }))
			.Select(n => n!.AsObject()["values"]!.AsObject())
			.Where(v => v.ContainsKey("Items"))
			.Should().BeEmpty(because: "a template-owned collection is split, never dumped whole into the [\"attributes\"] bucket");
	}

	[Test]
	[Description("When arrays were hoisted but the mobile template bundle could not be read (mobileTemplateArraysUnavailable), an explicit constraint warns that the hoisted arrays carry ONLY the page's own entries and template natives may be missing.")]
	public void Analyze_ArraysHoisted_AndTemplateUnavailable_AddsMissingNativesConstraint() {
		// Arrange: a collection with a filterAttributes array (hoisted) but no template bundle available.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "Items": { "isCollection": true, "modelConfig": { "path": "PDS",
				"filterAttributes": [ { "name": "QuickFilter_x_Items", "loadOnChange": true } ] } } } }
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)),
			mobileTemplateArraysUnavailable: true);

		// Assert
		guide.Constraints.Any(c => c.Contains("Could not read the mobile template's bundle"))
			.Should().BeTrue(because: "hoisting arrays without the template natives is surfaced as an explicit risk");
	}

	[Test]
	[Description("The 'template natives unavailable' constraint is NOT added when no modelConfig array was hoisted, even if the template bundle was unavailable — there is no array at risk.")]
	public void Analyze_NoArraysHoisted_EvenWhenTemplateUnavailable_NoMissingNativesConstraint() {
		// Arrange: a page-owned attribute with no modelConfig array to hoist.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "JobTitle", "type": "crt.Input", "value": "$QualifiedContactJobTitle" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "QualifiedContactJobTitle": { "modelConfig": { "path": "PDS.JobTitle" } } } }
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true)),
			mobileTemplateArraysUnavailable: true);

		// Assert
		guide.Constraints.Any(c => c.Contains("Could not read the mobile template's bundle"))
			.Should().BeFalse(because: "no array was hoisted, so there is nothing at risk to warn about");
	}

	[Test]
	[Description("An array nested DEEPER than modelConfig (e.g. modelConfig.sortingConfig.default) is hoisted recursively into a targeted merge at its own parent path — the hoist is type-driven over any array, not keyed to filterAttributes.")]
	public void Analyze_NestedModelConfigArray_HoistedToParentPath() {
		// Arrange: the collection carries a sortingConfig.default array two levels under modelConfig.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "Items": { "isCollection": true, "modelConfig": { "path": "PDS",
				"sortingConfig": { "default": [ { "columnName": "CreatedOn", "direction": "desc" } ] } } } } }
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)));

		// Assert
		JsonObject targeted = guide.ViewModelConfigDiff!.AsArray().Single(n =>
			n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>())
				.SequenceEqual(new[] { "attributes", "Items", "modelConfig", "sortingConfig" }))!.AsObject();
		targeted["values"]!["default"]!.AsArray()[0]!["columnName"]!.GetValue<string>()
			.Should().Be("CreatedOn", because: "the nested array is hoisted at its own parent path (modelConfig/sortingConfig)");
	}

	[Test]
	[Description("A top-level viewModelConfig key other than 'attributes' cannot be expressed as an [\"attributes\"] merge, so it is preserved in a minimal residual root merge (path []) while the attributes are still split out.")]
	public void Analyze_NonAttributesTopLevelKey_KeptInResidualRootMerge() {
		// Arrange: viewModelConfig carries a page-owned attribute AND an unrelated top-level section.
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "JobTitle", "type": "crt.Input", "value": "$QualifiedContactJobTitle" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "QualifiedContactJobTitle": { "modelConfig": { "path": "PDS.JobTitle" } } },
			  "converters": { "usr.Custom": {} } }
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true)));

		// Assert
		JsonArray diff = guide.ViewModelConfigDiff!.AsArray();
		JsonObject residual = diff.Single(n => n!.AsObject()["path"]!.AsArray().Count == 0)!.AsObject();
		residual["values"]!.AsObject().Should().ContainKey("converters",
			because: "a non-attributes top-level key is kept in the residual root merge");
		residual["values"]!.AsObject().Should().NotContainKey("attributes",
			because: "attributes are split out into their own [\"attributes\"] merge");
		diff.Should().Contain(n =>
			n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>()).SequenceEqual(new[] { "attributes" }),
			because: "the page-owned attribute still lands in a focused [\"attributes\"] merge");
	}

	[Test]
	[Description("CollectNativeArraysByPath returns every array anywhere in the template's merged viewModelConfig, keyed by its /-joined path — including arrays nested deeper than modelConfig.")]
	public void CollectNativeArraysByPath_ReturnsEveryArrayKeyedByPath() {
		// Arrange
		JsonObject templateVmc = JsonNode.Parse("""
			{ "attributes": { "Items": { "modelConfig": {
				"filterAttributes": [ { "name": "QuickFilterGroup_Filters" } ],
				"sortingConfig": { "default": [ { "columnName": "CreatedOn" } ] } } } } }
			""")!.AsObject();

		// Act
		IReadOnlyDictionary<string, JsonArray> result =
			WebToMobileAnalysisService.CollectNativeArraysByPath(templateVmc);

		// Assert
		result.Should().ContainKey("Items/modelConfig/filterAttributes",
			because: "a top-level modelConfig array is collected by its path");
		result.Should().ContainKey("Items/modelConfig/sortingConfig/default",
			because: "a deeply nested array is collected by its full path");
		result["Items/modelConfig/filterAttributes"][0]!["name"]!.GetValue<string>()
			.Should().Be("QuickFilterGroup_Filters", because: "the array's own entries are preserved");
	}

	[Test]
	[Description("CollectNativeArraysByPath returns an empty map for a null or attribute-less template viewModelConfig instead of throwing.")]
	public void CollectNativeArraysByPath_ReturnsEmpty_ForNullOrAttributeLessConfig() {
		// Act
		IReadOnlyDictionary<string, JsonArray> fromNull =
			WebToMobileAnalysisService.CollectNativeArraysByPath(null);
		IReadOnlyDictionary<string, JsonArray> fromEmpty =
			WebToMobileAnalysisService.CollectNativeArraysByPath(JsonNode.Parse("""{ }""")!.AsObject());

		// Assert
		fromNull.Should().BeEmpty(because: "a null config yields no native arrays");
		fromEmpty.Should().BeEmpty(because: "an attribute-less config yields no native arrays");
	}

	[Test]
	[Description("CollectTemplateCollectionKeys returns only the attribute keys the template marks isCollection:true (case-insensitive), ignoring non-collection attributes.")]
	public void CollectTemplateCollectionKeys_ReturnsOnlyIsCollectionAttributes() {
		// Arrange
		JsonObject templateVmc = JsonNode.Parse("""
			{ "attributes": {
				"Items": { "isCollection": true, "modelConfig": { "path": "PDS" } },
				"Title": { "modelConfig": { "path": "PDS.Title" } },
				"Details": { "isCollection": false } } }
			""")!.AsObject();

		// Act
		IReadOnlySet<string> result = WebToMobileAnalysisService.CollectTemplateCollectionKeys(templateVmc);

		// Assert
		result.Should().Contain("Items", because: "Items is marked isCollection:true");
		result.Should().NotContain("Title", because: "Title carries no isCollection flag");
		result.Should().NotContain("Details", because: "Details is explicitly isCollection:false");
	}

	[Test]
	[Description("An array in the page's modelConfig (e.g. a data source's config.sortColumns) is hoisted out of the root merge into a TARGETED merge at its own parent path, UNIONED with the mobile template's native array at that same path (natives first, page entries after) — so the mobile diff engine's array-replace on a root merge cannot drop either side. No path-[] operation remains.")]
	public void Analyze_ModelConfigArray_HoistedToTargetedMerge_UnionedWithTemplateNatives() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			modelConfigJson: """
			{ "dataSources": { "PDS": { "config": {
				"sortColumns": [ { "columnName": "CreatedOn", "direction": "desc" } ] } } } }
			""");

		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)),
			mobileTemplateModelArraysByPath: new Dictionary<string, JsonArray>(StringComparer.OrdinalIgnoreCase) {
				["dataSources/PDS/config/sortColumns"] =
					JsonNode.Parse("""[ { "columnName": "UsrNativeSort", "direction": "asc" } ]""")!.AsArray()
			});

		JsonArray mcd = guide.ModelConfigDiff!.AsArray();
		mcd.Should().NotContain(n => n!.AsObject()["path"]!.AsArray().Count == 0,
			because: "the whole-config root merge is split into targeted merges");
		JsonObject targeted = mcd.Single(n =>
			n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>())
				.SequenceEqual(new[] { "dataSources", "PDS", "config" }))!.AsObject();
		targeted["values"]!["sortColumns"]!.AsArray().Select(n => n!["columnName"]!.GetValue<string>())
			.Should().Equal(new[] { "UsrNativeSort", "CreatedOn" },
				because: "the hoisted array unions the template native (first) with the page's own entry (after)");
	}

	[Test]
	[Description("When a modelConfig array is hoisted but the mobile template bundle could not be read (mobileTemplateArraysUnavailable), the hoisted array carries ONLY the page's own entries and the same 'template natives unavailable' constraint is raised as for viewModelConfig arrays.")]
	public void Analyze_ModelConfigArrayHoisted_AndTemplateUnavailable_AddsMissingNativesConstraint() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			modelConfigJson: """
			{ "dataSources": { "PDS": { "config": {
				"sortColumns": [ { "columnName": "CreatedOn", "direction": "desc" } ] } } } }
			""");

		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)),
			mobileTemplateArraysUnavailable: true);

		JsonObject targeted = guide.ModelConfigDiff!.AsArray().Single(n =>
			n!.AsObject()["path"]!.AsArray().Select(s => s!.GetValue<string>())
				.SequenceEqual(new[] { "dataSources", "PDS", "config" }))!.AsObject();
		targeted["values"]!["sortColumns"]!.AsArray().Should().HaveCount(1,
			because: "with no template natives the union degrades to just the page's own entry");
		guide.Constraints.Any(c => c.Contains("Could not read the mobile template's bundle"))
			.Should().BeTrue(because: "a hoisted modelConfig array without template natives is surfaced as an explicit risk");
		guide.Constraints.Any(c => c.Contains("viewModelConfig or modelConfig"))
			.Should().BeTrue(because: "the constraint text must name modelConfig too, since a modelConfig array (not only viewModelConfig) can trigger it");
	}

	[Test]
	[Description("CollectNativeArraysByPathFromRoot walks the WHOLE config from its root (not only 'attributes'), so it collects arrays anywhere in a template's merged modelConfig — e.g. under dataSources/<ds>/config — keyed by their full /-joined path.")]
	public void CollectNativeArraysByPathFromRoot_ReturnsArraysAnywhereFromRoot() {
		JsonObject templateModelConfig = JsonNode.Parse("""
			{ "dataSources": { "PDS": { "config": {
				"sortColumns": [ { "columnName": "CreatedOn" } ],
				"filter": { "items": [ { "columnPath": "Name" } ] } } } } }
			""")!.AsObject();

		IReadOnlyDictionary<string, JsonArray> result =
			WebToMobileAnalysisService.CollectNativeArraysByPathFromRoot(templateModelConfig);

		result.Should().ContainKey("dataSources/PDS/config/sortColumns",
			because: "an array under a data source's config is collected by its full path from the root");
		result.Should().ContainKey("dataSources/PDS/config/filter/items",
			because: "a deeply nested array is collected by its full path");
		WebToMobileAnalysisService.CollectNativeArraysByPathFromRoot(null)
			.Should().BeEmpty(because: "a null config yields no native arrays");
	}

	[Test]
	[Description("A top-level modelConfig key that is NOT an object (a scalar) cannot be expressed as a nested-key merge, so it stays in a residual path-[] root merge. That residual is EXPECTED-SAFE, not a regression: it carries only scalars — never an array — so the mobile diff engine's array-replace cannot drop the page's own entries, and the split invariant treats it as legitimate.")]
	public void Analyze_ModelConfigTopLevelScalar_KeptInArrayFreeResidualRootMerge() {
		// Arrange
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "List", "type": "crt.List", "items": "$Items" } ] } ]
			""",
			modelConfigJson: """
			{ "dataSources": { "PDS": { "config": { "attributes": {} } } },
			  "primaryDataSourceName": "PDS" }
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: Reg(("crt.FlexContainer", true), ("crt.List", false)));

		// Assert
		JsonArray mcd = guide.ModelConfigDiff!.AsArray();
		JsonObject residual = mcd.Single(n => n!.AsObject()["path"]!.AsArray().Count == 0)!.AsObject();
		residual["values"]!["primaryDataSourceName"]!.GetValue<string>().Should().Be("PDS",
			because: "a top-level scalar that cannot be a nested-key merge is preserved verbatim in the residual root merge");
		residual["values"]!.AsObject().Any(kv => kv.Value is JsonArray).Should().BeFalse(
			because: "a scalar-only residual root merge carries no array, so the mobile diff engine's array-replace cannot drop a page array — the shape is expected-safe, not a regression");
	}

	[Test]
	[Description("insert mobileValues carries the type, the field label, the control binding, and every source property verbatim — including one the mobile registry does not declare (registry is incomplete, ENG-91859); only the one-way value setter is left out.")]
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
		// The web registry declares usrWebOnly and the mobile one does not — under the old rule this made it
		// "web-only" and dropped; now it is carried verbatim (no registry-membership pruning).
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
		// Every source property is carried verbatim …
		leadVals.ContainsKey("readonly").Should().BeTrue(because: "readonly is carried");
		leadVals.ContainsKey("placeholder").Should().BeTrue(because: "placeholder is carried");
		// … including one the mobile registry does not declare (no registry-membership pruning while the
		// registry is incomplete — ENG-91859).
		leadVals.ContainsKey("usrWebOnly").Should().BeTrue(because: "registry-absent props are no longer dropped");
		// The control binding is prebuilt verbatim: fields bind via `control` on mobile exactly as on web
		// (ComboBox included) — see stock Contact_MobileFormPage and the mobile crt.ComboBox contract.
		leadVals["control"]!.GetValue<string>().Should().Be("$LeadName",
			because: "the control binding is the mobile field's data-source binding and is carried verbatim");

		// No caption but bound to PDS.JobTitle → auto-provided column-code label.
		JsonObject jobVals = Element(guide, "JobTitle").MobileValues!.AsObject();
		jobVals["label"]!.GetValue<string>().Should().Be("$Resources.Strings.JobTitle");
		// A web `value` property is a one-way setter, not the field binding — it is still excluded.
		jobVals.ContainsKey("value").Should().BeFalse(because: "the one-way value setter is never carried as a field binding");
	}

	[Test]
	[Description("When the mobile registry declares NO inputs for the type (empty/untrustworthy contract — ENG-91859), pruning is skipped: every source property (e.g. entityName) is carried verbatim even when the web registry declares it.")]
	public void Analyze_Insert_EmptyMobileContract_CarriesAllSourceProps() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "ProgressBar", "type": "crt.EntityStageProgressBar",
				  "entityName": "Lead", "shape": "rounded", "control": "$Stage" } ] } ]
			""");
		// Mobile registry entry EXISTS but declares no inputs (the registry-generation gap).
		var mobileByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.EntityStageProgressBar"] = new ComponentRegistryEntry { ComponentType = "crt.EntityStageProgressBar" }
		};
		// The web registry DOES declare entityName — under the old rule this made it web-only and dropped.
		var webByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.FlexContainer"] = new ComponentRegistryEntry { ComponentType = "crt.FlexContainer", Container = true },
			["crt.EntityStageProgressBar"] = new ComponentRegistryEntry {
				ComponentType = "crt.EntityStageProgressBar",
				Inputs = new Dictionary<string, JsonElement> {
					["entityName"] = JsonSerializer.SerializeToElement(new { }),
					["shape"] = JsonSerializer.SerializeToElement(new { })
				}
			}
		};
		// crt.EntityStageProgressBar is supported on mobile (so the leaf inserts) but its registry entry
		// carries no inputs — the exact ENG-91859 shape.
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crt.EntityStageProgressBar" };

		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, mobileTypes, WebTypes, webByType, mobileByType,
			Rules, templateRule: null,
			sourcePage: "UsrApp_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrApp_MobileFormPage", containerNameMap: null);

		JsonObject vals = Element(guide, "ProgressBar").MobileValues!.AsObject();
		vals["type"]!.GetValue<string>().Should().Be("crt.EntityStageProgressBar");
		vals["entityName"]!.GetValue<string>().Should().Be("Lead", because: "an empty mobile contract must not drop any property");
		vals["shape"]!.GetValue<string>().Should().Be("rounded");
		// The control binding is carried verbatim regardless of the contract completeness.
		vals["control"]!.GetValue<string>().Should().Be("$Stage",
			because: "the control binding is the mobile field's data-source binding and is carried verbatim");
	}

	[Test]
	[Description("crt.Feed carries dataSourceName + entitySchemaName even though the mobile registry declares only primaryColumnValue (partial contract) — dataSourceName is no longer excluded and a registry-absent required prop (entitySchemaName) is not dropped.")]
	public void Analyze_Insert_PartialMobileContract_CarriesRequiredDataSourceProps() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Feed", "type": "crt.Feed",
				  "dataSourceName": "PDS", "entitySchemaName": "Opportunity", "primaryColumnValue": "$Id" } ] } ]
			""");
		// Mobile registry declares ONLY primaryColumnValue — the incomplete-registry shape reported for crt.Feed.
		var mobileByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.Feed"] = new ComponentRegistryEntry {
				ComponentType = "crt.Feed",
				Inputs = new Dictionary<string, JsonElement> { ["primaryColumnValue"] = JsonSerializer.SerializeToElement(new { }) }
			}
		};
		var webByType = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase) {
			["crt.FlexContainer"] = new ComponentRegistryEntry { ComponentType = "crt.FlexContainer", Container = true },
			["crt.Feed"] = new ComponentRegistryEntry {
				ComponentType = "crt.Feed",
				Inputs = new Dictionary<string, JsonElement> {
					["dataSourceName"] = JsonSerializer.SerializeToElement(new { }),
					["entitySchemaName"] = JsonSerializer.SerializeToElement(new { }),
					["primaryColumnValue"] = JsonSerializer.SerializeToElement(new { })
				}
			}
		};
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crt.Feed" };

		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, mobileTypes, WebTypes, webByType, mobileByType,
			Rules, templateRule: null,
			sourcePage: "UsrApp_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrApp_MobileFormPage", containerNameMap: null);

		JsonObject vals = Element(guide, "Feed").MobileValues!.AsObject();
		vals["type"]!.GetValue<string>().Should().Be("crt.Feed");
		vals["dataSourceName"]!.GetValue<string>().Should().Be("PDS", because: "dataSourceName is required by crt.Feed and is no longer excluded");
		vals["entitySchemaName"]!.GetValue<string>().Should().Be("Opportunity", because: "a registry-absent required prop must not be dropped");
		vals["primaryColumnValue"]!.GetValue<string>().Should().Be("$Id");
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
	[Description("A rule whose condition mixes AND and OR across nested groups is dropped (the flat single-operator condition input cannot represent it), even when its actions would otherwise survive.")]
	public void ConvertPageBusinessRules_MixedAndOrCondition_DropsRule() {
		var rule = new SourcePageBusinessRule {
			Caption = "Mixed A AND (B OR C)",
			ConditionIssue = PageRuleConditionIssue.MixedAndOr,
			Actions = { ElementAction("make-read-only", "UsrName") }
		};
		PageBusinessRuleProbeResult probe = ProbeOf(rule);
		var elementMap = new List<ElementMapEntry> { El("UsrName", "merge", "AreaName") };

		PageBusinessRuleConversionInfo result = WebToMobileAnalysisService.ConvertPageBusinessRules(probe, elementMap);

		result.ConvertedRules.Should().BeEmpty();
		result.DroppedRules.Should().HaveCount(1);
		result.DroppedRules[0].Caption.Should().Be("Mixed A AND (B OR C)");
		result.DroppedRules[0].Reason.Should().Contain("mixes AND and OR");
	}

	[Test]
	[Description("A rule whose condition uses an unrecognized comparison operator is dropped rather than emitted with a silently rewritten comparison, even when its actions would otherwise survive.")]
	public void ConvertPageBusinessRules_UnrecognizedComparison_DropsRule() {
		var rule = new SourcePageBusinessRule {
			Caption = "Name begins with A",
			ConditionIssue = PageRuleConditionIssue.UnrecognizedComparison,
			Actions = { ElementAction("hide-element", "UsrName") }
		};
		PageBusinessRuleProbeResult probe = ProbeOf(rule);
		var elementMap = new List<ElementMapEntry> { El("UsrName", "merge", "AreaName") };

		PageBusinessRuleConversionInfo result = WebToMobileAnalysisService.ConvertPageBusinessRules(probe, elementMap);

		result.ConvertedRules.Should().BeEmpty();
		result.DroppedRules.Should().HaveCount(1);
		result.DroppedRules[0].Caption.Should().Be("Name begins with A");
		result.DroppedRules[0].Reason.Should().Contain("comparison operator");
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

	// ── Effective web-template resolution (replacing schema, parentSchemaName == schemaName) ──

	private static PageMetadataInfo Page(string schemaName, string parentSchemaName) =>
		new() { SchemaName = schemaName, ParentSchemaName = parentSchemaName };

	private static PageBundleInfo Chain(params string[] schemaNames) =>
		new() {
			Schemas = schemaNames.Select(n => new PageSchemaChainEntry { SchemaName = n }).ToList()
		};

	[Test]
	[Description("Non-replacing page: the direct parent already differs from the page's own name, so it is used verbatim — no chain climb, no behavior change.")]
	public void ResolveEffectiveTemplateName_NonReplacing_ReturnsDirectParent() {
		string result = MobilePageConversionGuideTool.ResolveEffectiveTemplateName(
			Page("Leads_FormPage", "PageWithTabsFreedomTemplate"),
			Chain("Leads_FormPage", "PageWithTabsFreedomTemplate", "BasePageFreedomTemplate"),
			Rules);
		result.Should().Be("PageWithTabsFreedomTemplate");
	}

	[Test]
	[Description("Replacing form page (parentSchemaName == schemaName): climb past the same-named base to the first rule-matching template ancestor.")]
	public void ResolveEffectiveTemplateName_ReplacingForm_ClimbsToTemplate() {
		string result = MobilePageConversionGuideTool.ResolveEffectiveTemplateName(
			Page("Cases_FormPage", "Cases_FormPage"),
			Chain("Cases_FormPage", "PageWithTabsFreedomTemplate", "BasePageFreedomTemplate"),
			Rules);
		result.Should().Be("PageWithTabsFreedomTemplate");
	}

	[Test]
	[Description("Multi-level replacing chain (page → same-named base → another same-named base → template): every same-named layer is skipped.")]
	public void ResolveEffectiveTemplateName_MultiLevelReplacing_ClimbsPastAllSameNamed() {
		string result = MobilePageConversionGuideTool.ResolveEffectiveTemplateName(
			Page("Cases_FormPage", "Cases_FormPage"),
			Chain("Cases_FormPage", "Cases_FormPage", "PageWithTabsFreedomTemplate"),
			Rules);
		result.Should().Be("PageWithTabsFreedomTemplate");
	}

	[Test]
	[Description("Replacing LIST page uses the same mechanism and resolves the list template rule.")]
	public void ResolveEffectiveTemplateName_ReplacingList_ClimbsToListTemplate() {
		var rules = new WebToMobilePageConversionRules {
			Templates = [new TemplateMappingRule { Web = "ListPageV3Template", Mobile = "BaseMobileListTemplate" }]
		};
		string result = MobilePageConversionGuideTool.ResolveEffectiveTemplateName(
			Page("UsrDemo_ListPage", "UsrDemo_ListPage"),
			Chain("UsrDemo_ListPage", "ListPageV3Template", "BaseTemplate"),
			rules);
		result.Should().Be("ListPageV3Template");
	}

	[Test]
	[Description("Replacing page with no rule-matching ancestor falls back to the first differently-named ancestor — never the page itself.")]
	public void ResolveEffectiveTemplateName_NoRuleMatch_ReturnsFirstDistinctAncestor() {
		string result = MobilePageConversionGuideTool.ResolveEffectiveTemplateName(
			Page("Foo_FormPage", "Foo_FormPage"),
			Chain("Foo_FormPage", "Bar_BaseFormPage", "BazTemplate"),
			Rules);
		result.Should().Be("Bar_BaseFormPage");
	}

	[Test]
	[Description("Empty-layout diagnostic: when the source has components but the baseline subtracts the whole tree (the self-parent bug), LayoutResolution reports 'empty' instead of returning a silently-empty layout.")]
	public void Analyze_LayoutSubtractedToEmpty_SetsLayoutResolutionDiagnostic() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.Tabs", "items": [
				{ "name": "GeneralTab", "type": "crt.TabContainer", "items": [
					{ "name": "UsrName", "type": "crt.Input" } ] } ] } ]
			""");
		var web = Reg(("crt.Tabs", true), ("crt.TabContainer", true), ("crt.Input", false));
		// Pathological baseline: EVERY name is treated as template chrome (what the self-parent bug produced).
		IReadOnlySet<string> everything = Names("Tabs", "GeneralTab", "UsrName");

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web, templateComponentNames: everything);

		guide.SourceStructure.Should().BeEmpty();
		guide.LayoutResolution.Should().StartWith("empty:");
	}

	[Test]
	[Description("A normal, non-empty conversion leaves LayoutResolution null and classifies a registry-known widget (crt.IndicatorWidget) as convertible, not dropped.")]
	public void Analyze_NonEmptyLayout_LeavesLayoutResolutionNullAndClassifiesWidget() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "UsrMetric", "type": "crt.IndicatorWidget" } ]
			""");
		var web = Reg(("crt.IndicatorWidget", false));

		MobilePageConversionGuide guide = Analyze(bundle, webByType: web);

		guide.SourceStructure.Should().NotBeEmpty();
		guide.LayoutResolution.Should().BeNull();
		ForType(guide, "crt.IndicatorWidget").Category.Should().Be("DirectMapping");
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

	[Test]
	[Description("A component twin whose rule declares carryProperties (FolderTree→FolderTreeActions) is kept through baseline subtraction AND gets a deterministic merge payload: the whitelisted web props (sourceSchemaName/rootSchemaName) are carried verbatim onto the mobile element, so the app-authored rootSchemaName is not lost to template-chrome pruning.")]
	public void Analyze_TemplateComponentTwin_CarryProperties_CarriesWebSchemaBindingOntoMobileElement() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "ContentContainer", "type": "crt.FlexContainer", "items": [
				{ "name": "FolderTree", "type": "crt.FolderTree", "sourceSchemaName": "FolderTree", "rootSchemaName": "UsrMouse" } ] } ]
			""");
		var web = Reg(("crt.FlexContainer", true), ("crt.FolderTree", false));
		var containerNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ContentContainer"] = "HeaderContainer" };
		var componentNameMap = new Dictionary<string, ComponentMappingRule>(StringComparer.OrdinalIgnoreCase) {
			["FolderTree"] = new ComponentMappingRule {
				Web = "FolderTree", Mobile = "FolderTreeActions", MobileType = "crt.FolderTreeActions",
				CarryProperties = ["sourceSchemaName", "rootSchemaName"], Note = "Folder tree."
			}
		};
		// FolderTree is inherited from the web list template (it is in the baseline). Without the components map
		// it would be pruned as chrome and its app-authored rootSchemaName lost; the map keeps it as a carry twin.
		IReadOnlySet<string> templateNames = Names("ContentContainer", "FolderTree");

		MobilePageConversionGuide guide = Analyze(
			bundle, webByType: web, containerNameMap: containerNameMap,
			templateComponentNames: templateNames, componentNameMap: componentNameMap);

		// Kept (not pruned): recorded as a merge-by-name twin onto the mobile FolderTreeActions element.
		ElementMapEntry twin = guide.ElementMap.Single(e => e.WebName == "FolderTree");
		twin.Operation.Should().Be("merge");
		twin.MobileName.Should().Be("FolderTreeActions");
		twin.MobileType.Should().Be("crt.FolderTreeActions");
		// Deterministic payload: the whitelisted web props are carried verbatim.
		JsonObject vals = twin.MobileValues!.AsObject();
		vals["rootSchemaName"]!.GetValue<string>().Should().Be("UsrMouse");
		vals["sourceSchemaName"]!.GetValue<string>().Should().Be("FolderTree");
		// The reason tells the caller to merge the prebuilt values (not hand-configure).
		twin.Reason.Should().Contain("rootSchemaName");
		// No duplicate insert for the folder element.
		guide.ElementMap.Should().NotContain(e => e.WebName == "FolderTree" && e.Operation == "insert");
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
	[Description("A BUTTON with an unknown/custom request (not in the supported set and not remapped) is DROPPED — a dead button has no purpose on mobile.")]
	public void Analyze_UnknownRequest_ButtonDropped() {
		MobilePageConversionGuide guide = AnalyzeRequests(ButtonBundle("CustomButton", "usr.MyCustomRequest"));

		Element(guide, "CustomButton").Operation.Should().Be("drop");
	}

	[Test]
	[Description("A NON-button component whose event-binding request is not in the supported set is NOT dropped — only buttons are dropped for an unsupported request. Some components legitimately use a system request absent from the list; it is kept verbatim and flagged.")]
	public void Analyze_NonButtonUnsupportedRequest_ComponentKept() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Progress", "type": "crt.EntityStageProgressBar", "caption": "P",
				  "updated": { "request": "usr.SomeSystemRequest", "params": {} } } ] } ]
			""");
		var mobileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.FlexContainer", "crt.EntityStageProgressBar"
		};

		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, mobileTypes, WebTypes,
			webByType: Reg(("crt.FlexContainer", true)),
			mobileByType: null,
			RequestRules, templateRule: null,
			sourcePage: "UsrApp_FormPage", sourceTemplate: null,
			suggestedTarget: "UsrApp_MobileFormPage", containerNameMap: null);

		Element(guide, "Progress").Operation.Should().NotBe("drop",
			because: "a non-button component is not dropped for an unsupported (likely system) request");
		guide.RequestConversions!.FlaggedRequests.Should().ContainSingle(r =>
			r.ElementName == "Progress" && r.Request == "usr.SomeSystemRequest");
	}

	[Test]
	[Description("The versioned rules file is authoritative: a request it maps 1:1 (crt.QuickFilterRequest) that the bundled offline constant does NOT list is KEPT, not dropped — so a CDN rules update can enable a request without a clio release.")]
	public void Analyze_VersionedRuleEnablesRequestBeyondConstant_Kept() {
		MobilePageConversionGuide guide = AnalyzeRequests(ButtonBundle("FilterButton", "crt.QuickFilterRequest"));

		Element(guide, "FilterButton").Operation.Should().NotBe("drop",
			because: "the versioned rules file maps crt.QuickFilterRequest, so it is supported even though the offline constant omits it");
		JsonObject clicked = ClickedOf(guide, "FilterButton")["clicked"]!.AsObject();
		clicked["request"]!.GetValue<string>().Should().Be("crt.QuickFilterRequest");
		guide.RequestConversions!.ConvertedRequests.Should().ContainSingle(r =>
			r.ElementName == "FilterButton" && r.WebRequest == "crt.QuickFilterRequest"
			&& r.MobileRequest == "crt.QuickFilterRequest");
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
	[Description("A multi-column grid container renamed by the template map (merge twin, e.g. GeneralInfoTabContainer -> GeneralTabContainer) still gets the phone one-column collapse: the column count captured under the WEB name is matched to children that carry the MOBILE parent name.")]
	public void Analyze_MultiColumnGrid_RenamedTwin_ConvertsSmallToOneColumn() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "GeneralInfoTabContainer", "type": "crt.GridContainer",
			    "columns": [ "minmax(32px, 1fr)", "minmax(32px, 1fr)" ], "items": [
				{ "name": "Name", "type": "crt.Input", "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 } },
				{ "name": "CreatedOn", "type": "crt.Input", "layoutConfig": { "column": 2, "row": 1, "colSpan": 1, "rowSpan": 1 } } ] } ]
			""");
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["GeneralInfoTabContainer"] = "GeneralTabContainer"
		};

		MobilePageConversionGuide guide = Analyze(bundle,
			webByType: Reg(("crt.GridContainer", true), ("crt.Input", false)),
			containerNameMap: map);

		AdaptiveLayoutGroup group = guide.AdaptiveLayout!.Single();
		group.ContainerName.Should().Be("GeneralTabContainer",
			because: "the adaptive group is keyed by the mobile (renamed) container name");

		// The renamed lookup matched: the 2nd child stacks on phone (col 1, row 2) and keeps the web 2-column
		// cell (col 2, row 1) on tablet/desktop. Before the fix its layoutConfig kept the web 2-column form.
		JsonObject co = AdaptiveOf(guide, "CreatedOn");
		co["small"]!["column"]!.GetValue<int>().Should().Be(1);
		co["small"]!["row"]!.GetValue<int>().Should().Be(2);
		co["medium"]!["column"]!.GetValue<int>().Should().Be(2);
		co["medium"]!["row"]!.GetValue<int>().Should().Be(1);
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
	[Description("Every property is carried verbatim: a system/framework prop (layoutConfig), a mobile-supported prop (readonly), AND a prop the web registry declares but the mobile registry lacks — the last is no longer dropped (no registry-membership pruning while the mobile registry is incomplete, ENG-91859).")]
	public void Analyze_AllProps_CarriedIncludingWebSpecific() {
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
		values.Should().ContainKey("webOnlyProp", "registry-absent props are no longer dropped while the mobile registry is incomplete");
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

	#region Tab body / Area layers synthesized into a converted tab (ENG-94188)

	/// <summary>A converter-SYNTHESIZED entry (no webName), addressed by the mobile name it creates.</summary>
	private static ElementMapEntry Synthesized(MobilePageConversionGuide guide, string mobileName) =>
		guide.ElementMap.Single(e => e.WebName is null && e.MobileName == mobileName);

	/// <summary>Position of an entry in the element map (a synthesized layer must precede what it holds).</summary>
	private static int IndexOfMobile(MobilePageConversionGuide guide, string mobileName) {
		var map = (IList<ElementMapEntry>)guide.ElementMap;
		for (int i = 0; i < map.Count; i++) {
			if (map[i].MobileName == mobileName) {
				return i;
			}
		}
		return -1;
	}

	private static WebToMobilePageConversionRules RulesWithTabAreaLayers(
		string tabComponentType = "crt.TabContainer", string[] detailComponentTypes = null) => new() {
		Components = GridRule.Components,
		TabAreaLayers = new TabAreaLayersRule {
			TabComponentType = tabComponentType,
			DetailComponentTypes = detailComponentTypes ?? [],
			MainTabContainer = new SynthesizedContainerRule {
				NamePrefix = "MainTabContainer_",
				Values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
					"""{ "type": "crt.GridContainer", "alignItems": "stretch", "padding": { "bottom": "medium" } }""")
			},
			AreaContainer = new SynthesizedContainerRule {
				NamePrefix = "GridContainer_",
				Values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
					"""{ "type": "crt.GridContainer", "color": "primary", "borderRadius": "medium" }""")
			}
		}
	};

	/// <summary>The synthesized layer names for a tab of the tabbed fixture (source page comes from AnalyzeTabbed).</summary>
	private static (string Main, string Area) LayerNames(string tabName) {
		string suffix = WebToMobileAnalysisService.StableSuffix("Leads_FormPage", tabName);
		return ("MainTabContainer_" + suffix, "GridContainer_" + suffix);
	}

	/// <summary>The synthesized detail Area name for one panel of the tabbed fixture (per-panel suffix, ENG-94188 AC#4).</summary>
	private static string DetailAreaName(string tabName, string panelName) =>
		"GridContainer_" + WebToMobileAnalysisService.StableSuffix("Leads_FormPage", $"{tabName}:{panelName}");

	/// <summary>Rules with the tab-area layers AND the expansion panel registered as a detail type (AC#4 switched on).</summary>
	private static WebToMobilePageConversionRules RulesWithPanelDetails() =>
		RulesWithTabAreaLayers(detailComponentTypes: ["crt.ExpansionPanel"]);

	[Test]
	[Description("ENG-94188 I2: a converted tab with content gets the designer's two layers (tab-body grid + Area card) inserted RIGHT AFTER its own entry, carrying the rule values verbatim plus an items slot, with no webName.")]
	public void Analyze_ShouldSynthesizeBothTabLayers_WhenConvertedTabHasContent() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(string main, string area) = LayerNames("OverviewTab");
		ElementMapEntry mainEntry = Synthesized(guide, main);
		mainEntry.Operation.Should().Be("insert");
		mainEntry.WebName.Should().BeNull(because: "a synthesized container has no source element behind it");
		mainEntry.WebType.Should().BeNull();
		mainEntry.ParentName.Should().Be("OverviewTab");
		mainEntry.PropertyName.Should().Be("items");
		mainEntry.MobileType.Should().Be("crt.GridContainer");
		mainEntry.MobileValues!["alignItems"]!.GetValue<string>().Should().Be("stretch");
		mainEntry.MobileValues!["padding"]!["bottom"]!.GetValue<string>().Should().Be("medium");
		mainEntry.MobileValues!["items"]!.AsArray().Should().BeEmpty(because: "children need an initialized slot to land in");
		mainEntry.Reason.Should().Contain("synthesized by the converter");

		ElementMapEntry areaEntry = Synthesized(guide, area);
		areaEntry.ParentName.Should().Be(main, because: "the Area card sits inside the tab body, not in the tab");
		areaEntry.MobileValues!["color"]!.GetValue<string>().Should().Be("primary");
		areaEntry.MobileValues!["borderRadius"]!.GetValue<string>().Should().Be("medium");
		areaEntry.MobileValues!["items"]!.AsArray().Should().BeEmpty();

		// Order: parent before child, both immediately after the tab.
		int tabAt = IndexOfMobile(guide, "OverviewTab");
		IndexOfMobile(guide, main).Should().Be(tabAt + 1);
		IndexOfMobile(guide, area).Should().Be(tabAt + 2);

		// The tab's content lives in the Area, not in the tab itself.
		Element(guide, "LeadName").ParentName.Should().Be(area);

		TabAreaLayerGroup group = guide.TabAreaLayers!.Single();
		group.TabName.Should().Be("OverviewTab");
		group.MainTabContainerName.Should().Be(main);
		group.AreaName.Should().Be(area);
		group.MovedChildren.Should().Equal(new[] { "LeadName" });
	}

	[Test]
	[Description("ENG-94188 I3: every top-level component of a converted tab is retargeted into the Area and stacked in SOURCE order — column 1, rows 1..N of the single-column card.")]
	public void Analyze_ShouldStackTabContentInSourceOrder_WhenTabContentMovesIntoArea() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" },
					{ "name": "Status", "type": "crt.ComboBox" },
					{ "name": "DecisionDate", "type": "crt.DateTimeEdit" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(_, string area) = LayerNames("OverviewTab");
		foreach (string name in new[] { "LeadName", "Status", "DecisionDate" }) {
			Element(guide, name).ParentName.Should().Be(area, because: "the tab body holds the Area, not the fields");
		}
		// Rows follow the source order, one per row of a single column.
		Element(guide, "LeadName").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1);
		Element(guide, "Status").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2);
		Element(guide, "DecisionDate").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(3);
		JsonNode first = Element(guide, "LeadName").MobileValues!["layoutConfig"]!;
		first["column"]!.GetValue<int>().Should().Be(1);
		first["colSpan"]!.GetValue<int>().Should().Be(1);
		first["rowSpan"]!.GetValue<int>().Should().Be(1);

		guide.TabAreaLayers!.Single().MovedChildren.Should().Equal("LeadName", "Status", "DecisionDate");
	}

	[Test]
	[Description("ENG-94188 I2: with TWO content-bearing tabs each tab's layers sit exactly at tab+1/tab+2 in the FINAL map — the first tab's two inserts shift the second tab, so the pass must re-resolve every tab's index instead of snapshotting positions before inserting; and each tab's children land in that tab's OWN Area.")]
	public void Analyze_ShouldPlaceLayersRightAfterEachTab_WhenMultipleTabsHaveContent() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] },
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [
					{ "name": "Budget", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(string overviewMain, string overviewArea) = LayerNames("OverviewTab");
		(string salesMain, string salesArea) = LayerNames("SalesTab");

		// Parent-before-child right after EACH tab, in the final (post-all-inserts) map: positions computed
		// against stale pre-insert indices would misplace the second tab's layers while a one-tab test stays green.
		int overviewAt = IndexOfMobile(guide, "OverviewTab");
		IndexOfMobile(guide, overviewMain).Should().Be(overviewAt + 1,
			because: "applying entries in element-map order must create the tab body before the Area it holds");
		IndexOfMobile(guide, overviewArea).Should().Be(overviewAt + 2,
			because: "the Area must exist before the tab's children that point at it");
		int salesAt = IndexOfMobile(guide, "SalesTab");
		salesAt.Should().BeGreaterThan(overviewAt + 2,
			because: "the first tab's two inserts shift every later element — the shift this test exists to exercise");
		IndexOfMobile(guide, salesMain).Should().Be(salesAt + 1,
			because: "the second tab's index must be re-resolved after the first tab's inserts moved it");
		IndexOfMobile(guide, salesArea).Should().Be(salesAt + 2,
			because: "the second tab's Area must still directly follow its own tab body");

		// Each tab's content lands in ITS OWN Area, never in a sibling tab's.
		Element(guide, "LeadName").ParentName.Should().Be(overviewArea,
			because: "cross-tab reparenting would silently move content between tabs");
		Element(guide, "Budget").ParentName.Should().Be(salesArea,
			because: "cross-tab reparenting would silently move content between tabs");

		guide.TabAreaLayers!.Select(g => g.TabName).Should().Equal(new[] { "OverviewTab", "SalesTab" },
			because: "groups follow the element-map order of the tabs");
	}

	[Test]
	[Description("ENG-94188 I3: a web layoutConfig carried over from the multi-column web page is REPLACED by the single-column stack placement — the Area is one column, so the old columns would misplace the field.")]
	public void Analyze_ShouldReplaceCarriedWebLayoutConfig_WhenTabChildMovesIntoSingleColumnArea() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input", "layoutConfig": { "column": 2, "row": 7, "colSpan": 3 } } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		JsonNode layout = Element(guide, "LeadName").MobileValues!["layoutConfig"]!;
		layout["column"]!.GetValue<int>().Should().Be(1);
		layout["row"]!.GetValue<int>().Should().Be(1);
		layout["colSpan"]!.GetValue<int>().Should().Be(1);
	}

	[Test]
	[Description("ENG-94188 I3: a layoutConfig the web page carried as a NON-OBJECT (scalar/array) cannot hold `adaptive` and is replaced by the stack placement — string-indexing it directly would crash the whole guide with InvalidOperationException.")]
	public void Analyze_ShouldReplaceLayoutConfigInsteadOfCrashing_WhenCarriedLayoutConfigIsNotAnObject() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input", "layoutConfig": "legacy-scalar" },
					{ "name": "Status", "type": "crt.ComboBox", "layoutConfig": [ 2, 7 ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		JsonNode first = Element(guide, "LeadName").MobileValues!["layoutConfig"]!;
		first.Should().BeOfType<JsonObject>(because: "a scalar layoutConfig carries no adaptive placement, so the stack pass replaces it");
		first["column"]!.GetValue<int>().Should().Be(1);
		first["row"]!.GetValue<int>().Should().Be(1);
		JsonNode second = Element(guide, "Status").MobileValues!["layoutConfig"]!;
		second.Should().BeOfType<JsonObject>(because: "an array layoutConfig carries no adaptive placement, so the stack pass replaces it");
		second["row"]!.GetValue<int>().Should().Be(2);
	}

	[Test]
	[Description("ENG-94188 I3: children of a wrapper dissolved into the tab are retargeted into the Area with rows; the relocate-children entry itself is retargeted but gets no placement (it is not an element).")]
	public void Analyze_ShouldStackRelocatedWrapperChildrenInArea_WhenWrapperDissolvesIntoTab() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "Wrapper", "type": "crt.FlexContainer", "items": [
						{ "name": "LeadName", "type": "crt.Input" },
						{ "name": "Status", "type": "crt.ComboBox" } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(_, string area) = LayerNames("OverviewTab");
		ElementMapEntry wrapper = Element(guide, "Wrapper");
		wrapper.Operation.Should().Be("relocate-children");
		wrapper.ParentName.Should().Be(area, because: "its children are now placed in the Area");
		wrapper.MobileValues.Should().BeNull(because: "a dissolved wrapper is never created, so it carries no values");

		Element(guide, "LeadName").ParentName.Should().Be(area);
		Element(guide, "LeadName").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1);
		Element(guide, "Status").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2);
		guide.TabAreaLayers!.Single().MovedChildren.Should().Equal(new[] { "LeadName", "Status" },
			"the wrapper is a routing hint, not a component that occupies a row");
	}

	[Test]
	[Description("ENG-94188 I3: a multi-column grid inside a converted tab keeps its own adaptive columns, and only its placement in the Area is added — the grid's children stay inside the grid with their adaptive cells.")]
	public void Analyze_ShouldKeepNestedGridAdaptiveLayout_WhenMultiColumnGridSitsInsideTab() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "FieldsContainer", "type": "crt.GridContainer", "columns": [ "1fr", "1fr" ], "items": [
						{ "name": "LeadName", "type": "crt.Input", "layoutConfig": { "column": 1, "row": 1 } },
						{ "name": "Status", "type": "crt.ComboBox", "layoutConfig": { "column": 2, "row": 1 } } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(_, string area) = LayerNames("OverviewTab");
		// The grid moves into the Area and gets its stack placement…
		ElementMapEntry grid = Element(guide, "FieldsContainer");
		grid.ParentName.Should().Be(area);
		grid.MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1);
		// …while keeping the responsive columns the adaptive pass baked onto it.
		grid.MobileValues!["adaptive"]!["medium"]!["columns"].Should().NotBeNull();
		// Its children are NOT touched: they stay in the grid with their per-breakpoint cells.
		Element(guide, "LeadName").ParentName.Should().Be("FieldsContainer");
		Element(guide, "Status").MobileValues!["layoutConfig"]!["adaptive"]!["medium"]!["column"]!
			.GetValue<int>().Should().Be(2);
		guide.AdaptiveLayout!.Single().ContainerName.Should().Be("FieldsContainer");
	}

	[Test]
	[Description("ENG-94188 I3: an element the adaptive pass already placed per breakpoint keeps that adaptive placement — the stack pass must not flatten it back to a single base cell.")]
	public void Analyze_ShouldKeepAdaptivePlacement_WhenTabChildIsAlreadyPlacedPerBreakpoint() {
		// A web tab carrying its own `columns` makes the adaptive pass treat the tab as a multi-column grid, so
		// its direct children arrive at this pass already holding layoutConfig.adaptive.
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "columns": [ "1fr", "1fr" ], "items": [
					{ "name": "LeadName", "type": "crt.Input", "layoutConfig": { "column": 1, "row": 1 } },
					{ "name": "Status", "type": "crt.ComboBox", "layoutConfig": { "column": 2, "row": 1 } } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(_, string area) = LayerNames("OverviewTab");
		Element(guide, "Status").ParentName.Should().Be(area, because: "retargeting still happens");
		JsonNode layout = Element(guide, "Status").MobileValues!["layoutConfig"]!;
		layout["adaptive"].Should().NotBeNull(because: "mobile resolves the placement from adaptive when present");
		layout["adaptive"]!["medium"]!["column"]!.GetValue<int>().Should().Be(2);
		layout["row"].Should().BeNull(because: "a flat base cell would silently drop the responsive placement");
	}

	[Test]
	[Description("ENG-94188 AC#5: a converted tab with no content gets NO layers at all, so an empty Area is never created and never has to be deleted.")]
	public void Analyze_ShouldSynthesizeNoLayers_WhenConvertedTabIsEmpty() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "EmptyTab", "type": "crt.TabContainer", "items": [] },
				{ "name": "FullTab", "type": "crt.TabContainer", "items": [
					{ "name": "Budget", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(string emptyMain, string emptyArea) = LayerNames("EmptyTab");
		IndexOfMobile(guide, emptyMain).Should().Be(-1);
		IndexOfMobile(guide, emptyArea).Should().Be(-1);
		guide.TabAreaLayers!.Select(g => g.TabName).Should().Equal("FullTab");
	}

	[Test]
	[Description("ENG-94188: a tab the mobile TEMPLATE provides arrives as a merge twin and gets no synthesized layers — the template already carries its own body.")]
	public void Analyze_ShouldSynthesizeNoLayers_WhenTabIsTemplateMergeTwin() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "FeedTabContainer", "type": "crt.TabContainer", "items": [
					{ "name": "Feed", "type": "crt.Feed" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		Element(guide, "FeedTabContainer").Operation.Should().Be("merge");
		guide.TabAreaLayers.Should().BeNull();
		guide.ElementMap.Should().NotContain(e => e.WebName == null);
	}

	[Test]
	[Description("ENG-94188: the pass is switched by DATA — rules without a tabAreaLayers section synthesize nothing (existing conversions unchanged).")]
	public void Analyze_ShouldSkipTabAreaLayersPass_WhenRulesCarryNoTabAreaLayersSection() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle);

		guide.TabAreaLayers.Should().BeNull();
		guide.ElementMap.Should().NotContain(e => e.WebName == null);
		Element(guide, "LeadName").ParentName.Should().Be("OverviewTab");
		guide.Constraints.Should().NotContain(c => c.Contains("tabAreaLayers"),
			because: "with the pass off there is nothing baked to warn the caller about");
		guide.NextSteps.Should().NotContain(s => s.Contains("guide.tabAreaLayers"));
	}

	[Test]
	[Description("ENG-94188 I4: when layers were synthesized the guide TELLS the caller they are already baked — a constraint (do not reparent/reorder/add an Area) and a next step (state guide.tabAreaLayers when presenting the plan).")]
	public void Analyze_ShouldCarryMandatoryConstraintAndNextStep_WhenTabAreaLayersAreSynthesized() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		guide.Constraints.Should().ContainSingle(c => c.Contains("tabAreaLayers is MANDATORY"))
			.Which.Should().Contain("do NOT reparent", because: "the caller must apply the map as it is");
		guide.NextSteps.Should().ContainSingle(s => s.Contains("guide.tabAreaLayers"))
			.Which.Should().Contain("MANDATORY",
				because: "the mobile tab body is the team's required structure — the caller must not turn it into a question");
		// Lock-in: the tab body is NOT put up for approval the way adaptiveLayout is.
		guide.Constraints.Concat(guide.NextSteps).Where(t => t.Contains("tabAreaLayers"))
			.Should().OnlyContain(t => !t.Contains("decline") && !t.Contains("may adjust"),
				because: "offering to skip or alter the mandatory tab structure is exactly what must not leak into the guide");
	}

	[Test]
	[Description("ENG-94188: which element gets the layers comes from the rule's tabComponentType, not from a type hardcoded in the engine — pointing it at another container type moves the synthesis there.")]
	public void Analyze_ShouldWrapConfiguredComponentType_WhenRulePointsAtAnotherContainerType() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "Panel", "type": "crt.ExpansionPanel", "items": [
						{ "name": "LeadName", "type": "crt.Input" } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers("crt.ExpansionPanel"));

		// The rule now points at the panel, so the tab is left alone and the panel gets the layers.
		guide.TabAreaLayers!.Single().TabName.Should().Be("Panel");
		(string main, string area) = LayerNames("Panel");
		Synthesized(guide, main).ParentName.Should().Be("Panel");
		Synthesized(guide, area).ParentName.Should().Be(main);
		IndexOfMobile(guide, "MainTabContainer_" + WebToMobileAnalysisService.StableSuffix("Leads_FormPage", "OverviewTab"))
			.Should().Be(-1, because: "the tab type no longer matches the rule");
	}

	[Test]
	[Description("ENG-94188: an explicit empty tabComponentType leaves nothing to match against, so the pass switches itself off rather than wrapping every insert.")]
	public void Analyze_ShouldSkipTabAreaLayersPass_WhenTabComponentTypeIsBlank() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers(tabComponentType: null));

		guide.TabAreaLayers.Should().BeNull();
		guide.ElementMap.Should().NotContain(e => e.WebName == null);
	}

	[Test]
	[Description("ENG-94188: a wrapper with no mobile equivalent dissolves INTO the tab (relocate-children), which still counts as tab content — the tab gets its layers.")]
	public void Analyze_ShouldSynthesizeLayers_WhenTabContentIsOnlyADissolvedWrapper() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "Wrapper", "type": "crt.FlexContainer", "items": [
						{ "name": "LeadName", "type": "crt.Input" } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		Element(guide, "Wrapper").Operation.Should().Be("relocate-children");
		guide.TabAreaLayers!.Single().TabName.Should().Be("OverviewTab",
			because: "a dissolved wrapper still puts content in the tab, so the tab is not empty");
	}

	[Test]
	[Description("ENG-94188: synthesized names are reproducible across runs and distinct per tab, so repeated guide runs and baseline diffs stay stable.")]
	public void Analyze_ShouldSynthesizeDeterministicPerTabNames_WhenRunRepeatedly() {
		const string viewConfig = """
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] },
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [
					{ "name": "Budget", "type": "crt.Input" } ] } ] } ]
			""";

		MobilePageConversionGuide first = AnalyzeTabbed(Bundle(viewConfig), rules: RulesWithTabAreaLayers());
		MobilePageConversionGuide second = AnalyzeTabbed(Bundle(viewConfig), rules: RulesWithTabAreaLayers());

		first.TabAreaLayers!.Should().HaveCount(2);
		first.TabAreaLayers.Select(g => g.AreaName).Should().Equal(second.TabAreaLayers!.Select(g => g.AreaName));
		first.TabAreaLayers.Select(g => g.AreaName).Should().OnlyHaveUniqueItems(because: "each tab gets its own card");
		first.TabAreaLayers.Select(g => g.MainTabContainerName).Should().OnlyHaveUniqueItems();
	}

	[Test]
	[Description("ENG-94188: when a source element already owns a synthesized name, the shared suffix is extended so BOTH layer names stay free.")]
	public void Analyze_ShouldExtendSharedSuffix_WhenSourceElementOwnsASynthesizedName() {
		(string main, string area) = LayerNames("OverviewTab");
		PageBundleInfo bundle = Bundle($$"""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "{{main}}", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		TabAreaLayerGroup group = guide.TabAreaLayers!.Single();
		group.MainTabContainerName.Should().NotBe(main, because: "the source element keeps that name");
		group.MainTabContainerName.Should().StartWith(main, because: "the collision guard extends the hash prefix");
		group.AreaName.Should().StartWith(area, because: "both layers share one extended suffix");
		Element(guide, main).Operation.Should().Be("insert");
	}

	[Test]
	[Description("ENG-94188: a synthesized entry serializes without a webName key at all (not as null), so the guide never shows a phantom source element.")]
	public void Analyze_ShouldOmitWebNameKey_WhenSynthesizedEntryIsSerialized() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(string main, _) = LayerNames("OverviewTab");
		JsonObject json = JsonSerializer.SerializeToNode(Synthesized(guide, main))!.AsObject();
		json.ContainsKey("webName").Should().BeFalse();
		json["operation"]!.GetValue<string>().Should().Be("insert");
		JsonSerializer.SerializeToNode(Element(guide, "LeadName"))!.AsObject()
			.ContainsKey("webName").Should().BeTrue();
	}

	[Test]
	[Description("ENG-94188 AC#4: an expansion panel among the tab's top-level content does not join the shared Area — it gets its OWN detail Area card as a sibling in the tab body (shared Area row 1, detail Area row 2, panel at row 1 of its card), and the map keeps parent-before-child order.")]
	public void Analyze_ShouldCarryPanelIntoOwnDetailArea_WhenTabMixesFieldsAndPanel() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" },
					{ "name": "Status", "type": "crt.ComboBox" },
					{ "name": "SimilarLead", "type": "crt.ExpansionPanel", "items": [
						{ "name": "SimilarLeadName", "type": "crt.Input" } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		(string main, string area) = LayerNames("OverviewTab");
		string detail = DetailAreaName("OverviewTab", "SimilarLead");
		// The fields stay in the shared Area, stacked as before…
		Element(guide, "LeadName").ParentName.Should().Be(area);
		Element(guide, "Status").ParentName.Should().Be(area);
		Element(guide, "Status").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2);
		// …the panel sits in its own card, the sole child of it.
		Element(guide, "SimilarLead").ParentName.Should().Be(detail,
			because: "a detail-like panel must not share the tab's Area (AC#4)");
		Element(guide, "SimilarLead").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1,
			because: "the panel is the only child of its own single-column card");
		// In the tab body: the shared Area is row 1, the detail Area row 2 — both explicit, order under a
		// grid parent is not carried by the items order.
		Synthesized(guide, area).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1,
			because: "with a detail sibling beside it the shared Area needs an explicit placement");
		ElementMapEntry detailEntry = Synthesized(guide, detail);
		detailEntry.ParentName.Should().Be(main, because: "the detail Area is a sibling of the shared Area inside the tab body");
		detailEntry.MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2);
		// Parent-before-child in the final map: layer 2, shared Area, detail Area — right after the tab.
		int tabAt = IndexOfMobile(guide, "OverviewTab");
		IndexOfMobile(guide, main).Should().Be(tabAt + 1);
		IndexOfMobile(guide, area).Should().Be(tabAt + 2);
		IndexOfMobile(guide, detail).Should().Be(tabAt + 3,
			because: "applying entries in element-map order must create the detail card before the panel that points at it");

		TabAreaLayerGroup group = guide.TabAreaLayers!.Single();
		group.AreaName.Should().Be(area);
		group.MovedChildren.Should().Equal(new[] { "LeadName", "Status" },
			because: "the panel moved into its own detail Area, not into the shared one");
		TabDetailAreaGroup detailGroup = group.DetailAreas!.Single();
		detailGroup.PanelName.Should().Be("SimilarLead");
		detailGroup.AreaName.Should().Be(detail);
		detailGroup.Row.Should().Be(2);
	}

	[Test]
	[Description("ENG-94188 Р2: even when the web page puts the panel BEFORE the fields, the shared Area stays first (row 1) and the detail Area follows (row 2) — a deliberate divergence from the through-going web order, fixed in the target spec.")]
	public void Analyze_ShouldKeepSharedAreaFirst_WhenWebPagePutsPanelBeforeFields() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "SimilarLead", "type": "crt.ExpansionPanel", "items": [] },
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		(_, string area) = LayerNames("OverviewTab");
		string detail = DetailAreaName("OverviewTab", "SimilarLead");
		Synthesized(guide, area).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1,
			because: "the shared Area is always first regardless of where the panel stood on the web page");
		Synthesized(guide, detail).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2);
		Element(guide, "LeadName").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1,
			because: "inside the shared Area the field is still the first (and only) stacked child");
	}

	[Test]
	[Description("ENG-94188 AC#4/AC#5: a tab whose top-level content is panels ONLY gets no shared Area at all — the detail Areas take rows 1..N in the panels' web order, so an empty shared card is never created.")]
	public void Analyze_ShouldSynthesizeNoSharedArea_WhenTabContentIsPanelsOnly() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [
					{ "name": "OpportunityPlanning", "type": "crt.ExpansionPanel", "items": [] },
					{ "name": "Products", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		(string main, string area) = LayerNames("SalesTab");
		IndexOfMobile(guide, area).Should().Be(-1,
			because: "with no non-panel content there is nothing for a shared Area to hold (AC#5 one level down)");
		string firstDetail = DetailAreaName("SalesTab", "OpportunityPlanning");
		string secondDetail = DetailAreaName("SalesTab", "Products");
		Synthesized(guide, firstDetail).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1,
			because: "without a shared Area the detail rows start at 1");
		Synthesized(guide, secondDetail).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2);
		Synthesized(guide, firstDetail).ParentName.Should().Be(main);
		Element(guide, "OpportunityPlanning").ParentName.Should().Be(firstDetail);
		Element(guide, "Products").ParentName.Should().Be(secondDetail);

		TabAreaLayerGroup group = guide.TabAreaLayers!.Single();
		group.AreaName.Should().BeNull(because: "no shared Area was synthesized");
		group.MovedChildren.Should().BeEmpty(because: "every child moved into its own detail card");
		group.DetailAreas!.Select(d => d.PanelName).Should().Equal("OpportunityPlanning", "Products");
	}

	[Test]
	[Description("ENG-94188 AC#5: a wrapper of panels ONLY dissolved into the tab leaves just a routing hint beside the panels — a hint is not content, so no shared Area is synthesized, the hint is retargeted to the tab body, and the detail rows start at 1.")]
	public void Analyze_ShouldSynthesizeNoSharedArea_WhenDissolvedWrapperHoldsPanelsOnly() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [
					{ "name": "Wrapper", "type": "crt.FlexContainer", "items": [
						{ "name": "OpportunityPlanning", "type": "crt.ExpansionPanel", "items": [] },
						{ "name": "Products", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		(string main, string area) = LayerNames("SalesTab");
		IndexOfMobile(guide, area).Should().Be(-1,
			because: "a relocate-children hint is a routing note, not content — an Area that would hold nothing must not be created (AC#5 one level down)");
		ElementMapEntry wrapper = Element(guide, "Wrapper");
		wrapper.Operation.Should().Be("relocate-children");
		wrapper.ParentName.Should().Be(main,
			because: "with no shared Area the hint points at the tab body, where the detail Areas holding its surfaced children live");
		string firstDetail = DetailAreaName("SalesTab", "OpportunityPlanning");
		string secondDetail = DetailAreaName("SalesTab", "Products");
		Synthesized(guide, firstDetail).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1,
			because: "without a shared Area the detail rows start at 1 — nothing may leave a row-1 hole");
		Synthesized(guide, secondDetail).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2,
			because: "the second detail card stacks right under the first");
		Element(guide, "OpportunityPlanning").ParentName.Should().Be(firstDetail,
			because: "the surfaced panel is a top-level detail child of the tab (Р4)");
		Element(guide, "Products").ParentName.Should().Be(secondDetail,
			because: "each surfaced panel gets its own detail card");

		TabAreaLayerGroup group = guide.TabAreaLayers!.Single();
		group.AreaName.Should().BeNull(because: "no shared Area was synthesized");
		group.MovedChildren.Should().BeEmpty(because: "every real child moved into its own detail card");
		group.DetailAreas!.Select(d => d.PanelName).Should().Equal(new[] { "OpportunityPlanning", "Products" },
			"the detail cards follow the panels' web order");
	}

	[Test]
	[Description("ENG-94188 Р4: a dissolved wrapper holding a panel AND a field surfaces both to the tab's top level — the panel gets its own detail Area, the field lands in the shared Area, and the hint keeps pointing at that Area.")]
	public void Analyze_ShouldSplitSurfacedWrapperChildren_WhenDissolvedWrapperMixesFieldAndPanel() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "Wrapper", "type": "crt.FlexContainer", "items": [
						{ "name": "LeadName", "type": "crt.Input" },
						{ "name": "SimilarLead", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		(_, string area) = LayerNames("OverviewTab");
		string detail = DetailAreaName("OverviewTab", "SimilarLead");
		Element(guide, "LeadName").ParentName.Should().Be(area,
			because: "the surfaced field is non-detail content and joins the shared Area");
		Element(guide, "LeadName").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1,
			because: "the field is the first (and only) stacked child of the shared Area");
		Element(guide, "SimilarLead").ParentName.Should().Be(detail,
			because: "a surfaced top-level panel still becomes a detail, exactly as a directly-placed one (Р4)");
		Element(guide, "Wrapper").ParentName.Should().Be(area,
			because: "with a shared Area synthesized the hint keeps pointing at it, as before this pass learned about details");
		Synthesized(guide, area).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1,
			because: "with a detail sibling beside it the shared Area needs an explicit placement");
		Synthesized(guide, detail).MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2,
			because: "the detail card stacks under the shared Area");

		TabAreaLayerGroup group = guide.TabAreaLayers!.Single();
		group.AreaName.Should().Be(area, because: "the surfaced field keeps the shared Area alive");
		group.MovedChildren.Should().Equal(new[] { "LeadName" },
			"the panel moved into its own detail card, and the hint occupies no row");
		group.DetailAreas!.Single().PanelName.Should().Be("SimilarLead");
	}

	[Test]
	[Description("ENG-94188 Р3: the panel is carried into its detail Area AS-IS — every web property (toggleType, togglePosition, labelColor, fullWidthHeader, titleWidth, fitContent, expanded) survives untouched, no alignItems is added, and only parentName + the single-child placement change; the panel's own children stay inside it.")]
	public void Analyze_ShouldCarryPanelAsIs_WhenPanelMovesIntoDetailArea() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "SimilarLead", "type": "crt.ExpansionPanel",
					  "toggleType": "arrow", "togglePosition": "right", "labelColor": "#333333",
					  "fullWidthHeader": true, "titleWidth": 200, "fitContent": true, "expanded": true,
					  "items": [ { "name": "SimilarLeadName", "type": "crt.Input" } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		JsonObject panel = Element(guide, "SimilarLead").MobileValues!.AsObject();
		panel["toggleType"]!.GetValue<string>().Should().Be("arrow", because: "prop cleanup is deferred with the general de-skin (Р3)");
		panel["togglePosition"]!.GetValue<string>().Should().Be("right");
		panel["labelColor"]!.GetValue<string>().Should().Be("#333333");
		panel["fullWidthHeader"]!.GetValue<bool>().Should().BeTrue();
		panel["titleWidth"]!.GetValue<int>().Should().Be(200);
		panel["fitContent"]!.GetValue<bool>().Should().BeTrue();
		panel["expanded"]!.GetValue<bool>().Should().BeTrue(because: "expanded exists on mobile too and must survive (Р6)");
		panel.ContainsKey("alignItems").Should().BeFalse(because: "the pass must not add properties either — the panel goes as-is");
		panel["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(1);
		Element(guide, "SimilarLeadName").ParentName.Should().Be("SimilarLead",
			because: "the panel's inner content is none of this pass's business (Р4)");
	}

	[Test]
	[Description("ENG-94188 Р7: a detail Area carries the SAME areaContainer values from the rules as the shared Area — no separate props block exists for the detail variant.")]
	public void Analyze_ShouldReuseSharedAreaRuleValues_WhenDetailAreaIsSynthesized() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" },
					{ "name": "SimilarLead", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		(_, string area) = LayerNames("OverviewTab");
		JsonObject sharedValues = Synthesized(guide, area).MobileValues!.AsObject();
		JsonObject detailValues = Synthesized(guide, DetailAreaName("OverviewTab", "SimilarLead")).MobileValues!.AsObject();
		foreach (string key in new[] { "type", "color", "borderRadius" }) {
			detailValues[key]!.GetValue<string>().Should().Be(sharedValues[key]!.GetValue<string>(),
				because: $"the detail Area reuses the shared areaContainer rule values verbatim ('{key}', Р7)");
		}
		detailValues["items"]!.AsArray().Should().BeEmpty(because: "a synthesized container needs an initialized slot for its child");
	}

	[Test]
	[Description("ENG-94188: detail Area names are per-panel deterministic — repeated runs reproduce them, sibling panels of one tab get distinct suffixes, and a collision with a source element extends the hash prefix instead of renaming randomly.")]
	public void Analyze_ShouldSynthesizeDeterministicPerPanelNames_WhenTabHoldsSeveralPanels() {
		const string viewConfig = """
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [
					{ "name": "OpportunityPlanning", "type": "crt.ExpansionPanel", "items": [] },
					{ "name": "Products", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ]
			""";

		MobilePageConversionGuide first = AnalyzeTabbed(Bundle(viewConfig), rules: RulesWithPanelDetails());
		MobilePageConversionGuide second = AnalyzeTabbed(Bundle(viewConfig), rules: RulesWithPanelDetails());

		IReadOnlyList<TabDetailAreaGroup> details = first.TabAreaLayers!.Single().DetailAreas!;
		details.Select(d => d.AreaName).Should().Equal(second.TabAreaLayers!.Single().DetailAreas!.Select(d => d.AreaName),
			because: "repeated guide runs and baseline diffs must reproduce identical detail names");
		details.Select(d => d.AreaName).Should().OnlyHaveUniqueItems(because: "each panel gets its own card");

		// A source element already owning the first panel's detail name forces a deterministic extension.
		string detailName = DetailAreaName("SalesTab", "OpportunityPlanning");
		PageBundleInfo collided = Bundle($$"""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [
					{ "name": "{{detailName}}", "type": "crt.Input" },
					{ "name": "OpportunityPlanning", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ]
			""");
		MobilePageConversionGuide guide = AnalyzeTabbed(collided, rules: RulesWithPanelDetails());
		string collidedDetail = guide.TabAreaLayers!.Single().DetailAreas!.Single().AreaName;
		collidedDetail.Should().NotBe(detailName, because: "the source element keeps its name");
		collidedDetail.Should().StartWith(detailName, because: "the collision guard extends the hash prefix deterministically");
	}

	[Test]
	[Description("ENG-94188 Р4: only the tab's TOP-LEVEL panels become details — a panel nested inside a grid container keeps its place, and a panel inside another panel stays inside it.")]
	public void Analyze_ShouldLeaveNestedPanelsUntouched_WhenPanelIsNotTopLevelTabContent() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "FieldsContainer", "type": "crt.GridContainer", "items": [
						{ "name": "NestedPanel", "type": "crt.ExpansionPanel", "items": [] } ] },
					{ "name": "OuterPanel", "type": "crt.ExpansionPanel", "items": [
						{ "name": "InnerPanel", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		Element(guide, "NestedPanel").ParentName.Should().Be("FieldsContainer",
			because: "a panel inside a container is ordinary content of that container (Р4)");
		Element(guide, "InnerPanel").ParentName.Should().Be("OuterPanel",
			because: "a panel inside a panel is the outer panel's own content (Р4)");
		guide.TabAreaLayers!.Single().DetailAreas!.Single().PanelName.Should().Be("OuterPanel",
			because: "only the top-level panel gets its own detail Area");
	}

	[Test]
	[Description("ENG-94188: a tab the mobile TEMPLATE provides is a merge twin and stays out of the pass entirely — a panel inside it gets no detail Area.")]
	public void Analyze_ShouldSynthesizeNoDetailArea_WhenPanelSitsInTemplateMergeTab() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "FeedTabContainer", "type": "crt.TabContainer", "items": [
					{ "name": "FeedPanel", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		Element(guide, "FeedTabContainer").Operation.Should().Be("merge");
		guide.TabAreaLayers.Should().BeNull(because: "merge tabs get no synthesized layers at all");
		guide.ElementMap.Should().NotContain(e => e.WebName == null,
			because: "nothing may be synthesized for a template-provided tab, panels included");
	}

	[Test]
	[Description("ENG-94188 data compatibility: without detailComponentTypes (or with an empty list) the panel keeps the pre-detail behavior — it joins the shared Area as an ordinary stacked child and no detail Area exists.")]
	public void Analyze_ShouldKeepPanelInSharedArea_WhenRulesCarryNoDetailComponentTypes() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" },
					{ "name": "SimilarLead", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		(_, string area) = LayerNames("OverviewTab");
		Element(guide, "SimilarLead").ParentName.Should().Be(area,
			because: "an empty detail-type list is the data switch back to the shared-Area behavior");
		Element(guide, "SimilarLead").MobileValues!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2);
		TabAreaLayerGroup group = guide.TabAreaLayers!.Single();
		group.MovedChildren.Should().Equal(new[] { "LeadName", "SimilarLead" },
			because: "the panel is then an ordinary moved child");
		group.DetailAreas.Should().BeNull(because: "no detail Areas exist with the feature switched off");
		Synthesized(guide, area).MobileValues!.AsObject().ContainsKey("layoutConfig").Should().BeFalse(
			because: "a shared Area that is the only child of the tab body still carries no placement of its own");
	}

	[Test]
	[Description("ENG-94188 snapshot: a page mixing all three tab shapes (fields+panel, panels-only, fields-only) lays out every tab correctly in ONE map — each tab's layers still sit right after its own entry despite the varying number of inserts of the earlier tabs.")]
	public void Analyze_ShouldLayOutWholeTabbedPage_WhenTabsMixSharedAndDetailAreas() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" },
					{ "name": "SimilarLead", "type": "crt.ExpansionPanel", "items": [] } ] },
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [
					{ "name": "OpportunityPlanning", "type": "crt.ExpansionPanel", "items": [] },
					{ "name": "Products", "type": "crt.ExpansionPanel", "items": [] } ] },
				{ "name": "ProcessingTab", "type": "crt.TabContainer", "items": [
					{ "name": "Notes", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithPanelDetails());

		guide.TabAreaLayers!.Should().HaveCount(3, because: "every content-bearing converted tab gets its layers");
		TabAreaLayerGroup overview = guide.TabAreaLayers![0];
		overview.AreaName.Should().NotBeNull();
		overview.MovedChildren.Should().Equal(new[] { "LeadName" });
		overview.DetailAreas!.Single().Row.Should().Be(2);
		TabAreaLayerGroup sales = guide.TabAreaLayers![1];
		sales.AreaName.Should().BeNull(because: "the panels-only tab gets no shared Area");
		sales.DetailAreas!.Select(d => d.Row).Should().Equal(1, 2);
		TabAreaLayerGroup processing = guide.TabAreaLayers![2];
		processing.AreaName.Should().NotBeNull();
		processing.DetailAreas.Should().BeNull(because: "a tab without panels has no detail cards");

		// The second tab's layers must sit right after ITS entry in the final map — the first tab inserted
		// THREE layers (body + shared + detail), so a stale pre-insert index would misplace everything here.
		int salesAt = IndexOfMobile(guide, "SalesTab");
		IndexOfMobile(guide, sales.MainTabContainerName).Should().Be(salesAt + 1);
		IndexOfMobile(guide, sales.DetailAreas![0].AreaName).Should().Be(salesAt + 2,
			because: "with no shared Area the first detail directly follows the tab body");
		IndexOfMobile(guide, sales.DetailAreas![1].AreaName).Should().Be(salesAt + 3);
		int processingAt = IndexOfMobile(guide, "ProcessingTab");
		IndexOfMobile(guide, processing.MainTabContainerName).Should().Be(processingAt + 1);
		IndexOfMobile(guide, processing.AreaName).Should().Be(processingAt + 2);
		Synthesized(guide, processing.AreaName).MobileValues!.AsObject().ContainsKey("layoutConfig").Should().BeFalse(
			because: "a shared Area alone in the tab body keeps carrying no placement");
	}

	#endregion

	#region Stable suffix (synthesized tab-layer names, ENG-94188)

	[Test]
	[Description("StableSuffix is a pure content hash: identical inputs produce the identical 7-char lowercase base36 suffix on every run (reproducible baselines).")]
	public void StableSuffix_ShouldReturnIdenticalSuffix_WhenInputsAreEqual() {
		string first = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_x1y2z3");
		string second = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_x1y2z3");

		first.Should().Be(second);
		first.Should().MatchRegex("^[0-9a-z]{7}$", "the suffix must look like a designer-generated one (7 lowercase base36 chars)");
	}

	[Test]
	[Description("Different tabs (and different source pages) hash to different suffixes, so synthesized names never collide across tabs.")]
	public void StableSuffix_ShouldReturnDistinctSuffixes_WhenInputsDiffer() {
		string tabA = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_a");
		string tabB = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_b");
		string otherPage = WebToMobileAnalysisService.StableSuffix("UsrOrder_FormPage", "Tab_a");

		tabA.Should().NotBe(tabB);
		tabA.Should().NotBe(otherPage);
	}

	[Test]
	[Description("A collision (the name already exists in the element map or the mobile template) deterministically EXTENDS the suffix with further hash characters — never a random rename.")]
	public void StableSuffix_ShouldExtendSuffixDeterministically_WhenCandidateIsTaken() {
		string free = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_x1y2z3");
		var taken = new HashSet<string> { free, free + "-unrelated" };

		string extendedFirst = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_x1y2z3", taken.Contains);
		string extendedSecond = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_x1y2z3", taken.Contains);

		extendedFirst.Should().Be(extendedSecond, "the extension must be as reproducible as the base suffix");
		extendedFirst.Should().StartWith(free, "the collision guard extends the hash prefix rather than replacing it");
		extendedFirst.Length.Should().Be(free.Length + 1);
	}

	[Test]
	[Description("Without a collision guard the suffix is the plain 7-char hash prefix; a guard that reports everything free returns the same value.")]
	public void StableSuffix_ShouldReturnBareHashPrefix_WhenGuardReportsNoCollision() {
		string bare = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_x1y2z3");
		string guarded = WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_x1y2z3", _ => false);

		guarded.Should().Be(bare);
	}

	[Test]
	[Description("GOLDEN VALUE — a compatibility contract, not a regular assertion: the suffix for a fixed input is pinned to the exact literal StableSuffix produced when ENG-94188 shipped. The other suffix tests compare the function to itself, so ONLY this literal can catch a silent change to the hash input format ($\"{page}:{tab}\"), algorithm (SHA-256), encoding (lowercase base36) or padding (PadLeft 7) — any of which renames every synthesized container in users' existing conversion baselines while the rest of the suite stays green. Do NOT update the literal to make the test pass; changing it is a deliberate baseline-migration decision.")]
	public void StableSuffix_ShouldReturnPinnedGoldenValue_WhenInputIsBaselineFixture() {
		WebToMobileAnalysisService.StableSuffix("UsrLead_FormPage", "Tab_x1y2z3").Should().Be("2vijwqq",
			because: "the suffix is part of the on-page name compatibility contract — a repeated conversion of the same page must synthesize the very same names it did on the day the feature shipped");
	}

	[Test]
	[Description("GOLDEN VALUE through the PUBLIC guide output: the full synthesized layer names for the tabbed fixture are pinned to the exact literals a real Analyze produced when ENG-94188 shipped, so the whole naming pipeline (prefix from the rules + StableSuffix over the page/tab pair) is locked end to end, not just the hash helper. Do NOT update the literals to make the test pass; changing them is a deliberate baseline-migration decision.")]
	public void Analyze_ShouldReproducePinnedGoldenNames_WhenConvertingTabbedFixture() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: RulesWithTabAreaLayers());

		TabAreaLayerGroup group = guide.TabAreaLayers!.Single();
		group.MainTabContainerName.Should().Be("MainTabContainer_4fjmsq8",
			because: "re-running the guide over an unchanged page must reproduce the exact names of the user's existing baseline");
		group.AreaName.Should().Be("GridContainer_4fjmsq8",
			because: "re-running the guide over an unchanged page must reproduce the exact names of the user's existing baseline");
		Element(guide, "LeadName").ParentName.Should().Be("GridContainer_4fjmsq8",
			because: "the moved child must point at the same pinned Area name");
	}

	#endregion

	#region Spacing normalization on inserted containers (ENG-91228)

	private static readonly IReadOnlySet<string> SpacingMobileTypes =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.GridContainer", "crt.FlexContainer", "crt.Input", "crt.TabContainer"
		};

	private static readonly IReadOnlyList<InsertValueOverrideRule> SpacingOverrides = [
		new InsertValueOverrideRule {
			Type = "crt.GridContainer",
			Values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
				"""{ "gap": { "rowGap": "medium", "columnGap": "medium" } }""")
		},
		new InsertValueOverrideRule {
			Type = "crt.FlexContainer",
			Values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{ "gap": "medium" }""")
		}
	];

	private static WebToMobilePageConversionRules RulesWithSpacingOverrides() => new() {
		InsertValueOverrides = SpacingOverrides
	};

	private static MobilePageConversionGuide AnalyzeSpacing(PageBundleInfo bundle, WebToMobilePageConversionRules rules) =>
		WebToMobileAnalysisService.Analyze(
			bundle, SpacingMobileTypes, WebTypes,
			webByType: Reg(("crt.FlexContainer", true), ("crt.GridContainer", true), ("crt.Input", false)),
			mobileByType: null, rules, templateRule: null,
			sourcePage: "UsrApp_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrApp_MobileFormPage", containerNameMap: null);

	[Test]
	[Description("ENG-91228: a converted grid container's web gap (any value, e.g. the canonical columnGap large / rowGap none) is DISCARDED, not translated — the insert carries the mobile-standard gap Medium on both axes, and the advisory section lists the container.")]
	public void Analyze_SpacingNormalization_ShouldReplaceWebGridGapWithMedium() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "InfoGrid", "type": "crt.GridContainer",
			    "gap": { "columnGap": "large", "rowGap": "none" },
			    "items": [ { "name": "LeadName", "type": "crt.Input", "control": "$LeadName" } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeSpacing(bundle, RulesWithSpacingOverrides());

		JsonObject vals = Element(guide, "InfoGrid").MobileValues!.AsObject();
		vals["gap"]!["rowGap"]!.GetValue<string>().Should().Be("medium");
		vals["gap"]!["columnGap"]!.GetValue<string>().Should().Be("medium",
			because: "the web spacing is ignored by design — mobile follows the mobile spacing standard");
		SpacingNormalizationEntry entry = guide.SpacingNormalization!.Normalized.Single(n => n.Name == "InfoGrid");
		entry.Type.Should().Be("crt.GridContainer");
		entry.Properties.Should().Equal("gap");
	}

	[Test]
	[Description("ENG-91228: a flex container's web gap of ANY shape (px number, none, CSS string) becomes the Medium token, and a flex container that carried NO gap at all still gets the explicit Medium — the converted body must be self-describing, not lean on client defaults.")]
	public void Analyze_SpacingNormalization_ShouldStampMediumOnFlex_WhateverTheWebCarried() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "TightRow", "type": "crt.FlexContainer", "gap": 0, "items": [
				{ "name": "LeadName", "type": "crt.Input", "control": "$LeadName" } ] },
			  { "name": "PlainColumn", "type": "crt.FlexContainer", "items": [
				{ "name": "Status", "type": "crt.Input", "control": "$Status" } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeSpacing(bundle, RulesWithSpacingOverrides());

		Element(guide, "TightRow").MobileValues!["gap"]!.GetValue<string>().Should().Be("medium",
			because: "a web gap 0/none is deliberately overridden — the known trade-off of the normalization");
		Element(guide, "PlainColumn").MobileValues!["gap"]!.GetValue<string>().Should().Be("medium",
			because: "a container without a web gap gets the explicit default added");
		guide.SpacingNormalization!.Normalized.Select(n => n.Name)
			.Should().BeEquivalentTo("TightRow", "PlainColumn");
	}

	[Test]
	[Description("ENG-91228: the pass runs AFTER the tab-area synthesis, so the synthesized tab-body grid and Area card are normalized by the SAME rule as converted containers — the invariant is 'every inserted Grid/Flex carries gap Medium', whatever its origin.")]
	public void Analyze_SpacingNormalization_ShouldCoverSynthesizedTabLayers() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");
		WebToMobilePageConversionRules baseRules = RulesWithTabAreaLayers();
		var rules = new WebToMobilePageConversionRules {
			Components = baseRules.Components,
			TabAreaLayers = baseRules.TabAreaLayers,
			InsertValueOverrides = SpacingOverrides
		};

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: rules);

		(string main, string area) = LayerNames("OverviewTab");
		foreach (string name in new[] { main, area }) {
			JsonObject vals = Synthesized(guide, name).MobileValues!.AsObject();
			vals["gap"]!["rowGap"]!.GetValue<string>().Should().Be("medium", because: $"{name} is an inserted grid like any other");
			vals["gap"]!["columnGap"]!.GetValue<string>().Should().Be("medium");
		}
		guide.SpacingNormalization!.Normalized.Select(n => n.Name).Should().Contain(new[] { main, area });
	}

	[Test]
	[Description("ENG-91228: a merge twin the mobile template provides is NEVER touched by the normalization — no values are stamped onto it and it is absent from the advisory list.")]
	public void Analyze_SpacingNormalization_ShouldNeverTouchMergeTwins() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");
		WebToMobilePageConversionRules baseRules = RulesWithTabAreaLayers();
		var rules = new WebToMobilePageConversionRules {
			Components = baseRules.Components,
			TabAreaLayers = baseRules.TabAreaLayers,
			InsertValueOverrides = SpacingOverrides
		};

		MobilePageConversionGuide guide = AnalyzeTabbed(bundle, rules: rules);

		ElementMapEntry tabs = Element(guide, "Tabs");
		tabs.Operation.Should().Be("merge", because: "the fixture maps Tabs onto the template's own Tabs");
		tabs.MobileValues.Should().BeNull(because: "a merge twin gets nothing stamped onto it");
		guide.SpacingNormalization!.Normalized.Select(n => n.Name).Should().NotContain("Tabs");
	}

	[Test]
	[Description("ENG-91228: the pass is switched by DATA — with no insertValueOverrides group in the rules the web gap is carried verbatim (the pre-normalization behavior) and the advisory section is null.")]
	public void Analyze_SpacingNormalization_ShouldBeNoOp_WhenRulesGroupAbsent() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "InfoGrid", "type": "crt.GridContainer",
			    "gap": { "columnGap": "large", "rowGap": "none" },
			    "items": [ { "name": "LeadName", "type": "crt.Input", "control": "$LeadName" } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeSpacing(bundle, new WebToMobilePageConversionRules());

		JsonObject vals = Element(guide, "InfoGrid").MobileValues!.AsObject();
		vals["gap"]!["columnGap"]!.GetValue<string>().Should().Be("large",
			because: "without the rules group the property-carry behavior is unchanged");
		vals["gap"]!["rowGap"]!.GetValue<string>().Should().Be("none");
		guide.SpacingNormalization.Should().BeNull();
	}

	[Test]
	[Description("ENG-91228: a rules file can never override an element's identity — 'type' (and 'name') entries in the override values are ignored, other listed properties still apply.")]
	public void Analyze_SpacingNormalization_ShouldIgnoreIdentityKeysInOverrides() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "InfoGrid", "type": "crt.GridContainer", "items": [
				{ "name": "LeadName", "type": "crt.Input", "control": "$LeadName" } ] } ]
			""");
		var rules = new WebToMobilePageConversionRules {
			InsertValueOverrides = [
				new InsertValueOverrideRule {
					Type = "crt.GridContainer",
					Values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
						"""{ "type": "crt.Label", "name": "Hijacked", "gap": { "rowGap": "medium", "columnGap": "medium" } }""")
				}
			]
		};

		MobilePageConversionGuide guide = AnalyzeSpacing(bundle, rules);

		JsonObject vals = Element(guide, "InfoGrid").MobileValues!.AsObject();
		vals["type"]!.GetValue<string>().Should().Be("crt.GridContainer", because: "identity keys are never overridable");
		vals.ContainsKey("name").Should().BeFalse();
		vals["gap"]!["rowGap"]!.GetValue<string>().Should().Be("medium");
		SpacingNormalizationEntry entry = guide.SpacingNormalization!.Normalized.Single(n => n.Name == "InfoGrid");
		entry.Properties.Should().Equal("gap");
	}

	#endregion

	#region Empty container removal (ENG-91228)

	private static readonly IReadOnlySet<string> EmptyRemovalMobileTypes =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"crt.TabPanel", "crt.TabContainer", "crt.FlexContainer", "crt.GridContainer",
			"crt.ExpansionPanel", "crt.Input", "crt.ComboBox"
		};

	private static readonly EmptyContainerRemovalRule EmptyRemoval = new() {
		RemovableTypes = ["crt.FlexContainer", "crt.GridContainer", "crt.TabPanel", "crt.TabContainer", "crt.ExpansionPanel"]
	};

	private static WebToMobilePageConversionRules RulesWithEmptyRemoval() => new() {
		Components = GridRule.Components,
		EmptyContainerRemoval = EmptyRemoval
	};

	private static WebToMobilePageConversionRules RulesWithEmptyRemovalAndTabLayers() => new() {
		Components = GridRule.Components,
		TabAreaLayers = RulesWithPanelDetails().TabAreaLayers,
		EmptyContainerRemoval = EmptyRemoval
	};

	private static MobilePageConversionGuide AnalyzeWithEmptyRemoval(
		PageBundleInfo bundle,
		WebToMobilePageConversionRules rules = null,
		IReadOnlyDictionary<string, string> containerNameMap = null,
		IReadOnlyList<WebToMobileAnalysisService.PositionalPlacement> positionalPlacements = null,
		IReadOnlyDictionary<string, string> mobileContainerParents = null,
		PageBusinessRuleProbeResult pageBusinessRulesProbe = null) =>
		WebToMobileAnalysisService.Analyze(
			bundle, EmptyRemovalMobileTypes,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crt.Timeline" },
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, rules ?? RulesWithEmptyRemoval(), templateRule: null,
			sourcePage: "Leads_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrLeads_MobileFormPage", containerNameMap: containerNameMap ?? TabbedContainerMap,
			positionalPlacements: positionalPlacements, mobileContainerParents: mobileContainerParents,
			pageBusinessRulesProbe: pageBusinessRulesProbe);

	[Test]
	[Description("ENG-91228: a converter-created container whose every child dropped is itself converted to a drop with reason 'empty container', and the guide's constraints warn the reader not to re-create it.")]
	public void Analyze_ShouldDropEmptyContainer_WhenNoChildSurvives() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "OnlyUnsupported", "type": "crt.GridContainer", "items": [
				{ "name": "Timeline", "type": "crt.Timeline" } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		ElementMapEntry box = Element(guide, "OnlyUnsupported");
		box.Operation.Should().Be("drop");
		box.Reason.Should().Contain("empty container");
		box.WebType.Should().Be("crt.GridContainer", because: "the report must still say what was removed");
		box.MobileName.Should().BeNull(because: "a drop carries no mobile target");
		Element(guide, "Timeline").Operation.Should().Be("drop", because: "the child's own drop is what emptied the box");
		guide.Constraints.Should().Contain(c => c.Contains("empty container"),
			because: "the reader must be told the removal already happened and is not theirs to redo or undo");
	}

	[Test]
	[Description("ENG-91228: one surviving child keeps its container — only containers with NO surviving child are removed.")]
	public void Analyze_ShouldKeepContainer_WhenAnyChildSurvives() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "MixedBox", "type": "crt.GridContainer", "items": [
				{ "name": "LeadName", "type": "crt.Input" },
				{ "name": "Timeline", "type": "crt.Timeline" } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		Element(guide, "MixedBox").Operation.Should().Be("insert");
		Element(guide, "LeadName").ParentName.Should().Be("MixedBox");
		guide.Constraints.Should().NotContain(c => c.Contains("empty container"),
			because: "with nothing removed there is nothing to warn about");
	}

	[Test]
	[Description("ENG-91228: emptiness cascades bottom-up — a FlexContainer holding only an empty GridContainer drops together with it.")]
	public void Analyze_ShouldCascadeRemoval_WhenWrapperHoldsOnlyEmptyContainer() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Wrapper", "type": "crt.FlexContainer", "items": [
				{ "name": "InnerGrid", "type": "crt.GridContainer", "items": [
					{ "name": "Timeline", "type": "crt.Timeline" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		Element(guide, "InnerGrid").Operation.Should().Be("drop");
		Element(guide, "InnerGrid").Reason.Should().Contain("empty container");
		Element(guide, "Wrapper").Operation.Should().Be("drop",
			because: "after the inner grid left, the wrapper holds nothing — the removal must cascade");
		Element(guide, "Wrapper").Reason.Should().Contain("empty container");
	}

	[Test]
	[Description("ENG-91228: a child with visible:false COUNTS as content — it is hidden at runtime only and must keep its designer home, so its container survives.")]
	public void Analyze_ShouldKeepContainer_WhenOnlyChildIsHiddenOnly() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "HiddenBox", "type": "crt.GridContainer", "items": [
				{ "name": "SecretField", "type": "crt.Input", "visible": false } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		Element(guide, "HiddenBox").Operation.Should().Be("insert");
		ElementMapEntry field = Element(guide, "SecretField");
		field.Operation.Should().Be("insert");
		field.MobileValues!["visible"]!.GetValue<bool>().Should().BeFalse(
			because: "the hidden child is carried, which is exactly why its container is not empty");
	}

	[Test]
	[Description("ENG-91228: a container whose items is a COLLECTION BINDING (a string, not an array) is a repeater with data, not empty scaffolding — it is kept.")]
	public void Analyze_ShouldKeepRepeaterContainer_WhenItemsIsACollectionBinding() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "RepeaterContainer", "type": "crt.FlexContainer", "items": "$Payments" } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		ElementMapEntry repeater = Element(guide, "RepeaterContainer");
		repeater.Operation.Should().Be("insert");
		repeater.MobileValues!["items"]!.GetValue<string>().Should().Be("$Payments",
			because: "the binding IS the container's content — deleting the shell would delete the repeater");
	}

	[Test]
	[Description("ENG-91228 (decision 2026-08-03): an ExpansionPanel is judged on items ONLY — an empty panel drops, and the tab it emptied cascades away, while the template Tabs twin stays a merge untouched.")]
	public void Analyze_ShouldDropEmptyExpansionPanel_AndCascadeIntoTab() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "OverviewTab", "type": "crt.TabContainer", "items": [
					{ "name": "EmptyPanel", "type": "crt.ExpansionPanel", "items": [] } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		Element(guide, "EmptyPanel").Operation.Should().Be("drop");
		Element(guide, "EmptyPanel").Reason.Should().Contain("empty container");
		Element(guide, "OverviewTab").Operation.Should().Be("drop",
			because: "the panel was the tab's only content, so the tab empties and cascades away");
		Element(guide, "Tabs").Operation.Should().Be("merge",
			because: "a template merge twin is structurally out of the removal's reach, however empty its converted content");
	}

	[Test]
	[Description("ENG-91228 (items-only decision): a panel with header buttons in tools but an empty items still drops — and the discarded tools are called out in the drop reason so the loss stays visible in the report.")]
	public void Analyze_ShouldMentionDiscardedTools_WhenEmptyPanelCarriesToolsButtons() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "ToolsOnlyPanel", "type": "crt.ExpansionPanel",
			    "tools": [ { "name": "AddButton", "type": "crt.Button" } ], "items": [] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		ElementMapEntry panel = Element(guide, "ToolsOnlyPanel");
		panel.Operation.Should().Be("drop");
		panel.Reason.Should().Contain("empty container");
		panel.Reason.Should().Contain("tools", because: "silent removal is acceptable only while the discarded tools stay visible in the report");
	}

	[Test]
	[Description("ENG-91228: the pass is switched by DATA — without an emptyContainerRemoval rules section the empty container is still inserted, exactly as before the feature.")]
	public void Analyze_ShouldSkipRemovalPass_WhenRulesCarryNoSection() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "OnlyUnsupported", "type": "crt.GridContainer", "items": [
				{ "name": "Timeline", "type": "crt.Timeline" } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle, rules: GridRule);

		Element(guide, "OnlyUnsupported").Operation.Should().Be("insert");
		guide.Constraints.Should().NotContain(c => c.Contains("empty container"));
	}

	[Test]
	[Description("ENG-91228 + ENG-94188: the removal runs BEFORE the tab-area synthesis — a removed empty tab gets NO layers (nothing resurrects it), while its content-bearing sibling keeps the full two-layer body.")]
	public void Analyze_ShouldSynthesizeNoLayers_WhenEmptyTabWasRemoved() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "EmptyTab", "type": "crt.TabContainer", "items": [
					{ "name": "Timeline", "type": "crt.Timeline" } ] },
				{ "name": "FullTab", "type": "crt.TabContainer", "items": [
					{ "name": "LeadName", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle, rules: RulesWithEmptyRemovalAndTabLayers());

		Element(guide, "EmptyTab").Operation.Should().Be("drop");
		guide.TabAreaLayers!.Single().TabName.Should().Be("FullTab",
			because: "only the surviving tab gets the designer's two-layer body");
		(string emptyMain, string emptyArea) = LayerNames("EmptyTab");
		IndexOfMobile(guide, emptyMain).Should().Be(-1, because: "a removed tab must not be resurrected by synthesized layers");
		IndexOfMobile(guide, emptyArea).Should().Be(-1);
		(string fullMain, string fullArea) = LayerNames("FullTab");
		Synthesized(guide, fullMain).ParentName.Should().Be("FullTab");
		Element(guide, "LeadName").ParentName.Should().Be(fullArea);
	}

	[Test]
	[Description("ENG-91228: a page's OWN inserted TabPanel (no template twin) whose every tab emptied cascades away completely — no tabless panel shell survives.")]
	public void Analyze_ShouldDropOwnTabPanelShell_WhenEveryTabEmptied() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "OwnTabs", "type": "crt.TabPanel", "items": [
				{ "name": "FirstTab", "type": "crt.TabContainer", "items": [
					{ "name": "Timeline", "type": "crt.Timeline" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(
			bundle, containerNameMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

		Element(guide, "FirstTab").Operation.Should().Be("drop");
		Element(guide, "OwnTabs").Operation.Should().Be("drop",
			because: "a panel whose every tab left is the most pointless shell of all — the cascade must reach it");
	}

	[Test]
	[Description("ENG-91228: positional :top indexes are re-compacted after removal — dropping the middle sibling leaves no hole, so the survivors land at contiguous positions above the mobile anchor.")]
	public void Analyze_ShouldCompactPositionalIndexes_WhenPositionalSiblingIsRemoved() {
		PageBundleInfo bundle = Bundle("""
			[
			  { "name": "TopBox", "type": "crt.FlexContainer", "items": [ { "name": "ProgressBar", "type": "crt.Input" } ] },
			  { "name": "TopEmpty", "type": "crt.FlexContainer", "items": [ { "name": "Timeline", "type": "crt.Timeline" } ] },
			  { "name": "TopField", "type": "crt.Input" },
			  { "name": "CardContentWrapper", "type": "crt.GridContainer", "items": [
			      { "name": "SideField", "type": "crt.Input" },
			      { "name": "Tabs", "type": "crt.TabPanel", "items": [
			          { "name": "OverviewTab", "type": "crt.TabContainer", "items": [ { "name": "LeadName", "type": "crt.Input" } ] } ] } ] },
			  { "name": "FooterField", "type": "crt.Input" }
			]
			""");
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["CardContentWrapper"] = "GeneralTabContainer", ["Tabs"] = "Tabs"
		};
		var placements = new List<WebToMobileAnalysisService.PositionalPlacement> { new("CardContentWrapper", "Tabs") };
		var mobileParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tabs"] = "MainContainer" };

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(
			bundle, containerNameMap: map, positionalPlacements: placements, mobileContainerParents: mobileParents);

		Element(guide, "TopEmpty").Operation.Should().Be("drop");
		Element(guide, "TopBox").Index.Should().Be(0, because: "the first survivor keeps the top slot");
		Element(guide, "TopField").Index.Should().Be(1,
			because: "the removed middle sibling must leave no index hole — a gap would misplace the insert");
		Element(guide, "FooterField").Index.Should().BeNull(because: ":bottom entries append and never carry an index");
	}

	[Test]
	[Description("ENG-91228 follow-up: positional :top compaction is NOT tied to the empty-container pass — a middle sibling dropped for an unrelated reason (unsupported type) leaves no index hole even with no emptyContainerRemoval rules section at all.")]
	public void Analyze_ShouldCompactPositionalIndexes_WhenSiblingDroppedForUnrelatedReason() {
		PageBundleInfo bundle = Bundle("""
			[
			  { "name": "TopBox", "type": "crt.FlexContainer", "items": [ { "name": "ProgressBar", "type": "crt.Input" } ] },
			  { "name": "Timeline", "type": "crt.Timeline" },
			  { "name": "TopField", "type": "crt.Input" },
			  { "name": "CardContentWrapper", "type": "crt.GridContainer", "items": [
			      { "name": "SideField", "type": "crt.Input" },
			      { "name": "Tabs", "type": "crt.TabPanel", "items": [
			          { "name": "OverviewTab", "type": "crt.TabContainer", "items": [ { "name": "LeadName", "type": "crt.Input" } ] } ] } ] }
			]
			""");
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["CardContentWrapper"] = "GeneralTabContainer", ["Tabs"] = "Tabs"
		};
		var placements = new List<WebToMobileAnalysisService.PositionalPlacement> { new("CardContentWrapper", "Tabs") };
		var mobileParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tabs"] = "MainContainer" };

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(
			bundle, rules: GridRule, containerNameMap: map, positionalPlacements: placements, mobileContainerParents: mobileParents);

		Element(guide, "Timeline").Operation.Should().Be("drop",
			because: "the unsupported middle sibling drops during the walk itself, before any empty-container logic");
		Element(guide, "TopBox").Index.Should().Be(0, because: "the first survivor keeps the top slot");
		Element(guide, "TopField").Index.Should().Be(1,
			because: "every drop source leaves the same positional hole, so compaction must run even when the empty-container pass removed nothing");
	}

	[Test]
	[Description("ENG-91228 follow-up: the requestConversions summary is reconciled with the removal pass — a converted binding on a container later removed as empty is reclassified into droppedRequests (naming the removal), never reported as converted for an element the map says not to create.")]
	public void Analyze_ShouldReclassifyConvertedRequest_WhenItsContainerWasRemovedAsEmpty() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "EmptyBox", "type": "crt.FlexContainer",
			    "clicked": { "request": "crt.SaveRecordRequest", "params": {} }, "items": [
				{ "name": "Timeline", "type": "crt.Timeline" } ] },
			  { "name": "SaveButton", "type": "crt.Input",
			    "clicked": { "request": "crt.SaveRecordRequest", "params": {} } } ]
			""");
		WebToMobilePageConversionRules rules = new() {
			Components = GridRule.Components,
			Requests = [
				new RequestMappingRule { Web = "crt.SaveRecordRequest", Mobile = "crt.SaveRecordRequest", Category = "DirectMapping" }
			],
			EmptyContainerRemoval = EmptyRemoval
		};

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle, rules: rules);

		Element(guide, "EmptyBox").Operation.Should().Be("drop",
			because: "the container's only child dropped, so the empty-container pass removes the container itself");
		guide.RequestConversions!.ConvertedRequests.Should().NotContain(r => r.ElementName == "EmptyBox",
			because: "reporting a binding as converted for a removed element would contradict the drop entry and invite the caller to re-create the container");
		guide.RequestConversions.DroppedRequests.Should().ContainSingle(r =>
				r.ElementName == "EmptyBox" && r.Binding == "clicked" && r.WebRequest == "crt.SaveRecordRequest",
				because: "the discarded binding must stay visible in the report instead of vanishing silently")
			.Which.Reason.Should().Contain("empty container",
				because: "the reason must name the removal so the reader can connect it to the elementMap drop entry");
		guide.RequestConversions.ConvertedRequests.Should().ContainSingle(r => r.ElementName == "SaveButton",
			because: "reconciliation is scoped to removed containers — a surviving element's converted binding still reports as converted");
	}

	[Test]
	[Description("ENG-91228 (decision 3): attributes referenced ONLY by a removed empty container are KEPT in viewModelConfig — the removal is layout cleanup, not attribute cleanup — while attributes of a genuinely dropped component are still cleaned as before.")]
	public void Analyze_ShouldKeepAttributesOfRemovedContainer_WhenOnlyEmptyContainerReferencedThem() {
		PageBundleInfo bundle = Bundle(
			viewConfigJson: """
			[ { "name": "EmptyBox", "type": "crt.GridContainer", "visible": "$BoxVisible", "items": [
				{ "name": "Timeline", "type": "crt.Timeline", "value": "$TimelineAttr" } ] } ]
			""",
			viewModelConfigJson: """
			{ "attributes": { "BoxVisible": { "type": "Boolean" }, "TimelineAttr": { "type": "String" } } }
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		Element(guide, "EmptyBox").Operation.Should().Be("drop");
		JsonObject attributes = guide.ViewModelConfig!["attributes"]!.AsObject();
		attributes.ContainsKey("BoxVisible").Should().BeTrue(
			because: "the empty-container removal deliberately keeps the attributes the removed container referenced");
		attributes.ContainsKey("TimelineAttr").Should().BeFalse(
			because: "an attribute consumed only by a genuinely dropped component is still cleaned, exactly as before");
	}

	[Test]
	[Description("ENG-91228 (decision 6): the removal runs BEFORE the business-rule conversion — a rule whose only action targets the removed container is dropped, while a rule on a surviving element still converts.")]
	public void Analyze_ShouldDropRuleOnRemovedContainer_WhenRemovalRunsBeforeRuleConversion() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "EmptyBox", "type": "crt.GridContainer", "items": [
				{ "name": "Timeline", "type": "crt.Timeline" } ] },
			  { "name": "LeadName", "type": "crt.Input" } ]
			""");
		PageBusinessRuleProbeResult probe = ProbeOf(
			SourceRule("Hide the box", ElementAction("hide-element", "EmptyBox")),
			SourceRule("Lock the name", ElementAction("make-read-only", "LeadName")));

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle, pageBusinessRulesProbe: probe);

		Element(guide, "EmptyBox").Operation.Should().Be("drop");
		guide.PageBusinessRules!.DroppedRules.Should().ContainSingle(r => r.Caption == "Hide the box",
			because: "a rule left with no live action follows its removed target out");
		guide.PageBusinessRules.ConvertedRules.Should().ContainSingle(r => r.Caption == "Lock the name",
			because: "rules on surviving elements are untouched by the removal");
	}

	#endregion

	#region Converted tab placement (explicit indexes so template Feed/Attachments stay last)

	private static readonly ConvertedTabPlacementRule TabPlacement = new() {
		TabsElementName = "Tabs", TabComponentType = "crt.TabContainer", FirstIndex = 1
	};

	private static WebToMobilePageConversionRules RulesWithTabPlacement() => new() {
		Components = GridRule.Components,
		EmptyContainerRemoval = EmptyRemoval,
		ConvertedTabPlacement = TabPlacement
	};

	[Test]
	[Description("Converted web tabs get explicit indexes under the mobile Tabs starting at firstIndex (right after the template's general tab), in web tree order — so applying the element map verbatim keeps the template's Feed/Attachments tabs last.")]
	public void Analyze_ShouldIndexConvertedTabs_AfterTemplateGeneralTab() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [ { "name": "Budget", "type": "crt.Input" } ] },
				{ "name": "HistoryTab", "type": "crt.TabContainer", "items": [ { "name": "Comment", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle, rules: RulesWithTabPlacement());

		ElementMapEntry sales = Element(guide, "SalesTab");
		sales.ParentName.Should().Be("Tabs");
		sales.Index.Should().Be(1, because: "position 0 belongs to the template's general tab");
		sales.Reason.Should().Contain("Feed/Attachments",
			because: "the report must explain why a non-positional insert suddenly carries an index");
		Element(guide, "HistoryTab").Index.Should().Be(2, because: "converted tabs keep the web page's own tab order");
		Element(guide, "Tabs").Operation.Should().Be("merge",
			because: "the Tabs twin itself is template chrome and is never indexed or moved");
		Element(guide, "Budget").Index.Should().BeNull(because: "only the tabs are indexed, never their content");
	}

	[Test]
	[Description("Leads_FormPage scenario: a tab removed as empty (its only child is unsupported on mobile) is never indexed, and the surviving tabs are numbered contiguously from firstIndex — no hole where the removed tab was.")]
	public void Analyze_ShouldIndexOnlySurvivingTabs_WhenMiddleTabWasRemovedAsEmpty() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [ { "name": "Budget", "type": "crt.Input" } ] },
				{ "name": "NextStepsTab", "type": "crt.TabContainer", "items": [ { "name": "Timeline", "type": "crt.Timeline" } ] },
				{ "name": "HistoryTab", "type": "crt.TabContainer", "items": [ { "name": "Comment", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle, rules: RulesWithTabPlacement());

		ElementMapEntry nextSteps = Element(guide, "NextStepsTab");
		nextSteps.Operation.Should().Be("drop",
			because: "its only child is unsupported on mobile, so the tab empties and the removal pass takes it");
		nextSteps.Index.Should().BeNull(because: "a drop is never indexed");
		Element(guide, "SalesTab").Index.Should().Be(1);
		Element(guide, "HistoryTab").Index.Should().Be(2,
			because: "the removed middle tab must leave no index hole — survivors stay contiguous");
	}

	[Test]
	[Description("The pass is switched by DATA — without a convertedTabPlacement rules section a converted tab carries no index and appends, exactly as before the feature.")]
	public void Analyze_ShouldLeaveTabsUnindexed_WhenRulesCarryNoPlacementSection() {
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Tabs", "type": "crt.TabPanel", "items": [
				{ "name": "SalesTab", "type": "crt.TabContainer", "items": [ { "name": "Budget", "type": "crt.Input" } ] } ] } ]
			""");

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(bundle);

		Element(guide, "SalesTab").Index.Should().BeNull(
			because: "with no placement section the converter behaves exactly as before the feature");
	}

	[Test]
	[Description("Tab indexes coexist with positional :top indexes: the positional group (under MainContainer) is compacted from 0, while the tab group (under Tabs) starts at firstIndex — the compaction never rebases the tab indexes because they are assigned after it.")]
	public void Analyze_ShouldKeepTabIndexBase_WhenPositionalCompactionRuns() {
		PageBundleInfo bundle = Bundle("""
			[
			  { "name": "TopBox", "type": "crt.FlexContainer", "items": [ { "name": "ProgressBar", "type": "crt.Input" } ] },
			  { "name": "Timeline", "type": "crt.Timeline" },
			  { "name": "TopField", "type": "crt.Input" },
			  { "name": "CardContentWrapper", "type": "crt.GridContainer", "items": [
			      { "name": "SideField", "type": "crt.Input" },
			      { "name": "Tabs", "type": "crt.TabPanel", "items": [
			          { "name": "SalesTab", "type": "crt.TabContainer", "items": [ { "name": "Budget", "type": "crt.Input" } ] } ] } ] }
			]
			""");
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["CardContentWrapper"] = "GeneralTabContainer", ["Tabs"] = "Tabs"
		};
		var placements = new List<WebToMobileAnalysisService.PositionalPlacement> { new("CardContentWrapper", "Tabs") };
		var mobileParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tabs"] = "MainContainer" };

		MobilePageConversionGuide guide = AnalyzeWithEmptyRemoval(
			bundle, rules: RulesWithTabPlacement(), containerNameMap: map,
			positionalPlacements: placements, mobileContainerParents: mobileParents);

		Element(guide, "TopBox").Index.Should().Be(0, because: ":top compaction still rebases the positional group to 0");
		Element(guide, "TopField").Index.Should().Be(1,
			because: "the dropped middle sibling leaves no positional hole, exactly as without tab placement");
		ElementMapEntry sales = Element(guide, "SalesTab");
		sales.ParentName.Should().Be("Tabs");
		sales.Index.Should().Be(1,
			because: "the tab index is assigned AFTER the compaction — rebased to 0 it would land before the template's general tab");
	}

	#endregion
}
