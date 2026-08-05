using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Tests for the <see cref="PageGetOptions.ExcludeOwnBody"/> path in <see cref="PageGetCommand.TryGetPage"/>:
/// when set, the response carries <see cref="PageGetResponse.BaseViewModelConfig"/> /
/// <see cref="PageGetResponse.BaseModelConfig"/> — the merged config EXCLUDING the editable schema's own body
/// (the runtime base a replace-mode write layers over) — while the main bundle stays the full merged config.
/// When not set, the base fields stay null (the option is validation-only and does not change the normal read).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageGetCommandExcludeOwnBodyTests {

	private const string SchemaName = "Test_FormPage";
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";

	// Parent template body: establishes viewModelConfig.attributes.BaseAttr and modelConfig.baseKey.
	private static readonly string ParentBody = BuildBody(
		vmcDiff: """[ { "operation": "merge", "path": [], "values": { "attributes": { "BaseAttr": { "type": "string" } } } } ]""",
		mcDiff: """[ { "operation": "merge", "path": [], "values": { "baseKey": "b" } } ]""");

	// Editable (head/own) body: layers viewModelConfig.attributes.OwnAttr and modelConfig.ownKey on top.
	private static readonly string HeadBody = BuildBody(
		vmcDiff: """[ { "operation": "merge", "path": ["attributes"], "values": { "OwnAttr": { "type": "string" } } } ]""",
		mcDiff: """[ { "operation": "merge", "path": [], "values": { "ownKey": "o" } } ]""");

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":"BaseTemplate"}]}""");
	}

	private static string BuildBody(string vmcDiff, string mcDiff) =>
		"define(\"" + SchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/" + vmcDiff + "/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/" + mcDiff + "/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private PageGetCommand CreateCommand() {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = SchemaName, PackageUId = "pkg-1", PackageName = "UsrPkg", SchemaVersion = 1, Body = HeadBody
			},
			new PageDesignerHierarchySchema {
				UId = "uid-2", Name = "BaseTemplate", PackageUId = "pkg-2", PackageName = "CrtPkg", SchemaVersion = 1, Body = ParentBody
			}
		]);
		return new PageGetCommand(_applicationClient, _serviceUrlBuilder, Substitute.For<ILogger>(),
			hierarchyClient, new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>());
	}

	[Test]
	[Description("ExcludeOwnBody makes get-page compute a base config that omits the editable schema's own body, while the main bundle keeps the full merged config.")]
	public void TryGetPage_ExcludeOwnBody_BaseConfigExcludesEditableOwnBody() {
		// Arrange
		PageGetCommand command = CreateCommand();
		PageGetOptions options = new() { SchemaName = SchemaName, Environment = "dev", ExcludeOwnBody = true };

		// Act
		bool ok = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		ok.Should().BeTrue(because: "the page reads successfully");
		response.BaseViewModelConfig.Should().NotBeNull(because: "ExcludeOwnBody requests the base-excluding-own-body config");
		string baseVmc = response.BaseViewModelConfig.ToJsonString();
		baseVmc.Should().Contain("BaseAttr", because: "the parent template contribution is part of the base");
		baseVmc.Should().NotContain("OwnAttr", because: "the editable schema's own body is excluded from the base");
		response.BaseModelConfig.ToJsonString().Should().Contain("baseKey").And.NotContain("ownKey",
			because: "modelConfig follows the same base rule");
		response.Bundle.ViewModelConfig.ToJsonString().Should().Contain("BaseAttr").And.Contain("OwnAttr",
			because: "the main bundle stays the FULL merged config, own body included");
	}

	[Test]
	[Description("Without ExcludeOwnBody the base config fields stay null — the option is validation-only and does not change the normal get-page read.")]
	public void TryGetPage_WithoutExcludeOwnBody_BaseConfigIsNull() {
		// Arrange
		PageGetCommand command = CreateCommand();
		PageGetOptions options = new() { SchemaName = SchemaName, Environment = "dev" };

		// Act
		bool ok = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		ok.Should().BeTrue(because: "the page reads successfully");
		response.BaseViewModelConfig.Should().BeNull(because: "the base config is only computed on demand for replace-mode validation");
		response.BaseModelConfig.Should().BeNull(because: "the base config is only computed on demand for replace-mode validation");
		response.Bundle.ViewModelConfig.ToJsonString().Should().Contain("BaseAttr").And.Contain("OwnAttr",
			because: "the normal read still returns the full merged config");
	}
}
