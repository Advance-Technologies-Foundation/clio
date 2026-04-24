namespace Clio.Tests.Command;

using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageCreateCommandTests {
	private const string TestBase = "http://test";
	private const string TemplateUId = "f691e828-0b36-42a7-898f-c337e9af67d0";
	private const string TemplateName = "BlankPageTemplate";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string SaveSchemaUrl = TestBase + "/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
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
		_catalog.FindTemplate(TemplateName).Returns(new PageTemplateInfo {
			UId = TemplateUId,
			Name = TemplateName,
			Title = "Blank page",
			GroupName = "Page",
			SchemaType = 9
		});
		_command = new PageCreateCommand(_applicationClient, _serviceUrlBuilder, _catalog, _logger);
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
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl, Arg.Is<string>(s => s.Contains(TemplateUId)));
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

	private void StubSelectQueryResponse(string url, string responseJson) {
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>()).Returns(responseJson);
	}
}
