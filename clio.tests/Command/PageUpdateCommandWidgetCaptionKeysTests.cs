using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Issue #1740: the widget-caption check of <c>update-page</c> resolves a key against what the target schema
/// already stores and inherits, not only against the body and the call's <c>resources</c>. Before the fix a
/// layout-only re-save of a page warned once for every caption key an earlier call had registered, and a save
/// whose caption bound a key declared only in the designer hierarchy was refused although it renders.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandWidgetCaptionKeysTests {

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string ParentSchemaUId = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff";
	private const string SchemaName = "Test_FormPage";
	private const string PackageUId = "test-pkg-uid";
	private const string StoredKey = "ProbeButton_caption";
	private const string InheritedOnlyKey = "PostponeQueueItemButton_caption";
	private const string MissingKey = "NeverRegistered_caption";
	private const string UnresolvedCaptionFragment = "will not be registered";

	private IApplicationClient _applicationClient;
	private IPageDesignerHierarchyClient _hierarchyClient;
	private PageUpdateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetClientUnitDesignerSchema).Returns(GetSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveClientUnitDesignerSchema).Returns(SaveSchemaUrl);
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		_applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		_hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		_hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(PackageUId);
		StubHierarchy(parentKeys: []);
		_command = new PageUpdateCommand(
			_applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), new PersistedResourceKeyReader(),
			_hierarchyClient, viewConfigApplierFactory: () => Substitute.For<IJsonDiffApplier>());
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_hierarchyClient.ClearReceivedCalls();
	}

	/// <summary>
	/// Stubs the designer hierarchy: the target schema first, then one parent level carrying
	/// <paramref name="parentKeys"/> - the shape <c>GetParentSchemas</c> returns for a page whose ancestor declares
	/// keys that <c>GetSchema</c> leaves out.
	/// </summary>
	private void StubHierarchy(params string[] parentKeys) =>
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = PackageUId },
			new PageDesignerHierarchySchema {
				UId = ParentSchemaUId, Name = "ParentTemplate", PackageUId = "parent-pkg-uid",
				LocalizableStrings = BuildLocalizableStrings(parentKeys)
			}
		]);

	/// <summary>Stubs the designer <c>GetSchema</c> answer with the given stored <c>localizableStrings</c> keys.</summary>
	private void StubStoredKeys(params string[] keys) =>
		_applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject {
					["uId"] = SchemaUId,
					["name"] = SchemaName,
					["body"] = "old body",
					["localizableStrings"] = BuildLocalizableStrings(keys)
				}
			}.ToString());

	private static JArray BuildLocalizableStrings(string[] keys) =>
		new(keys.Select(key => new JObject { ["name"] = key, ["value"] = "stored caption" }));

	/// <summary>A replace body that inserts one button whose caption binds <paramref name="key"/>.</summary>
	private static string BuildButtonBody(string key) =>
		"define(\"" + SchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"ProbeButton\"," +
		"\"parentName\":\"ActionButtonsContainer\",\"propertyName\":\"items\",\"index\":0," +
		"\"values\":{\"type\":\"crt.Button\",\"caption\":\"$Resources.Strings." + key + "\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static PageUpdateOptions ReplaceDryRun(string body, string resources = null) =>
		new() { SchemaName = SchemaName, Body = body, Resources = resources, DryRun = true };

	private static PageUpdateOptions ReplaceSave(string body) =>
		new() { SchemaName = SchemaName, Body = body };

	private int CallCount(string url) => _applicationClient.ReceivedCalls().Count(call =>
		call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest) &&
		call.GetArguments().FirstOrDefault() as string == url);

	[Test]
	[Description("Issue #1740: a replace dry run without resources does not warn about a caption key the schema already stores - the layout-only re-save that reported one false warning per registered key.")]
	public void TryUpdatePage_ShouldNotWarn_WhenReplaceDryRunCaptionKeyIsAlreadyStored() {
		// Arrange
		StubStoredKeys(StoredKey);

		// Act
		bool result = _command.TryUpdatePage(ReplaceDryRun(BuildButtonBody(StoredKey)), out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a dry run of a valid body succeeds");
		(response.Warnings ?? []).Should().NotContain(warning => warning.Contains(UnresolvedCaptionFragment),
			because: "the key is stored on the schema and renders at runtime, so warning about it is the false positive of issue #1740");
		CallCount(SaveSchemaUrl).Should().Be(0, because: "a dry run never writes");
	}

	[Test]
	[Description("Issue #1740: a replace dry run still warns about a caption key that is neither sent, stored nor inherited - the fix narrows the warning to genuinely missing keys and does not silence it.")]
	public void TryUpdatePage_ShouldStillWarn_WhenReplaceDryRunCaptionKeyIsMissingEverywhere() {
		// Arrange
		StubStoredKeys(StoredKey);
		StubHierarchy(InheritedOnlyKey);

		// Act
		bool result = _command.TryUpdatePage(ReplaceDryRun(BuildButtonBody(MissingKey)), out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the replace dry-run caption check is advisory");
		response.Warnings.Should().ContainSingle(warning => warning.Contains(UnresolvedCaptionFragment),
				because: "a key nobody registers renders the raw binding text, which is exactly what the warning exists to report")
			.Which.Should().Contain(MissingKey, because: "the warning must name the key that is actually missing");
	}

	[Test]
	[Description("Issue #1740: a replace dry run does not warn about a caption key that only the designer hierarchy declares - GetSchema omits keys from an ancestor's replacing schema in another package, and they render at runtime.")]
	public void TryUpdatePage_ShouldNotWarn_WhenReplaceDryRunCaptionKeyIsOnlyInheritedFromTheHierarchy() {
		// Arrange
		StubStoredKeys();
		StubHierarchy(InheritedOnlyKey);

		// Act
		bool result = _command.TryUpdatePage(ReplaceDryRun(BuildButtonBody(InheritedOnlyKey)), out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a dry run of a valid body succeeds");
		(response.Warnings ?? []).Should().NotContain(warning => warning.Contains(UnresolvedCaptionFragment),
			because: "the key is declared by a parent level of the resolved hierarchy and resolves at runtime");
	}

	[Test]
	[Description("Issue #1740: a replace dry run whose caption key is supplied in resources does not read the schema - the stored-key read stays on the failure path, so a clean body costs no extra round trip.")]
	public void TryUpdatePage_ShouldNotReadTheSchema_WhenReplaceDryRunCaptionKeyIsInResources() {
		// Arrange
		StubStoredKeys(StoredKey);
		string resources = "{\"" + MissingKey + "\": \"Probe\"}";

		// Act
		bool result = _command.TryUpdatePage(
			ReplaceDryRun(BuildButtonBody(MissingKey), resources), out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the caption key is registered by the call itself");
		(response.Warnings ?? []).Should().NotContain(warning => warning.Contains(UnresolvedCaptionFragment),
			because: "the key is in resources");
		CallCount(GetSchemaUrl).Should().Be(0,
			because: "a replace dry run fetches nothing from the designer service unless a caption is still unresolved");
	}

	[Test]
	[Description("Issue #1740: a real save whose inserted caption binds a key declared only in the designer hierarchy is saved, not refused - the save gate and the dry run read the same key set.")]
	public void TryUpdatePage_ShouldSave_WhenCaptionKeyIsOnlyInheritedFromTheHierarchy() {
		// Arrange
		StubStoredKeys();
		StubHierarchy(InheritedOnlyKey);

		// Act
		bool saved = _command.TryUpdatePage(ReplaceSave(BuildButtonBody(InheritedOnlyKey)), out PageUpdateResponse response);

		// Assert
		saved.Should().BeTrue(
			because: "the key resolves at runtime from the parent level, so refusing the save was a false refusal");
		response.Error.Should().BeNull(because: "a successful save carries no error");
		CallCount(SaveSchemaUrl).Should().Be(1, because: "the save must reach the designer service exactly once");
	}

	[Test]
	[Description("Issue #1740: the persisted-key read behind the label-resource rescue carries the hierarchy keys too, so an inserted field whose label binds a key declared only by a parent level is saved rather than refused.")]
	public void TryUpdatePage_ShouldSave_WhenInsertedFieldLabelKeyIsOnlyInheritedFromTheHierarchy() {
		// Arrange - the label binds a key that differs from the control attribute, so the platform cannot
		// auto-provide it: only the stored/inherited key set can resolve it.
		StubStoredKeys();
		StubHierarchy(InheritedOnlyKey);
		string body =
			"define(\"" + SchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"CaseSLA\"," +
			"\"values\":{\"type\":\"crt.Input\",\"label\":\"$Resources.Strings." + InheritedOnlyKey + "\",\"control\":\"$PDS_CaseSLA\"}}]" +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"path\":[]," +
			"\"values\":{\"attributes\":{\"PDS_CaseSLA\":{\"modelConfig\":{\"path\":\"PDS.UsrSLA\"}}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		bool saved = _command.TryUpdatePage(ReplaceSave(body), out PageUpdateResponse response);

		// Assert
		saved.Should().BeTrue(
			because: "the label key resolves at runtime from the parent level, the same set the caption check reads");
		response.Error.Should().BeNull(because: "a successful save carries no error");
		CallCount(SaveSchemaUrl).Should().Be(1, because: "the save must reach the designer service exactly once");
	}

	[Test]
	[Description("Issue #1740: a real save whose inserted caption binds a key that is neither sent, stored nor inherited is still refused before SaveSchema.")]
	public void TryUpdatePage_ShouldRefuse_WhenCaptionKeyIsMissingEverywhere() {
		// Arrange
		StubStoredKeys(StoredKey);
		StubHierarchy(InheritedOnlyKey);

		// Act
		bool saved = _command.TryUpdatePage(ReplaceSave(BuildButtonBody(MissingKey)), out PageUpdateResponse response);

		// Assert
		saved.Should().BeFalse(because: "a caption bound to a key nobody registers renders raw");
		response.Error.Should().Contain("unregistered localizable strings",
				because: "the refusal must name the defect class")
			.And.Contain(MissingKey, because: "the refusal must name the missing key");
		CallCount(SaveSchemaUrl).Should().Be(0, because: "a refused save must not reach the designer service");
	}
}
