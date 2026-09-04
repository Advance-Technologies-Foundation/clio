using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// PR #1356 review (d-krestov, Gate 3): AC-2's PRODUCTION wiring had no coverage at any level. The seven
/// SchemaValidationServiceTests cases all call <c>ValidateFieldLabelResources</c> with a hand-built
/// <c>HashSet</c> or a counting lambda, so the chain that actually delivers AC-2 —
/// <c>provider -> TryResolveContext -> IsCreateReplacing guard -> TryGetSchema -> schema["localizableStrings"]
/// -> ResourceStringHelper.GetExistingKeys -> key match -> save proceeds</c> — was entirely unexercised, and
/// every step in it could be broken by a refactor with a green suite. These tests drive the real
/// <see cref="PageUpdateCommand"/> over a stubbed client and assert the end-to-end outcome (save issued /
/// save refused) plus the round-trip cost the rescue claims.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandPersistedResourcesTests {

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";
	private const string PersistedResourceKey = "CaseSLA_label";

	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private PageUpdateCommand _command;

	/// <summary>
	/// Body that inserts a field whose label points at <see cref="PersistedResourceKey"/> while binding to a
	/// DIFFERENT attribute name, so the platform cannot auto-provide the caption: the validator rejects it
	/// unless the key is found among the persisted ones. This is the exact shape issue #1320 reported — the
	/// second save of a page whose key was registered by the first one.
	/// </summary>
	private static string BuildPersistedResourcePageBody() =>
		BuildDiffBackedPageBody(
			"""
			[
				{
					"operation":"insert",
					"name":"CaseSLA",
					"values":{"type":"crt.Input","label":"$Resources.Strings.CaseSLA_label","control":"$PDS_CaseSLA"}
				}
			]
			""",
			"""
			[
				{
					"operation":"merge",
					"path":[],
					"values":{"attributes":{"PDS_CaseSLA":{"modelConfig":{"path":"PDS.UsrSLA"}}}}
				}
			]
			""");

	/// <summary>A body with nothing for the label-resource validators to reject.</summary>
	private static string BuildCleanPageBody() => BuildDiffBackedPageBody("[]", "[]");

	private static string BuildDiffBackedPageBody(string viewConfigDiff, string viewModelConfigDiff) =>
		$$"""
		define(
			"{{SchemaName}}",
			/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
			function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfigDiff}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{{viewModelConfigDiff}}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			}
		);
		""";

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		_command = new PageUpdateCommand(
			_applicationClient, serviceUrlBuilder, _logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);
	}

	/// <summary>Stubs the GetSchema round-trip with the given persisted localizable-string keys.</summary>
	private void StubSchemaWithPersistedKeys(params string[] persistedKeys) {
		string entries = string.Join(",", System.Array.ConvertAll(persistedKeys,
			key => "{\"name\": \"" + key + "\", \"value\": \"stored caption\"}"));
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>())
			.Returns("{\"success\": true, \"schema\": {\"uId\": \"" + SchemaUId + "\", \"name\": \"" + SchemaName
				+ "\", \"body\": \"old body\", \"localizableStrings\": [" + entries + "]}}");
	}

	private static PageUpdateOptions CreateOptions(string body) =>
		new() { SchemaName = SchemaName, Body = body, Environment = "dev" };

	private int GetSchemaCallCount() => _applicationClient.ReceivedCalls().Count(call =>
		call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest) &&
		call.GetArguments().Length > 0 &&
		call.GetArguments()[0] as string == GetSchemaUrl);

	[Test]
	[Description("The whole AC-2 chain, end to end over the real command: a label resource that is NOT repeated in `resources` but IS already stored in the target schema's localizableStrings is read back through TryResolveContext -> TryGetSchema -> GetExistingKeys and the save proceeds (issue #1320).")]
	public void TryUpdatePage_ShouldSave_WhenTheLabelResourceIsAlreadyPersistedOnTheSchema() {
		// Arrange
		StubSchemaWithPersistedKeys(PersistedResourceKey);

		// Act
		bool saved = _command.TryUpdatePage(CreateOptions(BuildPersistedResourcePageBody()), out PageUpdateResponse response);

		// Assert
		saved.Should().BeTrue(
			because: "the key is stored on the schema and resolves at runtime, so a later save must not be forced to re-send it");
		response.Success.Should().BeTrue(because: "a rescued save reports success, not a validation failure");
		_applicationClient.Received().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>());
	}

	[Test]
	[Description("Non-vacuity guard for the test above: with the SAME body and an empty localizableStrings array on the schema, the production chain rejects the save. A refactor that made the rescue always report the key (or never consult the schema at all) would fail here.")]
	public void TryUpdatePage_ShouldRefuse_WhenTheLabelResourceIsPersistedNowhere() {
		// Arrange
		StubSchemaWithPersistedKeys();

		// Act
		bool saved = _command.TryUpdatePage(CreateOptions(BuildPersistedResourcePageBody()), out PageUpdateResponse response);

		// Assert
		saved.Should().BeFalse(
			because: "with the key stored nowhere the stricter verdict must stand rather than letting an unresolvable caption through");
		response.Error.Should().Contain(PersistedResourceKey,
			because: "the original diagnostic must survive the failed rescue and name the unresolved key");
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>());
	}

	[Test]
	[Description("The M2 carrier's claimed benefit, asserted rather than described (d-krestov point 3): the rescue costs exactly ONE extra GetSchema over a clean save - the tri-state carrier caches the read, so the two label-resource validators do not each pay a round-trip.")]
	public void TryUpdatePage_ShouldPayExactlyOneExtraGetSchema_WhenTheRescueRuns() {
		// Arrange
		StubSchemaWithPersistedKeys(PersistedResourceKey);
		_command.TryUpdatePage(CreateOptions(BuildCleanPageBody()), out PageUpdateResponse cleanResponse);
		int cleanSaveGetSchemaCalls = GetSchemaCallCount();
		_applicationClient.ClearReceivedCalls();

		// Act
		bool saved = _command.TryUpdatePage(CreateOptions(BuildPersistedResourcePageBody()), out PageUpdateResponse response);
		int rescuedSaveGetSchemaCalls = GetSchemaCallCount();

		// Assert
		cleanResponse.Success.Should().BeTrue(because: "the control save must succeed for its call count to be a valid baseline");
		cleanSaveGetSchemaCalls.Should().BeGreaterThan(0, because: "a zero baseline would make the comparison below vacuous");
		saved.Should().BeTrue(because: "the rescued save must still go through");
		response.Success.Should().BeTrue();
		rescuedSaveGetSchemaCalls.Should().Be(cleanSaveGetSchemaCalls + 1,
			because: "the rescue is one cached GetSchema on the failure path - one per validator, or one per key, would multiply the cost of every later save of a page with stored resources");
	}
}
