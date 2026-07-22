namespace Clio.Tests.Command;

using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageCreateCommandTests
{
	private const string TestBase = "http://test";
	private const string TemplateUId = "f691e828-0b36-42a7-898f-c337e9af67d0";
	private const string TemplateName = "BlankPageTemplate";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string SaveSchemaUrl = TestBase + "/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string GetSchemaUrl = TestBase + "/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string PackageUId = "aa000000-0000-0000-0000-000000000001";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ISchemaTemplateCatalog _catalog;
	private ILogger _logger;
	private PageCreateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_catalog = Substitute.For<ISchemaTemplateCatalog>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>())
			.Returns("""
				{"success": true, "schema": {"localizableStrings": [
					{"name": "DefaultPageTitle", "values": [{"cultureName": "en-US", "value": "Page title"}]},
					{"name": "SaveButton", "values": [{"cultureName": "en-US", "value": "Save"}]}
				]}}
				""");
		_catalog.FindTemplate(TemplateName).Returns(new PageTemplateInfo {
			UId = TemplateUId,
			Name = TemplateName,
			Title = "Blank page",
			GroupName = "Page",
			SchemaType = 9
		});
		_command = new PageCreateCommand(_applicationClient, _serviceUrlBuilder, _catalog, _logger,
			Substitute.For<Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver>());
	}

	[Test]
	public void TryCreatePage_Rejects_Missing_Schema_Name() {
		PageCreateOptions options = new() { Template = TemplateName, PackageName = "Custom" };

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryCreatePage_Rejects_Malformed_Schema_Name() {
		PageCreateOptions options = new() { SchemaName = "1InvalidName", Template = TemplateName, PackageName = "Custom" };

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name must start with a letter");
	}

	[Test]
	public void TryCreatePage_Rejects_Missing_Template() {
		PageCreateOptions options = new() { SchemaName = "UsrDemo_BlankPage", PackageName = "Custom" };

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("template is required");
	}

	[Test]
	public void TryCreatePage_Rejects_Missing_Package_Name() {
		PageCreateOptions options = new() { SchemaName = "UsrDemo_BlankPage", Template = TemplateName };

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("package-name");
	}

	[Test]
	public void TryCreatePage_Rejects_Unknown_Template() {
		_catalog.FindTemplate("NoSuchTemplate").Returns((PageTemplateInfo)null);
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_BlankPage",
			Template = "NoSuchTemplate",
			PackageName = "Custom"
		};

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("is not supported").And.Contain("list-page-templates");
	}

	[Test]
	public void TryCreatePage_Rejects_Missing_Package() {
		StubSelectQueryResponse(SelectQueryUrl, """{"success": true, "rows": []}""");
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_BlankPage",
			Template = TemplateName,
			PackageName = "DoesNotExist"
		};

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("DoesNotExist").And.Contain("not found");
	}

	[Test]
	public void TryCreatePage_Rejects_Duplicate_Schema_Name() {
		Queue<string> responses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": [{"UId": "11111111-2222-3333-4444-555555555555"}]}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => responses.Dequeue());
		PageCreateOptions options = new() {
			SchemaName = "UsrExisting_Page",
			Template = TemplateName,
			PackageName = "Custom"
		};

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("already exists");
	}

	[Test]
	public void TryCreatePage_Happy_Path_Posts_SaveSchema_And_Returns_SchemaUId() {
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_BlankPage",
			Template = TemplateName,
			PackageName = "Custom",
			Caption = "Demo page"
		};

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrDemo_BlankPage");
		response.SchemaUId.Should().NotBeNullOrWhiteSpace();
		response.TemplateName.Should().Be(TemplateName);
		response.PackageUId.Should().Be(PackageUId);
		response.Caption.Should().Be("Demo page");
		response.SchemaType.Should().Be(9);
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(s => s.Contains(TemplateUId)
				&& s.Contains("\"extendParent\":false")
				&& s.Contains("\"group\":\"Page\"")
				&& s.Contains("\"DefaultPageTitle\"")
				&& s.Contains("\"SaveButton\"")));
		_applicationClient.Received(1).ExecutePostRequest(GetSchemaUrl,
			Arg.Is<string>(s => s.Contains(TemplateUId) && s.Contains("\"useFullHierarchy\":false")));
	}

	private PageCreateCommand CommandWithDesignPackage(string designPackageUId, bool throws = false) {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		if (throws) {
			hierarchyClient.GetDesignPackageUId(Arg.Any<string>())
				.Returns(_ => throw new System.InvalidOperationException("probe failed"));
		} else {
			hierarchyClient.GetDesignPackageUId(Arg.Any<string>()).Returns(designPackageUId);
		}
		return new PageCreateCommand(_applicationClient, _serviceUrlBuilder, _catalog, _logger,
			Substitute.For<Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver>(), hierarchyClient);
	}

	private void StubHappyPathSelectAndSave() {
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
	}

	[Test]
	[Description("When the app design package differs from the chosen package, create-page surfaces designPackageUId + willCreateReplacingInDesignPackage and a note pointing to target-schema-uid.")]
	public void TryCreatePage_WhenChosenPackageIsNotDesignPackage_WarnsAboutSplit() {
		StubHappyPathSelectAndSave();
		PageCreateCommand command = CommandWithDesignPackage(designPackageUId: "design-pkg-uid");
		PageCreateOptions options = new() { SchemaName = "UsrDemo_BlankPage", Template = TemplateName, PackageName = "Custom" };

		bool result = command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeTrue(response.Error);
		response.WillCreateReplacingInDesignPackage.Should().BeTrue();
		response.DesignPackageUId.Should().Be("design-pkg-uid");
		response.Note.Should().Contain("target-schema-uid=" + response.SchemaUId);
	}

	[Test]
	[Description("When the chosen package IS the design package there is no split, so no warning is emitted.")]
	public void TryCreatePage_WhenChosenPackageIsDesignPackage_NoWarning() {
		StubHappyPathSelectAndSave();
		PageCreateCommand command = CommandWithDesignPackage(designPackageUId: PackageUId);
		PageCreateOptions options = new() { SchemaName = "UsrDemo_BlankPage", Template = TemplateName, PackageName = "Custom" };

		bool result = command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeTrue(response.Error);
		response.WillCreateReplacingInDesignPackage.Should().BeNull();
		response.DesignPackageUId.Should().BeNull();
	}

	[Test]
	[Description("The design-package probe is best-effort: a failure must not fail page creation.")]
	public void TryCreatePage_WhenDesignPackageProbeThrows_StillSucceeds() {
		StubHappyPathSelectAndSave();
		PageCreateCommand command = CommandWithDesignPackage(designPackageUId: null, throws: true);
		PageCreateOptions options = new() { SchemaName = "UsrDemo_BlankPage", Template = TemplateName, PackageName = "Custom" };

		bool result = command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeTrue(response.Error);
		response.WillCreateReplacingInDesignPackage.Should().BeNull();
	}

	[Test]
	public void TryCreatePage_Rejects_Caption_Whose_Script_Mismatches_Resolved_Culture() {
		// Arrange — resolve the profile culture to the Latin-script en-US, then pass a Cyrillic caption.
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver enUsResolver =
			Substitute.For<Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver>();
		enUsResolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>()).Returns("en-US");
		PageCreateCommand command = new(_applicationClient, _serviceUrlBuilder, _catalog, _logger, enUsResolver);
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_BlankPage",
			Template = TemplateName,
			PackageName = "Custom",
			Caption = "Замовлення"
		};

		// Act
		bool result = command.TryCreatePage(options, out PageCreateResponse response);

		// Assert
		result.Should().BeFalse("because a Cyrillic caption must not be stored under the Latin-script en-US culture (ENG-91044)");
		response.Error.Should().Contain("en-US", "because the failure must name the culture whose value is in the wrong script");
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>());
	}

	[Test]
	public void TryCreatePage_Copies_Template_Localizable_Strings_To_SaveSchema_Payload() {
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		string savePayload = null;
		_applicationClient.When(client => client.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()))
			.Do(call => savePayload = call.ArgAt<string>(1));
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_FormPage",
			Template = TemplateName,
			PackageName = "Custom",
			Caption = "Demo page"
		};

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeTrue(response.Error);
		JArray localizableStrings = (JArray)JObject.Parse(savePayload)["localizableStrings"];
		localizableStrings.Select(item => item["name"]!.ToString())
			.Should().BeEquivalentTo(["DefaultPageTitle", "SaveButton"],
				because: "SaveSchema deletes schema-level strings omitted from the request, so create-page must seed template resources");
	}

	[Test]
	public void TryCreatePage_Surface_Save_Error_From_DesignerService() {
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": false, "errorInfo": {"message": "schema name conflict"}}""");
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_BlankPage",
			Template = TemplateName,
			PackageName = "Custom"
		};

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Success.Should().BeFalse();
		response.Error.Should().Be("schema name conflict");
	}

	[Test]
	public void TryCreatePage_Resolves_Entity_Schema_When_Provided() {
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}""",
			"""{"success": true, "rows": [{"UId": "cccccccc-cccc-cccc-cccc-cccccccccccc"}]}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_BlankPage",
			Template = TemplateName,
			PackageName = "Custom",
			EntitySchemaName = "UsrDemoEntity"
		};

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeTrue();
		response.EntitySchemaName.Should().Be("UsrDemoEntity");
		response.EntitySchemaUId.Should().Be("cccccccc-cccc-cccc-cccc-cccccccccccc");
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(s => s.Contains("dependsOn") && s.Contains("cccccccc-cccc-cccc-cccc-cccccccccccc")));
	}

	[Test]
	public void TryCreatePage_Fails_When_Entity_Schema_Missing() {
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_BlankPage",
			Template = TemplateName,
			PackageName = "Custom",
			EntitySchemaName = "Ghost"
		};

		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("Ghost").And.Contain("not found");
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>());
	}

	[Test]
	[Description("Seeds the supplied optional-properties into the SaveSchema optionalProperties array.")]
	public void TryCreatePage_Seeds_Optional_Properties_Into_SaveSchema_Payload() {
		// Arrange
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		string savePayload = null;
		_applicationClient.When(client => client.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()))
			.Do(call => savePayload = call.ArgAt<string>(1));
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_Dashboard",
			Template = TemplateName,
			PackageName = "Custom",
			OptionalProperties = """[{"key":"DashboardsEntitySchemaName","value":"Contact"},{"key":"DashboardsElementName","value":"Dashboards"}]"""
		};

		// Act
		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		// Assert
		result.Should().BeTrue(response.Error);
		JArray optionalProperties = (JArray)JObject.Parse(savePayload)["optionalProperties"];
		optionalProperties.Select(item => item["key"]!.ToString())
			.Should().BeEquivalentTo(["DashboardsEntitySchemaName", "DashboardsElementName"],
				because: "create-page must seed the caller-supplied optional properties so a dashboard is linked at creation time");
	}

	[Test]
	[Description("Rejects a malformed optional-properties payload before any SaveSchema call.")]
	public void TryCreatePage_Rejects_Malformed_Optional_Properties() {
		// Arrange
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_Dashboard",
			Template = TemplateName,
			PackageName = "Custom",
			OptionalProperties = "{ not-an-array }"
		};

		// Act
		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		// Assert
		result.Should().BeFalse("because a malformed optional-properties payload must fail fast");
		response.Error.Should().Be(PageOptionalPropertiesHelper.InvalidOptionalPropertiesError,
			because: "the failure must use the canonical shared error wording");
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>());
	}

	[Test]
	[Description("Writes an empty optionalProperties array when the caller supplies none.")]
	public void TryCreatePage_Writes_Empty_Optional_Properties_When_None_Supplied() {
		// Arrange
		Queue<string> selectResponses = new([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		string savePayload = null;
		_applicationClient.When(client => client.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()))
			.Do(call => savePayload = call.ArgAt<string>(1));
		PageCreateOptions options = new() {
			SchemaName = "UsrDemo_BlankPage",
			Template = TemplateName,
			PackageName = "Custom"
		};

		// Act
		bool result = _command.TryCreatePage(options, out PageCreateResponse response);

		// Assert
		result.Should().BeTrue(response.Error);
		((JArray)JObject.Parse(savePayload)["optionalProperties"]).Should().BeEmpty(
			because: "omitting optional-properties must preserve the prior empty-array behavior");
	}

	private void StubSelectQueryResponse(string url, string responseJson) {
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>()).Returns(responseJson);
	}
}
