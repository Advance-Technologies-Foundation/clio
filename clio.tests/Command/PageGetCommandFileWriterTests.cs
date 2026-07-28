using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageGetCommandFileWriterTests {

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string SchemaName = "Test_FormPage";

	private const string Body =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private IPageFileWriter _writer;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_writer = Substitute.For<IPageFileWriter>();
	}

	private PageGetCommand CreateCommand(IPageDesignerHierarchyClient hierarchyClient) =>
		new(_applicationClient, _serviceUrlBuilder, _logger,
			hierarchyClient ?? new PageDesignerHierarchyClient(_applicationClient, _serviceUrlBuilder),
			new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			_writer);

	[Test]
	[Description("Execute must persist page files through the file writer after a successful get-page, passing the output-directory.")]
	public void Execute_ShouldWritePageFiles_WhenGetPageSucceeds() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":"BasePage"}]}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas("uid-1", "pkg-1").Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = SchemaName, PackageUId = "pkg-1", PackageName = "UsrPkg", SchemaVersion = 1, Body = Body
			}
		]);
		_writer.WritePageFiles(Arg.Any<PageGetResponse>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(new PageGetResponse { Success = true });
		PageGetCommand command = CreateCommand(hierarchyClient);
		PageGetOptions options = new() { SchemaName = SchemaName, Environment = "dev", OutputDirectory = "/proj" };

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful get-page that also writes its files must succeed");
		_writer.Received(1).WritePageFiles(
			Arg.Any<PageGetResponse>(), SchemaName, "dev", null, "/proj");
	}

	[Test]
	[Description("Execute must keep the fetched page and exit 0 when get-page succeeds but the baseline write fails — the page is the primary deliverable, persisting the baseline is best-effort.")]
	public void Execute_ShouldKeepFetchedPageAndExitZero_WhenBaselineWriteFails() {
		// Arrange — get-page succeeds, but the file writer reports a write failure (e.g. locked body.js).
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":"BasePage"}]}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas("uid-1", "pkg-1").Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = SchemaName, PackageUId = "pkg-1", PackageName = "UsrPkg", SchemaVersion = 1, Body = Body
			}
		]);
		_writer.WritePageFiles(Arg.Any<PageGetResponse>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(new PageGetResponse { Success = false, Error = "body.js is locked" });
		PageGetCommand command = CreateCommand(hierarchyClient);
		PageGetOptions options = new() { SchemaName = SchemaName, Environment = "dev", OutputDirectory = "/proj" };

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a page successfully read from the server must not be discarded because its baseline could not be persisted");
		_logger.Received(1).WriteWarning(
			Arg.Is<string>(message => message.Contains("body.js is locked")));
	}

	[Test]
	[Description("get-page must keep the own schema's optionalProperties in the merged bundle even when the own schema has no body yet (freshly created page/dashboard).")]
	public void Execute_ShouldIncludeOwnSchemaOptionalProperties_WhenOwnSchemaHasNoBody() {
		// Arrange — the own (most-derived) schema carries seeded optionalProperties but has no body
		// diff yet; only its body-bearing template parent has a body. The own schema must not be
		// dropped from the merged bundle just because its body is null.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":"BaseDashboardTemplate"}]}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas("uid-1", "pkg-1").Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = SchemaName, PackageUId = "pkg-1", PackageName = "UsrPkg", SchemaVersion = 1,
				Body = null,
				OptionalProperties = new JArray {
					new JObject { ["key"] = "DashboardsEntitySchemaName", ["value"] = "Contact" },
					new JObject { ["key"] = "DashboardsElementName", ["value"] = "Dashboards" }
				}
			},
			new PageDesignerHierarchySchema {
				UId = "uid-2", Name = "BaseDashboardTemplate", PackageUId = "pkg-2", PackageName = "CrtUIPlatform",
				SchemaVersion = 1, Body = Body
			}
		]);
		PageGetResponse captured = null;
		_writer.WritePageFiles(Arg.Any<PageGetResponse>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(ci => { captured = ci.Arg<PageGetResponse>(); return new PageGetResponse { Success = true }; });
		PageGetCommand command = CreateCommand(hierarchyClient);
		PageGetOptions options = new() { SchemaName = SchemaName, Environment = "dev" };

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "get-page must succeed for a freshly created body-less schema");
		captured.Should().NotBeNull(because: "a successful get-page passes its full response to the file writer");
		captured.Bundle.Should().NotBeNull(because: "the merged bundle is built before the MCP wrapper compacts it to file paths");
		captured.Bundle.Name.Should().Be(SchemaName,
			because: "the bundle identity must be the requested page, not its first body-bearing ancestor");
		Dictionary<string, string> optionalProperties = captured.Bundle.OptionalProperties
			.OfType<JsonNode>()
			.ToDictionary(node => node["key"]?.ToString() ?? string.Empty, node => node["value"]?.ToString());
		optionalProperties.Should().ContainKey("DashboardsEntitySchemaName",
			because: "the own schema's seeded entity-schema link-back must survive the merge, not be dropped with its null body");
		optionalProperties["DashboardsEntitySchemaName"].Should().Be("Contact",
			because: "the persisted entity-schema link-back value must round-trip through the merged bundle");
		optionalProperties.Should().ContainKey("DashboardsElementName",
			because: "the own schema's seeded dashboards-element link-back must survive the merge");
		optionalProperties["DashboardsElementName"].Should().Be("Dashboards",
			because: "the persisted dashboards-element link-back value must round-trip through the merged bundle");
	}

	[Test]
	[Description("Execute must not write page files when get-page itself fails, and must report a non-zero exit code.")]
	public void Execute_ShouldNotWritePageFiles_WhenGetPageFails() {
		// Arrange — empty metadata makes TryGetPage fail before any files would be written.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success":true,"rows":[]}""");
		PageGetCommand command = CreateCommand(hierarchyClient: null);
		PageGetOptions options = new() { SchemaName = SchemaName, Environment = "dev", OutputDirectory = "/proj" };

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a failed get-page must surface a non-zero exit code");
		_writer.DidNotReceive().WritePageFiles(
			Arg.Any<PageGetResponse>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
	}
}
