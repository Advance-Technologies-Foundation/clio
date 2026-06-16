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
			"crt.Input", "crt.Toggle", "crt.RichTextEditor", "crt.List", "crt.FolderTreeActions"
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
		IReadOnlyDictionary<string, string> containerNameMap = null) =>
		WebToMobileAnalysisService.Analyze(
			bundle, MobileTypes, WebTypes,
			webByType ?? new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType,
			Rules, templateRule,
			sourcePage: "UsrApp_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrApp_MobileFormPage", containerNameMap: containerNameMap);

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
	[Description("Web-only sections and multiple data sources are surfaced (not stripped) and reflected in constraints.")]
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
		guide.Constraints.Should().Contain(c => c.Contains("MULTIPLE data sources"));
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
		guide.Constraints.Should().Contain(c => c.Contains("SINGLE data source"));
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

	private static MobilePageConversionGuide AnalyzeTabbed(PageBundleInfo bundle) =>
		WebToMobileAnalysisService.Analyze(
			bundle, TabbedMobileTypes,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crt.DataGrid", "crt.IndicatorWidget", "crt.Timeline" },
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, GridRule, templateRule: null,
			sourcePage: "Leads_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrLeads_MobileFormPage", containerNameMap: TabbedContainerMap);

	[Test]
	[Description("Golden Leads_FormPage: Tabs merges; first tab relocates fields and drops widgets; a page-specific tab inserts with caption and drops its multi-DS grid; empty tabs drop; template twins merge.")]
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

		// First tab → relocate-children into AreaProfileContainer; its children carry that parentName.
		ElementMapEntry overview = Element(guide, "OverviewTab");
		overview.Operation.Should().Be("relocate-children");
		overview.ParentName.Should().Be("AreaProfileContainer");
		Element(guide, "LeadName").Operation.Should().Be("insert");
		Element(guide, "LeadName").ParentName.Should().Be("AreaProfileContainer");
		Element(guide, "Status").ParentName.Should().Be("AreaProfileContainer");
		// Unsupported / foreign-DS children of the relocated tab → drop.
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
}
