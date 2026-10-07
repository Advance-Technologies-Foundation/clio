using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-102161 on the update-page write path, for web pages: a generated form page holds PDS in its own body and the
/// template declares only AttachmentListDS, so a replace write without PDS must stop at <c>ValidateBody</c>, the last
/// gate before the save.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageUpdateToolDataSourceTests {

	private const string SchemaName = "UsrPdsRepro_FormPage";
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";

	private const string BindingsViewModelConfig = """
		{ "attributes": { "UsrName": { "modelConfig": { "path": "PDS.UsrName" } }, "Id": { "modelConfig": { "path": "PDS.Id" } } } }
		""";

	private static readonly string TemplateBody = WebBody(
		modelConfig: """{ "dataSources": { "AttachmentListDS": { "type": "crt.EntityDataSource" } } }""");

	private static readonly string GeneratedBody = WebBody(
		viewModelConfig: BindingsViewModelConfig,
		modelConfig: """{ "dataSources": { "PDS": { "type": "crt.EntityDataSource", "config": { "entitySchemaName": "UsrPdsRepro" } } }, "primaryDataSourceName": "PDS" }""");

	private static readonly string BodyWithoutDataSource = WebBody(viewModelConfig: BindingsViewModelConfig, modelConfigDiff: "[]");

	private IApplicationClient _applicationClient;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
	}

	private static string WebBody(string? viewModelConfig = null, string? modelConfig = null, string? modelConfigDiff = null,
		string? viewModelConfigDiff = null) =>
		"define(\"" + SchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		(viewModelConfig is null ? "" : "viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/" + viewModelConfig + "/**SCHEMA_VIEW_MODEL_CONFIG*/, ") +
		(viewModelConfigDiff is null ? "" : "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/" + viewModelConfigDiff + "/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, ") +
		(modelConfig is null ? "" : "modelConfig: /**SCHEMA_MODEL_CONFIG*/" + modelConfig + "/**SCHEMA_MODEL_CONFIG*/, ") +
		(modelConfigDiff is null ? "" : "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/" + modelConfigDiff + "/**SCHEMA_MODEL_CONFIG_DIFF*/, ") +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private PageGetCommand CreateGetCommand(bool readSucceeds = true, string? ownBody = null) {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(readSucceeds
				? $$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":"PageWithTabsFreedomTemplate"}]}"""
				: """{"success":true,"rows":[]}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = SchemaName, PackageUId = "pkg-1", PackageName = "UsrPkg", SchemaVersion = 1, Body = ownBody ?? GeneratedBody
			},
			new PageDesignerHierarchySchema {
				UId = "uid-2", Name = "PageWithTabsFreedomTemplate", PackageUId = "pkg-2", PackageName = "CrtUIPlatform",
				SchemaVersion = 1, Body = TemplateBody
			}
		]);
		return new PageGetCommand(_applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), hierarchyClient,
			new PageSchemaBodyParser(), new PageBundleBuilder(() => new JsonDiffApplier(), () => new JsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>());
	}

	private static PageUpdateTool BuildTool(PageGetCommand getCommand) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(getCommand);
		return new PageUpdateTool(
			command: null,
			logger: ConsoleLogger.Instance,
			commandResolver: resolver,
			mobileComponentCatalog: Substitute.For<IMobileComponentInfoCatalog>(),
			webComponentCatalog: Substitute.For<IComponentInfoCatalog>(),
			pageBaselineGuard: new PageBaselineGuard(Substitute.For<System.IO.Abstractions.IFileSystem>()),
			persistedResourceKeyReader: new PersistedResourceKeyReader(),
			dataSourceValidator: new PageDataSourceReferenceValidator(new PageSchemaBodyParser()));
	}

	[Test]
	[Description("W6: a web replace write that keeps PDS bindings but sends an empty SCHEMA_MODEL_CONFIG_DIFF stops before the save.")]
	public void ValidateBody_WebReplaceDropsPds_AbortsTheSave() {
		// Arrange
		PageUpdateTool tool = BuildTool(CreateGetCommand());
		PageUpdateOptions options = new() { SchemaName = SchemaName, Body = BodyWithoutDataSource, Environment = "dev" };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> _) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().NotBeNull(because: "the replace base excludes the own body, the only place PDS is declared");
		failure.Success.Should().BeFalse(because: "the caller must see the save as failed");
		failure.Error.Should().Contain("Data source 'PDS'", because: "the failure names the dropped data source")
			.And.Contain("SCHEMA_MODEL_CONFIG", because: "the web remedy names the web body sections")
			.And.Contain("do not re-run with validate=false",
				because: "the generic escape-hatch hint update-page appends must not be read as permission here");
	}

	[Test]
	[Description("An append fragment binding to PDS is valid on a diff-form page: the append base includes the page's own body, which declares PDS.")]
	public void ValidateBody_WebAppendBindingToOwnPds_IsAccepted() {
		// Arrange
		string diffFormOwnBody = WebBody(modelConfigDiff: """
			[ { "operation": "merge", "path": [], "values": { "dataSources": { "PDS": { "type": "crt.EntityDataSource" } }, "primaryDataSourceName": "PDS" } } ]
			""");
		PageUpdateTool tool = BuildTool(CreateGetCommand(ownBody: diffFormOwnBody));
		string fragment = WebBody(viewModelConfigDiff: """
			[ { "operation": "merge", "path": ["attributes"], "values": { "UsrName": { "modelConfig": { "path": "PDS.UsrName" } } } } ]
			""");
		PageUpdateOptions options = new() { SchemaName = SchemaName, Body = fragment, Environment = "dev", Mode = "append" };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> warnings) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().BeNull(because: "an append keeps the own body, so PDS stays declared");
		(warnings ?? []).Should().NotContain(w => w.Contains("were not checked"),
			because: "the base was read, so the data-source check ran");
	}

	[Test]
	[Description("When update-page cannot read the base, the data-source check passes and says so in the response warnings.")]
	public void ValidateBody_BaseUnreadable_WarnsThatTheCheckDidNotRun() {
		// Arrange
		PageUpdateTool tool = BuildTool(CreateGetCommand(readSucceeds: false));
		PageUpdateOptions options = new() { SchemaName = SchemaName, Body = BodyWithoutDataSource, Environment = "dev" };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> warnings) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().BeNull(because: "an unreadable base must not turn into a false rejection");
		warnings.Should().Contain(w => w.Contains("PDS") && w.Contains("were not checked"),
			because: "a fail-open pass must be visible in the update-page response, not only in the log");
	}
}
