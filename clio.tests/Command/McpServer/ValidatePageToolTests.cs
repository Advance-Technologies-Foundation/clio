using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ValidatePageToolTests {
	private const string ValidWebBody =
		"define(\"TestPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static PageValidateTool CreateTool(IFileSystem? fileSystem = null) => new(
		Substitute.For<IMobileComponentInfoCatalog>(),
		Substitute.For<IComponentInfoCatalog>(),
		fileSystem ?? new MockFileSystem());

	[Test]
	[Description("Advertises the stable MCP tool name for validate-page.")]
	public void PageValidateTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = PageValidateTool.ToolName;

		// Assert
		toolName.Should().Be("validate-page",
			because: "the MCP tool name must stay centralized on the production type");
	}

	[Test]
	[Description("Reads and validates the page body from body-file when no inline body is supplied.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldValidateFileContent_WhenBodyFileIsProvided() {
		// Arrange
		const string bodyFile = "C:\\workspace\\.clio-pages\\ContactPageV2\\body.js";
		var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> {
			[bodyFile] = new(ValidWebBody)
		});
		PageValidateTool tool = CreateTool(fileSystem);
		PageValidateArgs args = new(BodyFile: bodyFile);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeTrue(
			because: "validate-page must consume the exact body file returned by get-page");
		response.Validation.Errors.Should().BeNullOrEmpty(
			because: "a well-formed body loaded from disk should validate the same as the inline body");
	}

	[Test]
	[Description("Prefers the inline body when both body and body-file are supplied, preserving the existing inline contract.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldPreferInlineBody_WhenBothBodySourcesAreProvided() {
		// Arrange
		PageValidateTool tool = CreateTool(new MockFileSystem());
		PageValidateArgs args = new(Body: ValidWebBody, BodyFile: "C:\\missing\\body.js");

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeTrue(
			because: "body-file is a fallback and must not override a populated inline body");
	}

	[Test]
	[Description("Rejects a validate-page request that supplies neither inline body nor body-file with honest validation flags.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldRejectRequest_WhenNoBodySourceIsProvided() {
		// Arrange
		PageValidateTool tool = CreateTool();
		PageValidateArgs args = new();

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "validation cannot run without page body content");
		response.Validation.MarkersOk.Should().BeFalse(
			because: "no body exists in which markers could be verified");
		response.Validation.JsSyntaxOk.Should().BeFalse(
			because: "missing JavaScript must not be reported as syntactically valid");
		response.Validation.ContentOk.Should().BeFalse(
			because: "missing content must not be reported as valid");
		response.Validation.Errors.Should().ContainSingle(error => error.Contains("body-file"),
			because: "the error must identify both supported body sources");
	}

	[Test]
	[Description("Reports a stable missing-file classification instead of treating the request as an empty JavaScript body.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldReportPath_WhenBodyFileDoesNotExist() {
		// Arrange
		const string bodyFile = "C:\\workspace\\missing-body.js";
		PageValidateTool tool = CreateTool(new MockFileSystem());
		PageValidateArgs args = new(BodyFile: bodyFile);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "a missing file cannot supply validation content");
		response.Validation.JsSyntaxOk.Should().BeFalse(
			because: "a file read failure is not a successful syntax check");
		response.Validation.Errors.Should().ContainSingle(error => error.Contains("not found"),
			because: "the caller needs a stable missing-file classification");
		response.Validation.Errors.Should().NotContain(error => error.Contains(bodyFile),
			because: "structured MCP failures must not disclose host filesystem paths");
	}

	[Test]
	[Description("Rejects a body-file containing only whitespace with an actionable file-specific diagnostic.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldRejectFile_WhenBodyFileIsEmpty() {
		// Arrange
		const string bodyFile = "C:\\workspace\\empty-body.js";
		var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> {
			[bodyFile] = new("   ")
		});
		PageValidateTool tool = CreateTool(fileSystem);
		PageValidateArgs args = new(BodyFile: bodyFile);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "whitespace-only file content cannot be validated as a page body");
		response.Validation.Errors.Should().ContainSingle(error => error.Contains("empty"),
			because: "the diagnostic must distinguish an empty file from a missing one");
	}

	[Test]
	[Description("Converts body-file access failures into a structured validation response instead of an MCP invocation error.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldReturnStructuredFailure_WhenBodyFileCannotBeRead() {
		// Arrange
		const string bodyFile = "C:\\workspace\\protected-body.js";
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.File.Open(bodyFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read)
			.Returns(_ => throw new System.UnauthorizedAccessException("access denied at C:\\secret"));
		PageValidateTool tool = CreateTool(fileSystem);
		PageValidateArgs args = new(BodyFile: bodyFile);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "an unreadable file cannot supply validation content");
		response.Validation.Errors.Should().ContainSingle(error => error == "body-file could not be read.",
			because: "filesystem errors must remain actionable without leaking paths or provider exception text");
	}

	[Test]
	[Description("Rejects network body-file paths before touching the filesystem.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldRejectBodyFile_WhenPathIsNetworkLocation() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		PageValidateTool tool = CreateTool(fileSystem);
		PageValidateArgs args = new(BodyFile: @"\\server\share\body.js");

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "an OpenWorld=false tool must not initiate network filesystem access");
		response.Validation.Errors.Should().ContainSingle(error => error.Contains("local path"),
			because: "the caller needs a stable explanation of the rejected path class");
		fileSystem.File.DidNotReceiveWithAnyArgs().Open(default!, default, default, default);
	}

	[Test]
	[Description("Rejects an oversized body-file before allocating or parsing its contents.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldRejectBodyFile_WhenFileExceedsLimit() {
		// Arrange
		const string bodyFile = "C:\\workspace\\oversized-body.js";
		var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> {
			[bodyFile] = new(new byte[PageValidateTool.MaxBodyFileBytes + 1])
		});
		PageValidateTool tool = CreateTool(fileSystem);
		PageValidateArgs args = new(BodyFile: bodyFile);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "file input must not bypass the bounded MCP payload policy");
		response.Validation.Errors.Should().ContainSingle(error => error.Contains("byte limit"),
			because: "the caller needs an actionable size-limit diagnostic");
	}

	[Test]
	[Description("Honors cancellation while reading a body-file before validation begins.")]
	public async System.Threading.Tasks.Task ValidatePage_ShouldCancelBodyFileRead_WhenRequestIsCancelled() {
		// Arrange
		const string bodyFile = "C:\\workspace\\body.js";
		var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> {
			[bodyFile] = new(ValidWebBody)
		});
		PageValidateTool tool = CreateTool(fileSystem);
		PageValidateArgs args = new(BodyFile: bodyFile);
		using var cancellation = new System.Threading.CancellationTokenSource();
		cancellation.Cancel();

		// Act
		System.Func<System.Threading.Tasks.Task> act = () =>
			tool.ValidatePage(args, cancellation.Token);

		// Assert
		await act.Should().ThrowAsync<System.OperationCanceledException>(
			because: "a cancelled MCP request must not continue reading and validating a file payload");
	}

	[Test]
	[Description("Returns valid for a well-formed mobile JSON body (plain JSON starting with '{') without AMD markers.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenBodyIsValidMobileJson_ReturnsValid() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string mobileBody = """
			{
			  "viewConfigDiff": [],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";
		PageValidateArgs args = new(mobileBody, null);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeTrue(
			because: "a well-formed mobile JSON body with no disallowed keys should pass validation");
		response.Validation.MarkersOk.Should().BeTrue(
			because: "mobile pages have no AMD markers — MarkersOk should default to true");
		response.Validation.JsSyntaxOk.Should().BeTrue(
			because: "mobile pages have no JS syntax — JsSyntaxOk should default to true");
		response.Validation.ContentOk.Should().BeTrue(
			because: "a mobile body with no disallowed sections should pass content validation");
	}

	[Test]
	[Description("Returns invalid for a mobile JSON body that contains a 'validators' section.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenMobileBodyContainsValidators_ReturnsInvalid() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string mobileBodyWithValidators = """
			{
			  "viewConfigDiff": [],
			  "validators": {}
			}
			""";
		PageValidateArgs args = new(mobileBodyWithValidators, null);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "mobile pages do not support validators — validate-page must reject the body");
		response.Validation.ContentOk.Should().BeFalse(
			because: "the 'validators' key is disallowed in mobile page bodies");
		response.Validation.Errors.Should().ContainSingle(e => e.Contains("validators"),
			because: "the error should identify the disallowed 'validators' key");
	}

	[Test]
	[Description("Returns invalid for a mobile JSON body that contains a 'handlers' section.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenMobileBodyContainsHandlers_ReturnsInvalid() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string mobileBodyWithHandlers = """
			{
			  "viewConfigDiff": [],
			  "handlers": []
			}
			""";
		PageValidateArgs args = new(mobileBodyWithHandlers, null);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "mobile pages do not support handlers — validate-page must reject the body");
		response.Validation.ContentOk.Should().BeFalse(
			because: "the 'handlers' key is disallowed in mobile page bodies");
		response.Validation.Errors.Should().ContainSingle(e => e.Contains("handlers"),
			because: "the error should identify the disallowed 'handlers' key");
	}

	[Test]
	[Description("Returns invalid for a mobile JSON body that contains a 'converters' section.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenMobileBodyContainsConverters_ReturnsInvalid() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string mobileBodyWithConverters = """
			{
			  "viewConfigDiff": [],
			  "converters": {}
			}
			""";
		PageValidateArgs args = new(mobileBodyWithConverters, null);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "mobile pages do not support custom converters — validate-page must reject the body");
		response.Validation.ContentOk.Should().BeFalse(
			because: "the 'converters' key is disallowed in mobile page bodies");
		response.Validation.Errors.Should().ContainSingle(e => e.Contains("converters"),
			because: "the error should identify the disallowed 'converters' key");
	}

	// Chart-widget validation moved to a registry-driven walk (SchemaValidationService.ValidateChartWidgetConfig
	// + ChartWidgetValidation) that needs the component catalog. It is unit-tested against a registry fixture in
	// SchemaValidationServiceTests; an end-to-end tool test would require stubbing IComponentInfoCatalog
	// (ComponentCatalogState.GlobalReferences + per-component References), which the substituted catalog here
	// does not provide — so the empty-catalog path is fail-open (no chart error) by design.

	[Test]
	[Description("Returns all errors when mobile body contains multiple disallowed sections.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenMobileBodyContainsMultipleDisallowedSections_ReturnsAllErrors() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string mobileBodyWithMultiple = """
			{
			  "viewConfigDiff": [],
			  "handlers": [],
			  "validators": {}
			}
			""";
		PageValidateArgs args = new(mobileBodyWithMultiple, null);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeFalse(
			because: "both 'handlers' and 'validators' are disallowed in mobile pages");
		response.Validation.Errors!.Count.Should().Be(2,
			because: "each disallowed key should produce a distinct validation error");
	}

	[Test]
	[Description("Mobile path: warns when a label references a $Resources.Strings key that is not DS-auto-provided and is not present in the supplied resources argument.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenMobileLabelResourceKeyMissingFromSuppliedResources_ReturnsWarning() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string mobileBody = """
			{
			  "viewConfigDiff": [
			    {"operation":"merge","name":"UsrName","values":{"type":"crt.Input","label":"$Resources.Strings.PDS_UsrName","control":"$UsrName"}}
			  ],
			  "viewModelConfigDiff": [
			    {"operation":"merge","path":["attributes"],"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}}
			  ]
			}
			""";
		// Empty resources object — required to opt the page into label-resource validation (parity with web flow).
		PageValidateArgs args = new(mobileBody, "{}");

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert — proves resources are threaded into mobile validation: the empty object opts in, the label-warning fires.
		response.Valid.Should().BeTrue(
			because: "missing label resources are warnings, not errors");
		response.Validation.Warnings.Should().NotBeNull(
			because: "the page validator must surface label-resource warnings on the mobile path when resources are supplied");
		response.Validation.Warnings!.Should().Contain(w => w.Contains("PDS_UsrName") && w.Contains("render blank"),
			because: "the platform auto-provides captions only under the attribute name 'UsrName', not under the path-with-underscores form 'PDS_UsrName'");
	}

	[Test]
	[Description("Mobile path: suppresses the label-warning when the resources argument supplies the referenced key — proves that args.Resources is threaded into mobile validation.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenMobileLabelResourceKeyDeclaredInResources_ReturnsNoWarning() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string mobileBody = """
			{
			  "viewConfigDiff": [
			    {"operation":"merge","name":"UsrName","values":{"type":"crt.Input","label":"$Resources.Strings.PDS_UsrName","control":"$UsrName"}}
			  ],
			  "viewModelConfigDiff": [
			    {"operation":"merge","path":["attributes"],"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}}
			  ]
			}
			""";
		// Resources is a JSON object whose keys are resource names (matches PageValidateArgs.Resources contract used by web flow).
		string resources = """{"PDS_UsrName":"Name"}""";
		PageValidateArgs args = new(mobileBody, resources);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeTrue();
		(response.Validation.Warnings ?? []).Should().NotContain(w => w.Contains("PDS_UsrName"),
			because: "the resource key is explicitly declared in args.Resources and must be honored on the mobile path");
	}

	[Test]
	[Description("Mobile path: the differ oracle surfaces a not-a-container error when a child insert targets a slot the in-diff parent does not declare (no auto-repair).")]
	public async System.Threading.Tasks.Task ValidatePage_WhenMobileInsertTargetsUndeclaredParentSlot_ReturnsInvalid() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string mobileBody = """
			{ "viewConfigDiff": [
				{ "operation": "insert", "name": "ProductsList", "parentName": "ProductsListContainer", "propertyName": "items",
					"values": { "type": "crt.List", "items": "$ProductsList" } },
				{ "operation": "insert", "name": "ProductsList_ListItem", "parentName": "ProductsList", "propertyName": "itemLayout",
					"values": { "type": "crt.ListItem", "title": "$PDS_Name" } }
			] }
			""";
		PageValidateArgs args = new(mobileBody, null);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert — the validator now reproduces the server error instead of injecting the missing slot.
		response.Valid.Should().BeFalse(
			because: "the differ rejects an insert into a slot the parent does not declare");
		response.Validation.Errors.Should().Contain(e => e.Contains("ProductsList") && e.Contains("is not a container for other items"),
			because: "the differ oracle must surface the faithful error for model analysis");
	}

	[Test]
	[Description("Routes AMD bodies through AMD validation (not mobile path) when body starts with 'define('.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenBodyIsAmd_UsesAmdValidation() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string amdBody =
			"define(\"UsrPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageValidateArgs args = new(amdBody, null);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeTrue(
			because: "a well-formed AMD body should pass all AMD validations");
		response.Validation.MarkersOk.Should().BeTrue(
			because: "the AMD body has all required markers in correct order");
		response.Validation.JsSyntaxOk.Should().BeTrue(
			because: "the AMD body is syntactically valid JavaScript");
	}

	[Test]
	[Description("Web path: rejects a syntactically valid AMD body whose handler calls an undeclared module-scope helper, so the Page Designer loss is caught before save")]
	public async System.Threading.Tasks.Task ValidatePage_WhenHandlerCallsUndeclaredHelper_ReturnsInvalid() {
		// Arrange
		PageValidateTool tool = CreateTool();
		string amdBody =
			"define(\"UsrPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { await missingModuleHelper(request); return next?.handle(request); } }]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		PageValidateResponse response = await tool.ValidatePage(new PageValidateArgs(amdBody, null));

		// Assert
		response.Valid.Should().BeFalse(
			because: "a missing module-scope helper causes a runtime ReferenceError even though the JavaScript is syntactically valid");
		response.Validation.ContentOk.Should().BeFalse(
			because: "undefined handler calls are blocking content findings before update-page or sync-pages can save the body");
		response.Validation.Errors.Should().Contain(e => e.Contains("undefined-section-call") && e.Contains("missingModuleHelper"),
			because: "the MCP response must expose both the stable rule id and the missing helper name");
	}

	[Test]
	[Description("Web path: scopes the registry-driven chart-widget validation to the explicit version argument.")]
	public async System.Threading.Tasks.Task ValidatePage_WhenVersionProvided_ScopesChartCatalogToThatVersion() {
		// Arrange
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageValidateTool tool = new(Substitute.For<IMobileComponentInfoCatalog>(), webCatalog, new MockFileSystem());
		string amdBody =
			"define(\"UsrPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageValidateArgs args = new(amdBody, null, "8.1.5");

		// Act
		await tool.ValidatePage(args);

		// Assert
		string requestedVersion = (string)webCatalog.ReceivedCalls()
			.Single(c => c.GetMethodInfo().Name == nameof(IComponentInfoCatalog.LoadAsync))
			.GetArguments()[0];
		requestedVersion.Should().Be("8.1.5",
			because: "validate-page must scope its chart-widget pre-flight check to the version the agent passed");
	}
}
