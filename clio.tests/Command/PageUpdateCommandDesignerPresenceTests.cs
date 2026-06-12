namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandDesignerPresenceTests {
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";

	private const string ValidBody =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient = null!;
	private IPageDesignerPresenceNotifier _notifier = null!;
	private PageUpdateCommand _sut = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_notifier = Substitute.For<IPageDesignerPresenceNotifier>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		_applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }""");
		_applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		_sut = new PageUpdateCommand(
			_applicationClient,
			serviceUrlBuilder,
			logger,
			hierarchyClient,
			_notifier);
	}

	private static PageUpdateOptions CreateOptions() =>
		new() {
			SchemaName = SchemaName,
			Body = ValidBody,
			NotifyDesignerPresence = true
		};

	[Test]
	[Description("TryUpdatePage calls the Designer Presence notifier only after a successful non-dry-run save when the update-page path enabled it.")]
	public void TryUpdatePage_ShouldCallDesignerPresenceNotifier_WhenSaveSucceeds() {
		// Arrange
		PageUpdateOptions options = CreateOptions();

		// Act
		bool result = _sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the save should succeed in the happy path");
		response.Success.Should().BeTrue(because: "the response should report the successful save");
		_notifier.Received(1).TryNotifyPageSaved(SchemaName, SchemaName);
	}

	[Test]
	[Description("TryUpdatePage does not call the Designer Presence notifier when validation fails before the save path is reached.")]
	public void TryUpdatePage_ShouldNotCallDesignerPresenceNotifier_WhenValidationFails() {
		// Arrange
		PageUpdateOptions options = CreateOptions();
		options.Body = null;

		// Act
		bool result = _sut.TryUpdatePage(options, out _);

		// Assert
		result.Should().BeFalse(because: "missing body content must fail validation before save");
		_notifier.DidNotReceive().TryNotifyPageSaved(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("TryUpdatePage does not call the Designer Presence notifier when a checksum conflict blocks the write.")]
	public void TryUpdatePage_ShouldNotCallDesignerPresenceNotifier_WhenConflictBlocksSave() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": [{"Checksum": "server-checksum", "ModifiedOn": "2026-06-12T09:00:00"}]}""");
		PageUpdateOptions options = CreateOptions();
		options.ExpectedChecksum = "baseline-checksum";

		// Act
		bool result = _sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "external-modification conflicts must stop the save");
		response.Conflict.Should().BeTrue(because: "the command should surface the structured conflict");
		_notifier.DidNotReceive().TryNotifyPageSaved(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("TryUpdatePage does not call the Designer Presence notifier for dry-run validation requests.")]
	public void TryUpdatePage_ShouldNotCallDesignerPresenceNotifier_WhenDryRun() {
		// Arrange
		PageUpdateOptions options = CreateOptions();
		options.DryRun = true;

		// Act
		bool result = _sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "dry-run validation should still succeed on a valid body");
		response.DryRun.Should().BeTrue(because: "the response should remain a dry-run result");
		_notifier.DidNotReceive().TryNotifyPageSaved(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("TryUpdatePage does not call the Designer Presence notifier when SaveSchema fails.")]
	public void TryUpdatePage_ShouldNotCallDesignerPresenceNotifier_WhenSaveFails() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": false, "errorInfo": {"message": "Save failed"}}""");
		PageUpdateOptions options = CreateOptions();

		// Act
		bool result = _sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "the command should report the underlying save failure");
		response.Success.Should().BeFalse(because: "SaveSchema failure must surface as an unsuccessful response");
		_notifier.DidNotReceive().TryNotifyPageSaved(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("TryUpdatePage keeps success=true and appends a warning when the Designer Presence notifier skips or fails after save.")]
	public void TryUpdatePage_ShouldAppendWarningAndKeepSuccess_WhenDesignerPresenceNotificationFailsOpen() {
		// Arrange
		PageUpdateOptions options = CreateOptions();
		_notifier.TryNotifyPageSaved(SchemaName, SchemaName)
			.Returns("Designer presence notification skipped: forms-auth cookies are unavailable. The page save already succeeded.");

		// Act
		bool result = _sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the page save must stay successful when the live notification fails open");
		response.Success.Should().BeTrue(because: "the notifier warning must not flip the save outcome");
		response.Warnings.Should().ContainSingle(
			because: "the structured response should surface the best-effort notification issue as a warning");
		response.Warnings![0].Should().Contain("Designer presence notification skipped",
			because: "the warning should explain why the live notification did not run");
	}
}
