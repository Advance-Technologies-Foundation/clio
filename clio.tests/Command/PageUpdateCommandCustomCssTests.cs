namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandCustomCssTests
{
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";

	// A web page body whose viewConfigDiff insert introduces a non-empty custom inline 'styles' object.
	private const string CustomCssBody =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"RedLabel\",\"values\":{\"type\":\"crt.Label\",\"styles\":{\"color\":\"red\"}}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private PageUpdateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		StubNameMetadata();
		StubDesignerEndpoints();
		_command = new PageUpdateCommand(
			_applicationClient, _serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), CreateHierarchyClient());
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
	}

	private void StubNameMetadata() {
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
	}

	private void StubDesignerEndpoints() {
		_applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }""");
		_applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
	}

	private static IPageDesignerHierarchyClient CreateHierarchyClient() {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		return hierarchyClient;
	}

	private static PageUpdateOptions CreateOptions(bool allowCustomCss) =>
		new() {
			SchemaName = SchemaName,
			Body = CustomCssBody,
			AllowCustomCss = allowCustomCss
		};

	[Test]
	[Description("TryUpdatePage must reject a body that introduces a custom inline 'styles' object and skip SaveSchema when allow-custom-css is not set.")]
	public void TryUpdatePage_ShouldRejectAndSkipSave_WhenStylesBodyAndAllowCustomCssFalse() {
		// Arrange
		PageUpdateOptions options = CreateOptions(allowCustomCss: false);

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(
			because: "custom CSS is a last resort and must be rejected without explicit confirmation");
		response.Success.Should().BeFalse(
			because: "the save must not proceed while the custom-CSS gate blocks the body");
		response.Error.Should().Contain("custom CSS",
			because: "the error must tell the agent the body introduces custom CSS");
		response.Error.Should().Contain("allow-custom-css",
			because: "the error must name the flag that authorizes custom CSS");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must pass the custom-CSS gate and save when allow-custom-css is set for a body that introduces a 'styles' object.")]
	public void TryUpdatePage_ShouldPassStylesGateAndSave_WhenAllowCustomCssTrue() {
		// Arrange
		PageUpdateOptions options = CreateOptions(allowCustomCss: true);

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(
			because: "allow-custom-css=true authorizes a body that introduces a custom 'styles' object");
		response.Success.Should().BeTrue(
			because: "the save must proceed once custom CSS is explicitly allowed");
		_applicationClient.Received(1).ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}
