using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandBaselineTests {

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";
	private const string MetaPath = "/ws/.clio-pages/Test_FormPage/meta.json";

	private const string ValidBody =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient;
	private IPageBaselineGuard _guard;
	private PageUpdateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
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
		_guard = Substitute.For<IPageBaselineGuard>();
		_command = new PageUpdateCommand(_applicationClient, serviceUrlBuilder, logger, hierarchyClient, null, _guard);
	}

	private void StubChecksumByUId(params string[] responses) {
		System.Collections.Generic.Queue<string> queue = new(responses);
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => queue.Count > 0 ? queue.Dequeue() : """{"success": false}""");
	}

	private static string ChecksumRow(string checksum) =>
		$$"""{"success": true, "rows": [{"Checksum": "{{checksum}}", "ModifiedOn": "2026-06-16T09:00:00"}]}""";

	private void ArmGuardWithChecksum(string baselineChecksum) =>
		_guard.TryArm(Arg.Any<PageUpdateOptions>(), Arg.Any<string>())
			.Returns(ci => {
				ci.Arg<PageUpdateOptions>().ExpectedChecksum = baselineChecksum;
				ci.Arg<PageUpdateOptions>().ExpectedSchemaUId = SchemaUId;
				return (MetaPath, true);
			});

	private static PageUpdateOptions CreateOptions(bool dryRun = false) =>
		new() { SchemaName = SchemaName, Body = ValidBody, Environment = "dev", DryRun = dryRun };

	[Test]
	[Description("Execute must arm the baseline guard before saving and refresh it after a successful save.")]
	public void Execute_ShouldRefreshBaseline_WhenSaveSucceedsAndArmed() {
		// Arrange
		ArmGuardWithChecksum("baseline-checksum");
		StubChecksumByUId(ChecksumRow("baseline-checksum"), ChecksumRow("fresh-after-save"));

		// Act
		int exitCode = _command.Execute(CreateOptions());

		// Assert
		exitCode.Should().Be(0, because: "a matching baseline lets the CLI save proceed");
		_guard.Received(1).RefreshOrDrop(MetaPath, Arg.Any<PageUpdateOptions>(), Arg.Is<PageUpdateResponse>(r => r.Success));
	}

	[Test]
	[Description("Execute must surface a non-zero exit code and skip the refresh when the on-disk baseline conflicts with the server checksum.")]
	public void Execute_ShouldReturnFailure_WhenArmedChecksumDiffers() {
		// Arrange
		ArmGuardWithChecksum("baseline-checksum");
		StubChecksumByUId(ChecksumRow("server-changed-checksum"));

		// Act
		int exitCode = _command.Execute(CreateOptions());

		// Assert
		exitCode.Should().Be(1, because: "an external modification must block the CLI save");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_guard.DidNotReceive().RefreshOrDrop(Arg.Any<string>(), Arg.Any<PageUpdateOptions>(), Arg.Any<PageUpdateResponse>());
	}

	[Test]
	[Description("Execute must not refresh the baseline when the guard reports it is not armed (no on-disk baseline).")]
	public void Execute_ShouldNotRefreshBaseline_WhenNotArmed() {
		// Arrange
		_guard.TryArm(Arg.Any<PageUpdateOptions>(), Arg.Any<string>()).Returns(((string)null, false));
		StubChecksumByUId();

		// Act
		int exitCode = _command.Execute(CreateOptions());

		// Assert
		exitCode.Should().Be(0, because: "a CLI save without a baseline must proceed unchanged");
		_guard.DidNotReceive().RefreshOrDrop(Arg.Any<string>(), Arg.Any<PageUpdateOptions>(), Arg.Any<PageUpdateResponse>());
	}

	[Test]
	[Description("Execute must not refresh the baseline on a dry-run even when armed, because no save occurred.")]
	public void Execute_ShouldNotRefreshBaseline_WhenDryRun() {
		// Arrange
		_guard.TryArm(Arg.Any<PageUpdateOptions>(), Arg.Any<string>()).Returns((MetaPath, true));
		StubChecksumByUId();

		// Act
		int exitCode = _command.Execute(CreateOptions(dryRun: true));

		// Assert
		exitCode.Should().Be(0, because: "a dry-run validation must succeed");
		_guard.DidNotReceive().RefreshOrDrop(Arg.Any<string>(), Arg.Any<PageUpdateOptions>(), Arg.Any<PageUpdateResponse>());
	}
}
