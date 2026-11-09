namespace Clio.Tests.Command.PageModalSize;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.PageModalSize;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageModalSizeServiceTests {
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string RuntimeUrl = "http://test/ServiceModel/AppearanceSettingsService.svc/GetAppearanceSettings";
	private const string PackageUId = "aa000000-0000-0000-0000-000000000001";
	private const string PageUId = "cc000000-0000-0000-0000-00000000000a";
	private const string OwnLayerUId = "dd000000-0000-0000-0000-000000000001";
	private const string ParentLayerUId = "dd000000-0000-0000-0000-000000000002";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IAddonSchemaDesignerClient _addonClient;
	private IPageSchemaResolver _pageSchemaResolver;
	private PageModalSizeService _service;
	private AddonSchemaDto _savedSchema;
	private AddonGetRequestDto _addonRequest;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_addonClient = Substitute.For<IAddonSchemaDesignerClient>();
		_pageSchemaResolver = Substitute.For<IPageSchemaResolver>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetAppearanceSettings).Returns(RuntimeUrl);
		_pageSchemaResolver.ResolveHierarchy(Arg.Any<string>(), Arg.Any<Guid>()).Returns(ModalHierarchy());
		_addonClient.When(client => client.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(callInfo => _savedSchema = callInfo.Arg<AddonSchemaDto>());
		_service = new PageModalSizeService(_applicationClient, _serviceUrlBuilder, _addonClient, _pageSchemaResolver);
	}

	private static IReadOnlyList<PageDesignerHierarchySchema> ModalHierarchy() => [
		new PageDesignerHierarchySchema { UId = PageUId, Name = "UsrOrder_MiniPage" },
		new PageDesignerHierarchySchema { UId = Guid.NewGuid().ToString(), Name = "BaseMiniPageTemplate" }
	];

	// The SelectQuery answers in call order: the package UId, then the add-on layer lookups (own uId, then parent).
	private void StubSelects(params string[] responses) {
		Queue<string> queue = new(responses);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(_ => queue.Dequeue());
	}

	private static string PackageRow() => $$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""";

	private static string LayerRow(string packageName) =>
		$$"""{"success": true, "rows": [{"PackageName": "{{packageName}}"}]}""";

	private const string NoRows = """{"success": true, "rows": []}""";

	private void StubAddon(string metaData) =>
		_addonClient.GetSchema(Arg.Do<AddonGetRequestDto>(request => _addonRequest = request))
			.Returns(new AddonSchemaDto {
				MetaData = metaData,
				AdditionalData = new Dictionary<string, JsonElement> {
					["uId"] = JsonSerializer.SerializeToElement(OwnLayerUId),
					["parentSchemaUId"] = JsonSerializer.SerializeToElement(ParentLayerUId),
					["futureField"] = JsonSerializer.SerializeToElement(new { kept = true })
				}
			});

	// The runtime answers in call order: before the write, then after it. The last size repeats.
	private void StubRuntime(params int?[] sizes) {
		// A bare null binds to the params array itself, so it stands for one "no size" answer.
		string[] responses = Array.ConvertAll(sizes ?? [null], size => size is null
			? """{"success": true, "value": {"modalSettings": null}}"""
			: """{"success": true, "value": {"modalSettings": {"size": """ + size + "}}}");
		_applicationClient.ExecutePostRequest(RuntimeUrl, Arg.Any<string>()).Returns(responses[0], responses[1..]);
	}

	[Test]
	[Description("Reports no stored size and the runtime default (small) for a modal page with no AppearanceSettings layer.")]
	public void Get_ShouldReportDefault_WhenNoSizeIsSet() {
		// Arrange
		StubSelects(PackageRow());
		StubAddon("""{"modalSettings": null}""");
		StubRuntime(null);

		// Act
		PageModalSizeReadResult result = _service.Get(new PageModalSizeReadRequest("Custom", "UsrOrder_MiniPage"));

		// Assert
		result.Size.Should().BeNull(because: "no layer sets a size");
		result.SizeValue.Should().BeNull(because: "no layer sets a size");
		result.SourcePackageName.Should().BeNull(because: "there is no layer to name");
		result.EffectiveSize.Should().Be("small", because: "a mini page with no size opens small at runtime");
		result.Warning.Should().BeNull(because: "the runtime agrees with the package view");
		result.AvailableSizes.Should().Equal(["small", "medium", "large", "extra-large"],
			because: "the read names the sizes a write accepts");
	}

	[Test]
	[Description("Names the vendor package the size comes from when the requested package has no layer of its own.")]
	public void Get_ShouldNameParentLayerPackage_WhenPackageHasNoOwnLayer() {
		// Arrange
		StubSelects(PackageRow(), NoRows, LayerRow("CrtUIv2"));
		StubAddon("""{"modalSettings": {"size": 3}}""");
		StubRuntime(3);

		// Act
		PageModalSizeReadResult result = _service.Get(new PageModalSizeReadRequest("Custom", "EmailMiniPage"));

		// Assert
		result.Size.Should().Be("large", because: "size 3 is the designer's L");
		result.SourcePackageName.Should().Be("CrtUIv2",
			because: "an unsaved add-on uId has no row, so the layer is the parent the add-on service resolved");
		result.EffectiveSize.Should().Be("large", because: "the runtime opens the window with the stored size");
	}

	[Test]
	[Description("Warns when a package above the requested one overrides the size the package sees.")]
	public void Get_ShouldWarn_WhenHigherLayerOverridesSize() {
		// Arrange
		StubSelects(PackageRow(), LayerRow("Custom"));
		StubAddon("""{"modalSettings": {"size": 2}}""");
		StubRuntime(4);

		// Act
		PageModalSizeReadResult result = _service.Get(new PageModalSizeReadRequest("Custom", "UsrOrder_MiniPage"));

		// Assert
		result.Size.Should().Be("medium", because: "the package sees its own layer");
		result.EffectiveSize.Should().Be("extra-large", because: "the window opens with what the runtime merges");
		result.Warning.Should().Contain("would not change the window",
			because: "a write to an overridden package would report success and change nothing the user sees");
	}

	[Test]
	[Description("Changes only modalSettings.size and keeps every other add-on field, including unknown ones.")]
	public void Set_ShouldKeepOtherAddonFields_WhenSizeChanges() {
		// Arrange
		StubSelects(PackageRow(), LayerRow("Custom"));
		StubAddon("""{"modalSettings": {"size": 2, "futureSetting": "x"}, "otherSettings": {"a": 1}}""");
		StubRuntime(2, 4);

		// Act
		PageModalSizeWriteResult result = _service.Set(
			new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "XL"));

		// Assert
		JsonObject saved = JsonNode.Parse(_savedSchema.MetaData)!.AsObject();
		saved["modalSettings"]!["size"]!.GetValue<int>().Should().Be(4, because: "XL is stored as size 4");
		saved["modalSettings"]!["futureSetting"]!.GetValue<string>().Should().Be("x",
			because: "other modalSettings fields must survive the write");
		saved["otherSettings"]!["a"]!.GetValue<int>().Should().Be(1,
			because: "other AppearanceSettings fields must survive the write");
		_savedSchema.AdditionalData["futureField"].GetProperty("kept").GetBoolean().Should().BeTrue(
			because: "the add-on payload is saved back as the service returned it");
		result.PreviousSize.Should().Be("medium", because: "the result names the size before the write");
		result.PreviousSourcePackageName.Should().Be("Custom", because: "the result names where the old size came from");
		result.Size.Should().Be("extra-large", because: "the result names the size written");
		result.Warning.Should().BeNull(because: "the runtime opens the window with the new size");
	}

	[Test]
	[Description("Creates modalSettings when the page has no size yet and passes the page as the add-on parent.")]
	public void Set_ShouldCreateModalSettings_WhenNoSizeIsSet() {
		// Arrange
		StubSelects(PackageRow());
		StubAddon("""{"modalSettings": null}""");
		StubRuntime(null, 3);

		// Act
		PageModalSizeWriteResult result = _service.Set(
			new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "large"));

		// Assert
		JsonNode.Parse(_savedSchema.MetaData)!["modalSettings"]!["size"]!.GetValue<int>().Should().Be(3,
			because: "large is stored as size 3");
		result.PreviousSize.Should().BeNull(because: "no size was set before the write");
		result.PreviousSizeValue.Should().BeNull(because: "no size was set before the write");
		_addonRequest.TargetParentSchemaUId.Should().Be(Guid.Parse(PageUId),
			because: "the page is passed as the parent so the add-on lands in the requested package");
		_addonRequest.TargetPackageUId.Should().Be(Guid.Parse(PackageUId), because: "the write goes to package-name");
		_addonRequest.TargetSchemaManagerName.Should().Be("ClientUnitSchemaManager",
			because: "AppearanceSettings is a page add-on");
		_addonRequest.AddonName.Should().Be("AppearanceSettings", because: "the size lives in AppearanceSettings");
	}

	[Test]
	[Description("Warns, but still reports success, when the runtime does not show the size just written.")]
	public void Set_ShouldWarn_WhenRuntimeStillShowsAnotherSize() {
		// Arrange
		StubSelects(PackageRow(), LayerRow("Custom"));
		StubAddon("""{"modalSettings": {"size": 2}}""");
		StubRuntime(2, 1);

		// Act
		PageModalSizeWriteResult result = _service.Set(
			new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "large"));

		// Assert
		result.Warning.Should().Contain("still opens small",
			because: "the caller must learn that the saved size does not reach the window");
	}

	[Test]
	[Description("Refuses before anything is written when a package above package-name sets the size (EC-1).")]
	public void Set_ShouldRefuseBeforeWriting_WhenHigherLayerOverridesSize() {
		// Arrange
		StubSelects(PackageRow(), LayerRow("Custom"));
		StubAddon("""{"modalSettings": {"size": 2}}""");
		StubRuntime(4);

		// Act
		Action act = () => _service.Set(new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "large"));

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a write under an overriding layer would report success and leave the window unchanged")
			.WithMessage("*opens extra-large, not medium*package above 'Custom'*Nothing was changed*");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
		_addonClient.DidNotReceive().ResetClientScriptCache();
		_addonClient.DidNotReceive().BuildConfiguration();
	}

	[Test]
	[Description("Surfaces a failed save as a failure and does not reset the cache or rebuild.")]
	public void Set_ShouldThrowAndNotRebuild_WhenSaveSchemaFails() {
		// Arrange
		StubSelects(PackageRow(), LayerRow("Custom"));
		StubAddon("""{"modalSettings": {"size": 2}}""");
		StubRuntime(2);
		_addonClient.When(client => client.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(_ => throw new InvalidOperationException("Package 'Custom' is locked."));

		// Act
		Action act = () => _service.Set(new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "large"));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a refused save must not read as a success")
			.WithMessage("*locked*");
		_addonClient.DidNotReceive().ResetClientScriptCache();
		_addonClient.DidNotReceive().BuildConfiguration();
	}

	[Test]
	[Description("Turns a failed runtime check after a committed save into a warning instead of a failure.")]
	public void Set_ShouldWarn_WhenRuntimeCheckFails() {
		// Arrange
		StubSelects(PackageRow(), LayerRow("Custom"));
		StubAddon("""{"modalSettings": {"size": 2}}""");
		_applicationClient.ExecutePostRequest(RuntimeUrl, Arg.Any<string>()).Throws(new InvalidOperationException("503"));

		// Act
		PageModalSizeWriteResult result = _service.Set(
			new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "large"));

		// Assert
		_savedSchema.Should().NotBeNull(because: "the save happened before the check");
		result.Warning.Should().Contain("could not be checked",
			because: "a failed check after a committed write is reported, not thrown");
	}

	[Test]
	[Description("Still writes the size when the lookup of the previous size's package fails, and reports it as a warning.")]
	public void Set_ShouldWriteAndWarn_WhenSourceLayerLookupFails() {
		// Arrange
		StubSelects(PackageRow(), """{"success": false}""");
		StubAddon("""{"modalSettings": {"size": 2}}""");
		StubRuntime(2, 3);

		// Act
		PageModalSizeWriteResult result = _service.Set(
			new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "large"));

		// Assert
		_savedSchema.Should().NotBeNull(because: "the package of the old size is informational and must not block the write");
		result.PreviousSourcePackageName.Should().BeNull(because: "the lookup failed");
		result.Warning.Should().Contain("could not be determined", because: "the failed lookup is reported");
	}

	[Test]
	[Description("Reports a stored value the designer does not offer by its raw value, with no size name.")]
	public void Set_ShouldReportUnsupportedPreviousValueByRawValue() {
		// Arrange
		StubSelects(PackageRow(), LayerRow("CrtUIv2"));
		StubAddon("""{"modalSettings": {"size": 5}}""");
		StubRuntime(5, 2);

		// Act
		PageModalSizeWriteResult result = _service.Set(
			new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "M"));

		// Assert
		result.PreviousSize.Should().BeNull(
			because: "Custom (5) is a platform value the designer never offers, so it has no size name");
		result.PreviousSizeValue.Should().Be(5, because: "the raw old value is still reported");
	}

	[TestCase("[1, 2]")]
	[TestCase("not json")]
	[Description("Refuses to write when the add-on metadata is not a JSON object, instead of replacing the other settings.")]
	public void Set_ShouldRefuseAndNotSave_WhenMetadataIsNotAnObject(string metaData) {
		// Arrange
		StubSelects(PackageRow());
		StubAddon(metaData);

		// Act
		Action act = () => _service.Set(new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "large"));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the other settings in that body could not be kept")
			.WithMessage("*not a JSON object*Nothing was changed*");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Refuses an unsupported size before any remote call, so nothing is read or written.")]
	public void Set_ShouldThrowBeforeAnyCall_WhenSizeIsUnsupported() {
		// Act
		Action act = () => _service.Set(new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "wider"));

		// Assert
		act.Should().Throw<UnsupportedPageModalSizeException>(because: "the platform does not support that size");
		_addonClient.DidNotReceive().GetSchema(Arg.Any<AddonGetRequestDto>());
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Refuses a page that does not inherit BaseMiniPageTemplate and writes nothing.")]
	public void Set_ShouldRefuse_WhenPageIsNotModal() {
		// Arrange
		StubSelects(PackageRow());
		_pageSchemaResolver.ResolveHierarchy(Arg.Any<string>(), Arg.Any<Guid>()).Returns([
			new PageDesignerHierarchySchema { UId = PageUId, Name = "UsrOrder_FormPage" },
			new PageDesignerHierarchySchema { UId = Guid.NewGuid().ToString(), Name = "PageWithTabsFreedomTemplate" }
		]);

		// Act
		Action act = () => _service.Set(new PageModalSizeWriteRequest("Custom", "UsrOrder_FormPage", "large"));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a form page has no modal window size")
			.WithMessage("*is not a modal page*Nothing was changed*");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[TestCase(null, "Custom")]
	[TestCase("UsrOrder_MiniPage", " ")]
	[Description("Requires a page name and a package name.")]
	public void Get_ShouldThrow_WhenRequiredFieldIsMissing(string pageSchemaName, string packageName) {
		// Act
		Action act = () => _service.Get(new PageModalSizeReadRequest(packageName, pageSchemaName));

		// Assert
		act.Should().Throw<ArgumentException>(because: "the page and the package identify the add-on layer");
	}

	[TestCase(null, "Custom")]
	[TestCase("UsrOrder_MiniPage", " ")]
	[Description("Requires a page name and a package name before any remote call on a write.")]
	public void Set_ShouldThrowBeforeAnyCall_WhenRequiredFieldIsMissing(string pageSchemaName, string packageName) {
		// Act
		Action act = () => _service.Set(new PageModalSizeWriteRequest(packageName, pageSchemaName, "large"));

		// Assert
		act.Should().Throw<ArgumentException>(because: "the page and the package identify the add-on layer");
		_addonClient.DidNotReceive().GetSchema(Arg.Any<AddonGetRequestDto>());
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Trims the page and package names before they reach the platform.")]
	public void Set_ShouldTrimNames_WhenTheyArePadded() {
		// Arrange
		StubSelects(PackageRow(), LayerRow("Custom"));
		StubAddon("""{"modalSettings": {"size": 2}}""");
		StubRuntime(2, 3);

		// Act
		PageModalSizeWriteResult result = _service.Set(
			new PageModalSizeWriteRequest(" Custom ", " UsrOrder_MiniPage ", "large"));

		// Assert
		_pageSchemaResolver.Received(1).ResolveHierarchy("UsrOrder_MiniPage", Guid.Parse(PackageUId));
		result.PackageName.Should().Be("Custom", because: "the package name is trimmed");
		result.PageSchemaName.Should().Be("UsrOrder_MiniPage", because: "the page name is trimmed");
	}

	[TestCase("[1, 2]")]
	[TestCase("not json")]
	[Description("Warns instead of reporting 'no size' when the add-on metadata is not a JSON object.")]
	public void Get_ShouldWarn_WhenMetadataIsNotAnObject(string metaData) {
		// Arrange
		StubSelects(PackageRow());
		StubAddon(metaData);
		StubRuntime(null);

		// Act
		PageModalSizeReadResult result = _service.Get(new PageModalSizeReadRequest("Custom", "UsrOrder_MiniPage"));

		// Assert
		result.Size.Should().BeNull(because: "no size could be read from that body");
		result.Warning.Should().Contain("is not a JSON object",
			because: "an unreadable body must not be reported as 'no size set'");
	}
}
