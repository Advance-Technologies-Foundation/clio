using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Resources.ProcessDesigner;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GuidanceGetToolTests {
	private IFeatureToggleService _featureToggleService;

	[SetUp]
	public void SetUp() {
		// A bare substitute returns false for every IsEnabled(...) call, which keeps feature-gated
		// guidance hidden while ungated guidance stays visible (GuidanceCatalog.IsVisible short-circuits
		// on a null gate type and never calls the toggle service). Tests that need a gated guide enable
		// it explicitly on this substitute.
		_featureToggleService = Substitute.For<IFeatureToggleService>();
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for get-guidance.")]
	public void GuidanceGet_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GuidanceGetTool)
			.GetMethod(nameof(GuidanceGetTool.GetGuidance))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(GuidanceGetTool.ToolName,
			because: "the MCP guidance tool name must stay stable for prompts that route guide lookups through it");
	}

	[Test]
	[Category("Unit")]
	[Description("Documents configuration web-service and page-schema guides as known guidance names on the get-guidance argument contract.")]
	public void GuidanceGet_Should_Document_Known_Guides_In_Argument_Descriptions() {
		// Arrange
		ParameterInfo argsParameter = typeof(GuidanceGetTool)
			.GetMethod(nameof(GuidanceGetTool.GetGuidance))!
			.GetParameters()
			.Single(parameter => parameter.ParameterType == typeof(GuidanceGetArgs));
		PropertyInfo nameProperty = typeof(GuidanceGetArgs).GetProperty(nameof(GuidanceGetArgs.Name))!;

		// Act
		System.ComponentModel.DescriptionAttribute parameterDescription = argsParameter
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();
		System.ComponentModel.DescriptionAttribute propertyDescription = nameProperty
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Assert
		parameterDescription.Description.Should().Contain("configuration-webservice",
			because: "the top-level argument hint should mention the configuration web-service implementation guidance name");
		parameterDescription.Description.Should().Contain("atf-repository-dev",
			because: "the top-level argument hint should mention generated composable-app skill guidance names");
		parameterDescription.Description.Should().Contain("feature-toggle-tests",
			because: "the top-level argument hint should mention generated composable-app test guidance names");
		parameterDescription.Description.Should().Contain("page-schema-handlers",
			because: "the top-level argument hint should mention the dedicated handler guidance name");
		parameterDescription.Description.Should().Contain("related-list",
			because: "the top-level argument hint should mention the dedicated related-list (detail) guidance name");
		parameterDescription.Description.Should().Contain("esq-filters",
			because: "the top-level argument hint should mention the dedicated ESQ filters guidance name");
		parameterDescription.Description.Should().Contain("page-modification",
			because: "the top-level argument hint should mention the general page modification guidance name");
		propertyDescription.Description.Should().Contain("configuration-webservice",
			because: "the serialized name field hint should mention the configuration web-service implementation guidance name");
		propertyDescription.Description.Should().Contain("atf-repository-dev",
			because: "the serialized name field hint should mention generated composable-app skill guidance names");
		propertyDescription.Description.Should().Contain("feature-toggle-tests",
			because: "the serialized name field hint should mention generated composable-app test guidance names");
		propertyDescription.Description.Should().Contain("page-schema-handlers",
			because: "the serialized name field hint should stay aligned with the known handler guidance name");
		propertyDescription.Description.Should().Contain("related-list",
			because: "the serialized name field hint should mention the dedicated related-list (detail) guidance name");
		propertyDescription.Description.Should().Contain("esq-filters",
			because: "the serialized name field hint should mention the dedicated ESQ filters guidance name");
		propertyDescription.Description.Should().Contain("page-modification",
			because: "the serialized name field hint should stay aligned with the known page modification guidance name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical ESQ filters guidance article when the caller requests esq-filters.")]
	public async Task GuidanceGet_Should_Return_Esq_Filters_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("esq-filters"));

		// Assert
		result.Success.Should().BeTrue(
			because: "esq-filters is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/esq-filters",
			because: "the guidance tool should preserve the canonical esq-filters guide URI in the response");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical theming guidance article when the caller requests theming: a single entry point that builds the theme CSS with the native build-theme tool and routes the agent to the right flow rather than embedding the token catalog.")]
	public async Task GuidanceGet_Should_Return_Theming_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("theming"));

		// Assert
		result.Success.Should().BeTrue(
			because: "theming is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/theming",
			because: "the guidance tool should preserve the canonical theming guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP custom-theme guide",
			because: "the guidance tool should return the canonical theming article text");
		result.Article.Text.Should().Contain("Which flow",
			because: "theming is the single entry point — the article must help the agent pick the workspace/dev vs no-code/server flow");
		result.Article.Text.Should().NotContain("@creatio/theming",
			because: "the npm package is retired — theme CSS is built by the native build-theme tool");
		result.Article.Text.Should().NotContain("AI_GUIDES_INDEX.md",
			because: "the npm package index is retired; the guide no longer routes through it");
		result.Article.Text.Should().Contain("push-workspace",
			because: "the workspace/dev flow must direct deployment through push-workspace");
		result.Article.Text.Should().Contain("No-code / server flow",
			because: "the no-code/server flow is now available and must be described, not marked unavailable");
		result.Article.Text.Should().NotContain("not yet available",
			because: "the server flow shipped, so the guide must no longer say it is unavailable");
		result.Article.Text.Should().Contain("create-theme",
			because: "the server flow must route the agent to the create-theme MCP tool");
		result.Article.Text.Should().Contain("update-theme",
			because: "the server flow must route the agent to the update-theme MCP tool");
		result.Article.Text.Should().Contain("delete-theme",
			because: "the server flow must route the agent to the delete-theme MCP tool");
		result.Article.Text.Should().NotContain("-by-environment",
			because: "the guide routes to theming tools by their canonical names only");
		result.Article.Text.Should().NotContain("-by-credentials",
			because: "the guide routes to theming tools by their canonical names only");
		result.Article.Text.Should().Contain("build-theme",
			because: "the guide must route theme-CSS building to the native build-theme tool rather than hand-computing colors");
		result.Article.Text.Should().Contain("Branding — logos and background",
			because: "the guide must carry the branding companion section for logos and the shell background (ENG-92981)");
		result.Article.Text.Should().Contain("CrtAppToolbarLogo",
			because: "the branding section must map the Freedom UI top-panel logo slot to its Binary sys setting");
		result.Article.Text.Should().Contain("value-file-path",
			because: "logo uploads must route through the Binary sys-setting file path, never inline bytes");
		result.Article.Text.Should().Contain("HideSplashScreenLogoImage",
			because: "applying logos must also hide the stock splash logo");
		result.Article.Text.Should().Contain("ImageAPIService/upload",
			because: "the background image upload must use the platform image API, since OData JSON cannot write the binary stream");
		result.Article.Text.Should().Contain("SysImageInTag",
			because: "the background must be registered in the Appearance gallery via the shell-background tag");
		result.Article.Text.Should().Contain("CrtBackgroundConfig",
			because: "the shell background is applied by pointing CrtBackgroundConfig at the uploaded image");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the theming guidance a thin pointer (CM-03): it names the --crt-* token namespace at most once without restating the token catalog.")]
	public async Task GuidanceGet_Should_Not_Restate_Token_Catalog_When_Topic_Is_Theming() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("theming"));

		// Assert
		result.Article.Should().NotBeNull(
			because: "theming is a registered guidance name that resolves to an article");
		int tokenNamespaceMentions = result.Article!.Text.Split("--crt").Length - 1;
		tokenNamespaceMentions.Should().BeLessThanOrEqualTo(1,
			because: "the guide may name the --crt-* token namespace once as a pointer, but must not restate the --crt-* token catalog (CM-03 — single source of truth)");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical indicator widget guidance article when the caller requests indicator-widget.")]
	public async Task GuidanceGet_Should_Return_Indicator_Widget_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("indicator-widget"));

		// Assert
		result.Success.Should().BeTrue(
			because: "indicator-widget is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/indicator-widget",
			because: "the guidance tool should preserve the canonical indicator-widget guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP indicator widget guide",
			because: "the guidance tool should return the canonical indicator widget article text");
		result.Article.Text.Should().Contain("get-component-info",
			because: "the trimmed indicator widget guide should point callers to get-component-info as the source of truth");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical chart widget guidance article when the caller requests chart-widget.")]
	public async Task GuidanceGet_Should_Return_Chart_Widget_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("chart-widget"));

		// Assert
		result.Success.Should().BeTrue(
			because: "chart-widget is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/chart-widget",
			because: "the guidance tool should preserve the canonical chart-widget guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP chart widget guide",
			because: "the guidance tool should return the canonical chart widget article text");
		result.Article.Text.Should().Contain("get-component-info",
			because: "the trimmed chart widget guide should point callers to get-component-info as the source of truth");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical related-list guidance article when the caller requests related-list.")]
	public async Task GuidanceGet_Should_Return_Related_List_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("related-list"));

		// Assert
		result.Success.Should().BeTrue(
			because: "related-list is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/related-list",
			because: "the guidance tool should preserve the canonical related-list guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP related list guide",
			because: "the guidance tool should return the canonical related-list article text");
		result.Article.Text.Should().Contain("modelConfig.dependencies",
			because: "the related-list guide must teach the declarative dependencies entry that scopes a list by the open record");
		result.Article.Text.Should().Contain("get-component-info",
			because: "the related-list guide should point callers to get-component-info as the source of truth for crt.DataGrid and crt.ExpansionPanel");
		result.Article.Text.Should().Contain("is not a container for other items",
			because: "the related-list guide must warn that an inserted container without an initialized items slot fails at runtime");
		result.Article.Text.Should().Contain("Use `modelConfig.dependencies` instead",
			because: "the related-list guide must warn against the init-handler scoping anti-pattern and redirect to declarative dependencies");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical related-page-binding guidance article when the caller requests related-page-binding.")]
	public async Task GuidanceGet_Should_Return_Related_Page_Binding_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("related-page-binding"));

		// Assert
		result.Success.Should().BeTrue(
			because: "related-page-binding is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/related-page-binding",
			because: "the guidance tool should preserve the canonical related-page-binding guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-to-object binding guide",
			because: "the guidance tool should return the canonical page-to-object binding article text");
		result.Article.Text.Should().Contain("create-related-page-addon",
			because: "the resolved article must document the create-related-page-addon write tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Pins the get-component-info-first / anti-bundle-reverse-engineering sentence in the page-modification guidance so it cannot be silently reverted (ENG-91953 recurrence guard). The ENG-91556 split relocated the canonical flow (which carries this sentence) into the page-modification-overview sub-guide.")]
	public async Task GuidanceGet_Should_Pin_AntiBundleReverseEngineering_ForPageModification() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act — the canonical flow moved to the overview sub-guide in the ENG-91556 split
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-modification-overview"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-modification-overview is a registered guidance name that now owns the canonical flow");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Text.Should().Contain("reverse-engineering one is NOT a substitute",
			because: "the anti-bundle-reverse-engineering guidance is a core ENG-91953 deliverable and must be guarded against silent reverts");
		result.Article.Text.Should().Contain("compiled bundle",
			because: "the guidance must keep warning that a compiled page bundle hides the catalog-only signals get-component-info carries");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical handler guidance article when the caller requests page-schema-handlers.")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Handlers_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-schema-handlers"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-handlers is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-handlers",
			because: "the guidance tool should preserve the canonical handler guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema handlers guide",
			because: "the guidance tool should return the canonical handler article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical SDK common guidance article when the caller requests page-schema-creatio-devkit-common.")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Sdk_Common_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-schema-creatio-devkit-common"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-creatio-devkit-common is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-creatio-devkit-common",
			because: "the guidance tool should preserve the canonical SDK common guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema sdk common guide",
			because: "the guidance tool should return the canonical SDK common article text");
		result.Article.Text.Should().Contain("Pattern selection order for handler-side data/service work is mandatory",
			because: "the guidance tool should return the updated SDK common routing rules for request sdk and fetch selection");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical page modification guidance article when the caller requests page-modification.")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-modification"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-modification is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-modification",
			because: "the guidance tool should preserve the canonical page modification guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page modification guide",
			because: "the guidance tool should return the canonical page modification article text");
		result.Article.Text.Should().Contain("page-schema-resources",
			because: "the page modification guide should route localizable string edits to the resources guide");
		result.Article.Text.Should().Contain("COMPONENT-TYPE VERIFICATION IS MANDATORY",
			because: "the page modification guide must force component-type verification before any viewConfigDiff insert to prevent invented crt.* types");
		result.Article.Text.Should().Contain("ASK THE USER",
			because: "the page modification guide must tell the agent to ask the user (pick an existing component or build a custom one) when no OOTB component matches");
		result.Article.Text.Should().Contain("WEB, MOBILE, OR BOTH",
			because: "Step 0 must make the agent resolve web vs mobile before editing a page");
		result.Article.Text.Should().Contain("default to web",
			because: "Step 0 must default to web when the requirement does not name a surface");
		result.Article.Text.Should().Contain("page-modification-field-contract",
			because: "the entry guide must route a data-bound field insert to the focused field-contract sub-guide (ENG-91556 split)");
		result.Article.Text.Should().Contain("page-modification-overview",
			because: "the entry guide must point at the save-lifecycle sub-guide so the detailed mechanics stay one get-guidance call away (ENG-91556 split)");
		result.Article.Text.Should().Contain("showing a user-facing message/confirmation/info/success/error popup",
			because: "the gate table must route a 'show a confirmation message' requirement into page-schema-handlers so the agent uses crt.ShowDialogRequest (ENG-91748)");
		result.Article.Text.Should().Contain("NEVER use `alert(...)`, `window.alert(...)`, `confirm(...)`, or `prompt(...)`",
			because: "the page modification guide must forbid raw browser dialog primitives in page-body handlers so the agent stops emitting alert() (ENG-91748)");
		string entryCrlfWorstCase = result.Article.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
		new System.Text.UTF8Encoding(false).GetByteCount(entryCrlfWorstCase).Should().BeLessThanOrEqualTo(15000,
			because: "the entry guide must stay <= 15 KB (CRLF worst case) so a single get-guidance response fits the agent token limit (ENG-91556 AC#2)");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the page-modification-overview sub-guide carrying the relocated body save-lifecycle content (ENG-91556 split).")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Overview_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-modification-overview"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-modification-overview is a registered guidance name after the split");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-overview",
			because: "the overview sub-guide must expose its stable canonical URI");
		result.Article.Text.Should().Contain("clio MCP page modification overview guide",
			because: "the overview sub-guide must carry its own header");
		result.Article.Text.Should().Contain("Replacing-schema concept",
			because: "the replacing-schema concept moved from the entry guide into the overview sub-guide");
		result.Article.Text.Should().Contain("do NOT resend the full raw.body",
			because: "the do-not-resend rule moved into the overview sub-guide");
		result.Article.Text.Should().Contain("External-modification conflicts",
			because: "the checksum-conflict recovery content moved into the overview sub-guide");
		result.Article.Text.Should().Contain("mode: \"append\"",
			because: "the update-page write-mode rules (replace/append) moved into the overview sub-guide and must not be silently dropped");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the page-modification-field-contract sub-guide carrying the relocated inserted-field contract content plus the shared InsertedFieldContractSummary (ENG-91556 split).")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Field_Contract_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-modification-field-contract"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-modification-field-contract is a registered guidance name after the split");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-field-contract",
			because: "the field-contract sub-guide must expose its stable canonical URI");
		result.Article.Text.Should().Contain("clio MCP page modification field-contract guide",
			because: "the field-contract sub-guide must carry its own header");
		result.Article.Text.Should().Contain("Inserted-field contract for a new data-bound field control",
			because: "the inserted-field contract section moved into the field-contract sub-guide");
		result.Article.Text.Should().Contain(Clio.Command.SchemaValidationService.InsertedFieldContractSummary,
			because: "the field-contract sub-guide must still inject the shared inserted-field contract summary verbatim so the guidance matches the validator");
		result.Article.Text.Should().Contain("the attribute must reach `viewModelConfig.attributes`",
			because: "the viewModelConfigDiff nesting rule moved into the field-contract sub-guide");
		result.Article.Text.Should().Contain("Static vs diff body forms",
			because: "the static-vs-diff body-form decision moved into the field-contract sub-guide and must not be silently dropped");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the page-modification-containers sub-guide carrying the relocated bundle.json and parentName content (ENG-91556 split).")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Containers_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-modification-containers"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-modification-containers is a registered guidance name after the split");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-containers",
			because: "the containers sub-guide must expose its stable canonical URI");
		result.Article.Text.Should().Contain("clio MCP page modification containers guide",
			because: "the containers sub-guide must carry its own header");
		result.Article.Text.Should().Contain("Finding a container for a new component",
			because: "the container-selection section moved into the containers sub-guide");
		result.Article.Text.Should().Contain("jq recipes for bundle.json",
			because: "the bundle.json jq recipes moved into the containers sub-guide");
		result.Article.Text.Should().Contain("ARRAY of objects",
			because: "the bundle.json top-level shape (containers is an ARRAY) moved into the containers sub-guide and must not be silently dropped");
		result.Article.Text.Should().Contain("Inserting a NEW container",
			because: "the new-container slot-init rule (ENG-91555) lives in the containers sub-guide after the ENG-91556 split");
		result.Article.Text.Should().Contain("its content slot MUST be initialized",
			because: "the new-container rule must center on initializing the content slot, the verified root cause aligned with related-list and get-component-info (ENG-91555, PR #789 review)");
		result.Article.Text.Should().Contain("is not a container for other items",
			because: "the guide must name the exact runtime error a slot-less container raises so the agent recognizes it (ENG-91555)");
		result.Article.Text.Should().Contain("only a NEWLY-inserted container needs this",
			because: "the guide must scope the slot-init rule to newly-inserted containers so existing-container inserts are not mistakenly rewritten (ENG-91555, PR #789 review)");
		result.Article.Text.Should().Contain("dry-run validates JSON/schema shape ONLY and will NOT catch this",
			because: "the guide must state the dry-run limitation so the agent does not treat a passing validation as proof the page works (ENG-91555)");
		result.Article.Text.Should().Contain("inline children are config-node objects, NOT diff operations",
			because: "the example must clarify that inline children carry no operation/parentName key so an agent does not turn them into malformed diff operations (PR #789 review)");
		result.Article.Text.Should().Contain("initialize the slot empty",
			because: "the rule must not contradict the related-list composite: separate parentName child inserts are valid when the container initializes its slot (PR #789 review)");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the page-modification-components sub-guide carrying the relocated viewConfigDiff/handler/get-component-info content including the whenToUse selection metadata (ENG-91556 split).")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Components_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-modification-components"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-modification-components is a registered guidance name after the split");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-components",
			because: "the components sub-guide must expose its stable canonical URI");
		result.Article.Text.Should().Contain("clio MCP page modification components guide",
			because: "the components sub-guide must carry its own header");
		result.Article.Text.Should().Contain("Adding a button with a click handler",
			because: "the button+handler section moved into the components sub-guide");
		result.Article.Text.Should().Contain("whenToUse",
			because: "the get-component-info selection-metadata guidance (whenToUse/whenNotToUse) moved into the components sub-guide (ENG-91134 / Solution A)");
		result.Article.Text.Should().Contain("latest-fallback",
			because: "the get-component-info resolvedFrom interpretation (incl. latest-fallback) moved into the components sub-guide and must not be silently dropped");
	}

	[Test]
	[Category("Unit")]
	[Description("Every guide in the page-modification family stays within the 15 KB per-response budget so no single get-guidance call exceeds the agent token limit (ENG-91556 AC#2). Measured against the CRLF-normalized worst case so the guard holds regardless of the checkout's line endings.")]
	public async Task GuidanceGet_Should_KeepEveryPageModificationGuideWithin15Kb_AfterSplit() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);
		string[] familyNames = [
			"page-modification", "page-modification-overview", "page-modification-field-contract",
			"page-modification-containers", "page-modification-components"
		];
		System.Text.UTF8Encoding utf8 = new(false);

		// Act / Assert
		foreach (string name in familyNames) {
			GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs(name));
			result.Success.Should().BeTrue(
				because: $"{name} must resolve in the catalog after the page-modification split");
			// Normalize every line ending to CRLF so the budget reflects the largest the article can be
			// served at (a CRLF checkout adds one byte per line over an LF checkout); this keeps the guard
			// independent of git autocrlf and matches the real runtime size observed on Windows.
			string crlfWorstCase = result.Article!.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
			utf8.GetByteCount(crlfWorstCase).Should().BeLessThanOrEqualTo(15000,
				because: $"the {name} guide must stay <= 15 KB (CRLF worst case) so a single get-guidance response fits the agent token limit (ENG-91556 AC#2)");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical page-creation guidance article when the caller requests page-creation.")]
	public async Task GuidanceGet_Should_Return_Page_Creation_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-creation"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-creation is a registered guidance name that the dashboard-creation guide routes to by name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-creation",
			because: "the guidance tool should preserve the canonical page-creation guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-creation guide",
			because: "the guidance tool should return the canonical page-creation article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the dashboard-creation guide through get-guidance to its canonical URI.")]
	public async Task GuidanceGet_Should_Return_Dashboard_Creation_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("dashboard-creation"));

		// Assert
		result.Success.Should().BeTrue(
			because: "dashboard-creation is a registered guidance name the create-page tool and the dashboards router route to");
		result.Article.Should().NotBeNull(
			because: "a successful guidance lookup must return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/dashboard-creation",
			because: "the tool must resolve the dashboard-creation name to its canonical guide URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the dashboard-design guide through get-guidance to its canonical URI.")]
	public async Task GuidanceGet_Should_Return_Dashboard_Design_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("dashboard-design"));

		// Assert
		result.Success.Should().BeTrue(
			because: "dashboard-design is a registered guidance name the dashboards router routes to");
		result.Article.Should().NotBeNull(
			because: "a successful guidance lookup must return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/dashboard-design",
			because: "the tool must resolve the dashboard-design name to its canonical guide URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical page localizable string guidance article when the caller requests page-schema-resources.")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Resources_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-schema-resources"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-resources is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-resources",
			because: "the guidance tool should preserve the canonical resources guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema resources guide",
			because: "the guidance tool should return the canonical resources article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical validator guidance article when the caller requests page-schema-validators.")]
	public void GuidanceGet_Should_Return_Page_Schema_Validators_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("page-schema-validators")).Result;

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-validators is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "the guidance tool should preserve the canonical resource URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema validators guide",
			because: "the guidance tool should return the canonical validator article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves guidance names case-insensitively so prompt-generated uppercase names still work.")]
	public void GuidanceGet_Should_Resolve_Guidance_Name_Case_Insensitively() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("PAGE-SCHEMA-VALIDATORS")).Result;

		// Assert
		result.Success.Should().BeTrue(
			because: "the guidance catalog stores names with an ordinal-ignore-case comparer");
		result.Article.Should().NotBeNull(
			because: "a case-insensitive match should still return the canonical guidance article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "case-insensitive lookup should still resolve to the canonical validator guide URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured error with availableGuides when args omit the required name parameter")]
	public void GuidanceGet_Should_Return_Structured_Error_On_Missing_Name() {
		GuidanceGetTool tool = new(_featureToggleService);

		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs(null)).Result;

		result.Success.Should().BeFalse();
		result.Error.Should().Contain("Missing required parameter 'name'",
			because: "calling without name should surface a clear structured error instead of throwing");
		result.AvailableGuides.Should().NotBeNullOrEmpty(
			because: "missing-name errors should still return the list of valid guides to unblock the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("Legacy alias 'topic' is accepted as 'name' with a hint when ExtensionData carries the value")]
	public void GuidanceGet_Should_Accept_Legacy_Alias_Topic() {
		GuidanceGetTool tool = new(_featureToggleService);
		var element = System.Text.Json.JsonDocument.Parse("\"page-schema-validators\"").RootElement;
		GuidanceGetArgs args = new(null) {
			ExtensionData = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> {
				["topic"] = element
			}
		};

		GuidanceGetResponse result = tool.GetGuidance(args).Result;

		result.Success.Should().BeTrue(
			because: "legacy 'topic' alias should resolve to 'name' so the caller's first attempt succeeds");
		result.Article!.Name.Should().Be("page-schema-validators");
		result.Hint.Should().Contain("rename to 'name'",
			because: "the hint should teach the caller the canonical field name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an explicit error and the known guide names when the requested guidance name is unknown.")]
	public void GuidanceGet_Should_Return_Known_Guide_Names_For_Unknown_Request() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("not-a-guide")).Result;

		// Assert
		result.Success.Should().BeFalse(
			because: "unknown guidance names should not resolve silently");
		result.Error.Should().Contain("Unknown guidance 'not-a-guide'",
			because: "the failure should name the rejected guide explicitly");
		result.AvailableGuides.Should().Contain([
				"agent-execution",
				"app-modeling",
				"atf-repository-dev",
				"atf-repository-model-management",
				"atf-repository-tests",
				"composable-app-e2e-test-implementation",
				"composable-app-repo-bootstrap",
				"configuration-webservice",
				"configuration-webservice-tests",
				"configuration-entity-event-listener",
				"configuration-entity-event-listener-tests",
				"creatio-composable-app-development",
				"creatio-freedom-iframe-section",
				"data-bindings",
				"existing-app-maintenance",
				"indicator-widget",
				"esq-filters",
				"feature-toggle",
				"feature-toggle-tests",
				"page-schema-converters",
				"page-schema-handlers",
				"page-schema-creatio-devkit-common",
				"page-schema-validators",
				"related-list",
				"related-page-binding",
				"sys-setting",
				"sys-setting-tests",
				"sys-settings",
				"support-mode"
			],
			because: "the failure response should help the caller recover with one of the registered guidance names");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical configuration web-service guidance article when the caller requests configuration-webservice.")]
	public async Task GuidanceGet_Should_Return_Configuration_WebService_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("configuration-webservice"));

		// Assert
		result.Success.Should().BeTrue(
			because: "configuration-webservice is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/configuration-webservice",
			because: "the guidance tool should preserve the canonical configuration web-service guide URI in the response");
		result.Article.Text.Should().Contain("creatio-config-webservice",
			because: "the guidance tool should return the canonical configuration web-service article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical configuration web-service test guidance article when the caller requests configuration-webservice-tests.")]
	public async Task GuidanceGet_Should_Return_Configuration_WebService_Tests_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("configuration-webservice-tests"));

		// Assert
		result.Success.Should().BeTrue(
			because: "configuration-webservice-tests is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/configuration-webservice-tests",
			because: "the guidance tool should preserve the canonical configuration web-service test guide URI in the response");
		result.Article.Text.Should().Contain("configuration-webservice-tests",
			because: "the guidance tool should return the canonical configuration web-service test article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns generated composable-app skill guidance articles by their skill names.")]
	public async Task GuidanceGet_Should_Return_Generated_Composable_App_Skill_Articles() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse atfResult = await tool.GetGuidance(new GuidanceGetArgs("atf-repository-dev"));
		GuidanceGetResponse sysSettingTestsResult = await tool.GetGuidance(new GuidanceGetArgs("sys-setting-tests"));

		// Assert
		atfResult.Success.Should().BeTrue(
			because: "atf-repository-dev is a generated guidance name");
		atfResult.Article.Should().NotBeNull(
			because: "successful generated guidance lookups should return an article");
		atfResult.Article!.Uri.Should().Be("docs://mcp/guides/atf-repository-dev",
			because: "generated guidance lookup should preserve the stable guide URI");
		atfResult.Article.Text.Should().Contain("ATF.Repository",
			because: "the generated guidance article should preserve the source skill content");

		sysSettingTestsResult.Success.Should().BeTrue(
			because: "sys-setting-tests is a generated guidance name");
		sysSettingTestsResult.Article.Should().NotBeNull(
			because: "successful generated test guidance lookups should return an article");
		sysSettingTestsResult.Article!.Uri.Should().Be("docs://mcp/guides/sys-setting-tests",
			because: "generated test guidance lookup should preserve the stable guide URI");
		sysSettingTestsResult.Article.Text.Should().Contain("SetupSysSettings",
			because: "the generated test guidance article should preserve the source skill content");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical converter guidance article when the caller requests page-schema-converters.")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Converters_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-schema-converters"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-converters is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-converters",
			because: "the guidance tool should preserve the canonical converter guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema converters guide",
			because: "the guidance tool should return the canonical converter article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical agent-execution guidance article when the caller requests agent-execution.")]
	public async Task GuidanceGet_Should_Return_Agent_Execution_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("agent-execution"));

		// Assert
		result.Success.Should().BeTrue(
			because: "agent-execution is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/agent-execution",
			because: "the guidance tool should preserve the canonical agent-execution guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP agent execution guide",
			because: "the guidance tool should return the canonical agent-execution article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical support-mode guidance article when the caller requests support-mode.")]
	public async Task GuidanceGet_Should_Return_Support_Mode_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("support-mode"));

		// Assert
		result.Success.Should().BeTrue(
			because: "support-mode is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/support-mode",
			because: "the guidance tool should preserve the canonical support-mode guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP support-mode guide",
			because: "the guidance tool should return the canonical support-mode article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical sys-settings guidance article when the caller requests sys-settings, with the documented core contract, value-type rules, SecureText masking, and Lookup resolution sections.")]
	public async Task GuidanceGet_Should_Return_SysSettings_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("sys-settings"));

		// Assert
		result.Success.Should().BeTrue(
			because: "sys-settings is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/sys-settings",
			because: "the guidance tool should preserve the canonical sys-settings guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP sys-settings guide",
			because: "the guidance tool should return the canonical sys-settings article header");
		result.Article.Text.Should().Contain("SecureText masking semantics",
			because: "the guide must spell out that read-back values are masked so callers don't misdiagnose 'did my write succeed'");
		result.Article.Text.Should().Contain("Lookup resolution rules",
			because: "the guide must explain how Lookup display names resolve to GUIDs server-side");
		result.Article.Text.Should().Contain("Binary",
			because: "the guide must explain Binary's exclusion from the advertised surface");
		result.Article.Text.Should().Contain("named JSON arguments",
			because: "the guide must explicitly tell agents that the MCP tools take named JSON args, not positional CLI args, so copy/paste of placeholder syntax does not break the call");
		result.Article.Text.Should().Contain("ambiguous matches",
			because: "the guide must document that multi-row display-name resolution is rejected and recommend GUID disambiguation");
		result.Article.Text.Should().Contain("escapes the value safely",
			because: "the guide must promise a behavioral escape guarantee instead of leaking the System.Text.Json implementation detail to agents");
		result.Article.Text.Should().Contain("shell-execution tool",
			because: "the CLI anti-pattern must be tool-agnostic (Bash/run_in_terminal/execution_subagent) rather than naming one specific shell tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical deploy-lifecycle guidance article when the caller requests deploy-lifecycle.")]
	public async Task GuidanceGet_Should_Return_Deploy_Lifecycle_Article() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("deploy-lifecycle"));

		// Assert
		result.Success.Should().BeTrue(
			because: "deploy-lifecycle is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/deploy-lifecycle",
			because: "the guidance tool should preserve the canonical deploy-lifecycle guide URI in the response");
		result.Article.Text.Should().Contain("assert-infrastructure",
			because: "the deploy lifecycle guide must spell out the infrastructure preflight as the first step");
		result.Article.Text.Should().Contain("deploy-creatio",
			because: "the deploy lifecycle guide must culminate in the deployment call");
		result.Article.Text.Should().Contain("install-gate",
			because: "the deploy lifecycle guide must cover cliogate installation as a post-deploy readiness step");
	}

	[Test]
	[Category("Unit")]
	[Description("Hides process-modeling from availableGuides when the process-designer feature gate is disabled, while the ungated run-process-button guide stays listed.")]
	public void GuidanceGet_Should_Hide_ProcessModeling_From_AvailableGuides_When_Gate_Disabled() {
		// Arrange
		// _featureToggleService is a bare substitute: IsEnabled(...) returns false, so the gated guides are disabled.
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("not-a-guide")).Result;

		// Assert
		result.Success.Should().BeFalse(
			because: "an unknown guidance name must not resolve");
		result.AvailableGuides.Should().NotContain("process-modeling",
			because: "process-modeling is gated behind a disabled process-designer flag and must not leak through get-guidance");
		result.AvailableGuides.Should().Contain("run-process-button",
			because: "run-process-button documents the shipped run-process scenario (get-process-signature + update-page) and is deliberately ungated, like the gps tool itself");
		result.AvailableGuides.Should().Contain("app-modeling",
			because: "ungated guidance entries must stay listed regardless of feature-toggle state");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats process-modeling as an unknown guide when the process-designer feature gate is disabled.")]
	public void GuidanceGet_Should_Reject_ProcessModeling_As_Unknown_When_Gate_Disabled() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("process-modeling")).Result;

		// Assert
		result.Success.Should().BeFalse(
			because: "a gated guide must resolve as unknown while its feature flag is off");
		result.Article.Should().BeNull(
			because: "a disabled gated guide must not return its article");
		result.Error.Should().Contain("Unknown guidance 'process-modeling'",
			because: "the failure must name the rejected guide so the disabled gate is indistinguishable from a non-existent guide");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves run-process-button while the process-designer feature gate is disabled: the guide documents the shipped run-process scenario and is not gated.")]
	public void GuidanceGet_Should_Resolve_RunProcessButton_When_Gate_Disabled() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("run-process-button")).Result;

		// Assert
		result.Success.Should().BeTrue(
			because: "run-process-button is ungated — its consumers (update-page, page-modification, page-schema-handlers, mobile-page guides) are public and must never hit a dead pointer");
		result.Article.Should().NotBeNull(
			because: "the ungated guide must return its article regardless of the process-designer flag");
		result.Article!.Uri.Should().Be("docs://mcp/guides/run-process-button",
			because: "the canonical run-process-button article URI must be stable");
	}

	[Test]
	[Category("Unit")]
	[Description("Lists and resolves process-modeling when the process-designer feature gate is enabled; the ungated run-process-button resolves alongside it.")]
	public async Task GuidanceGet_Should_List_And_Resolve_ProcessDesigner_Guides_When_Gate_Enabled() {
		// Arrange
		_featureToggleService.IsEnabled(typeof(ProcessModelingGuidanceResource)).Returns(true);
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse listing = tool.GetGuidance(new GuidanceGetArgs("not-a-guide")).Result;
		GuidanceGetResponse processModeling = await tool.GetGuidance(new GuidanceGetArgs("process-modeling"));
		GuidanceGetResponse runProcessButton = await tool.GetGuidance(new GuidanceGetArgs("run-process-button"));

		// Assert
		listing.AvailableGuides.Should().Contain("process-modeling",
			because: "process-modeling must be listed when the process-designer gate is enabled");
		listing.AvailableGuides.Should().Contain("run-process-button",
			because: "run-process-button is ungated and must always be listed");
		processModeling.Success.Should().BeTrue(
			because: "process-modeling must resolve when the process-designer gate is enabled");
		processModeling.Article!.Uri.Should().Be("docs://mcp/guides/process-modeling",
			because: "the enabled process-modeling guide must return its canonical article URI");
		runProcessButton.Success.Should().BeTrue(
			because: "run-process-button is ungated and must resolve regardless of the gate");
		runProcessButton.Article!.Uri.Should().Be("docs://mcp/guides/run-process-button",
			because: "the enabled run-process-button guide must return its canonical article URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a non-gated guide regardless of feature-toggle state, while gated guides are disabled.")]
	public async Task GuidanceGet_Should_Resolve_NonGated_Guide_When_ProcessDesigner_Gate_Disabled() {
		// Arrange
		GuidanceGetTool tool = new(_featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("app-modeling"));

		// Assert
		result.Success.Should().BeTrue(
			because: "app-modeling carries no feature gate and must resolve even while process-designer is off");
		result.Article!.Uri.Should().Be("docs://mcp/guides/app-modeling",
			because: "the ungated guide must return its canonical article URI");
	}
}
