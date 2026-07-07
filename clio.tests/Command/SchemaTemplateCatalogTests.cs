namespace Clio.Tests.Command;

using System;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SchemaTemplateCatalogTests {
	private const string WebTemplatesJson = """
	{
	    "items": [
	        { "groupName": "Page", "imageId": "b61606bf-c802-1e81-ea59-842e9be3f452", "title": "Tabbed Page with Progress Bar", "uId": "7ed23be6-71df-4ad3-b56a-caa77b9881ce", "name": "PageWithTabsAndProgressBarTemplate" },
	        { "groupName": "Page", "imageId": "bb9cbd6d-3b98-4002-a41f-7b70fc9762e5", "title": "Tabbed page with left area", "uId": "3b2e117f-8c6b-4ca5-80a2-7ebb497cddf9", "name": "PageWithTabsFreedomTemplate" },
	        { "groupName": "Page", "imageId": "f9813cc3-2843-4819-982b-61c9f9ec1da9", "title": "Blank page", "uId": "f691e828-0b36-42a7-898f-c337e9af67d0", "name": "BlankPageTemplate" }
	    ],
	    "success": true,
	    "errorInfo": null
	}
	""";

	private const string MobileTemplatesJson = """
	{
	    "items": [
	        { "groupName": "MobilePage", "imageId": "d09bfa76-b8bd-4266-96df-f634693b9648", "title": "Blank page mobile", "uId": "478ab83b-527b-4830-b2b8-2206bb9bf283", "name": "BlankMobilePageTemplate" }
	    ],
	    "success": true,
	    "errorInfo": null
	}
	""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private SchemaTemplateCatalog _catalog;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("/rest/schema.template.api/templates?schemaType=9")
			.Returns("http://test/rest/schema.template.api/templates?schemaType=9");
		_serviceUrlBuilder.Build("/rest/schema.template.api/templates?schemaType=10")
			.Returns("http://test/rest/schema.template.api/templates?schemaType=10");
		_applicationClient.ExecuteGetRequest("http://test/rest/schema.template.api/templates?schemaType=9")
			.Returns(WebTemplatesJson);
		_applicationClient.ExecuteGetRequest("http://test/rest/schema.template.api/templates?schemaType=10")
			.Returns(MobileTemplatesJson);
		_catalog = new SchemaTemplateCatalog(_applicationClient, _serviceUrlBuilder);
	}

	[Test]
	public void GetTemplates_Web_Returns_Parsed_Items() {
		var templates = _catalog.GetTemplates(PageSchemaType.Web);

		templates.Should().HaveCount(4,
			because: "the 3 endpoint items plus the injected BaseDashboardTemplate are returned");
		templates[0].Name.Should().Be("PageWithTabsAndProgressBarTemplate");
		templates[0].SchemaType.Should().Be(9);
		templates[0].GroupName.Should().Be("Page");
	}

	[Test]
	[Description("Injects BaseDashboardTemplate into the web catalog because the platform template endpoint omits it.")]
	public void GetTemplates_Web_Injects_BaseDashboardTemplate_When_Endpoint_Omits_It() {
		// Act
		var templates = _catalog.GetTemplates(PageSchemaType.Web);

		// Assert
		PageTemplateInfo dashboard = templates.SingleOrDefault(t => t.Name == "BaseDashboardTemplate");
		dashboard.Should().NotBeNull(because: "the dashboard template must be reachable even though schema.template.api omits it");
		dashboard!.UId.Should().Be("eb4d4a67-25d8-fcfa-7851-c4c91efb7b9c",
			because: "create-page uses this UId as the dashboard parent; it is the CrtUIPlatform base-schema GUID");
		dashboard.GroupName.Should().Be("DashboardPage",
			because: "create-page stamps this as the schema group, and SysFreedomDashboardQueryExecutor lists a dashboard only when its group is exactly 'DashboardPage'");
		dashboard.SchemaType.Should().Be(9, because: "BaseDashboardTemplate is a web page template");
	}

	[Test]
	[Description("Resolves the injected BaseDashboardTemplate through FindTemplate so create-page can use it.")]
	public void FindTemplate_Resolves_Injected_BaseDashboardTemplate() {
		// Act
		PageTemplateInfo result = _catalog.FindTemplate("BaseDashboardTemplate");

		// Assert
		result.Should().NotBeNull(because: "create-page resolves the template through the same catalog");
		result!.UId.Should().Be("eb4d4a67-25d8-fcfa-7851-c4c91efb7b9c",
			because: "the resolved template must carry the platform dashboard UId");
	}

	[Test]
	[Description("Does not duplicate BaseDashboardTemplate when the endpoint already advertises it.")]
	public void GetTemplates_Web_Does_Not_Duplicate_Dashboard_Template_When_Endpoint_Provides_It() {
		// Arrange — an environment whose endpoint DOES advertise BaseDashboardTemplate.
		_applicationClient.ExecuteGetRequest("http://test/rest/schema.template.api/templates?schemaType=9")
			.Returns("""
			{ "items": [
			    { "groupName": "Dashboard", "title": "Dashboard", "uId": "eb4d4a67-25d8-fcfa-7851-c4c91efb7b9c", "name": "BaseDashboardTemplate" }
			], "success": true, "errorInfo": null }
			""");
		SchemaTemplateCatalog catalog = new(_applicationClient, _serviceUrlBuilder);

		// Act
		var templates = catalog.GetTemplates(PageSchemaType.Web);

		// Assert
		templates.Count(t => t.Name == "BaseDashboardTemplate").Should().Be(1,
			because: "the injection must be deduped when the endpoint already returns the dashboard template");
	}

	[Test]
	public void GetTemplates_Mobile_Returns_Only_Mobile_Items() {
		var templates = _catalog.GetTemplates(PageSchemaType.Mobile);

		templates.Should().ContainSingle();
		templates[0].Name.Should().Be("BlankMobilePageTemplate");
		templates[0].SchemaType.Should().Be(10);
	}

	[Test]
	public void GetTemplates_Default_Returns_Web_And_Mobile_Combined() {
		var templates = _catalog.GetTemplates();

		templates.Should().HaveCount(5,
			because: "3 web endpoint items + injected BaseDashboardTemplate + 1 mobile item");
		templates.Select(t => t.Name).Should().Contain(["BlankPageTemplate", "BlankMobilePageTemplate", "BaseDashboardTemplate"]);
	}

	[Test]
	public void FindTemplate_By_Name_Is_Case_Insensitive() {
		var result = _catalog.FindTemplate("blankpagetemplate");

		result.Should().NotBeNull();
		result!.UId.Should().Be("f691e828-0b36-42a7-898f-c337e9af67d0");
	}

	[Test]
	public void FindTemplate_By_UId_Works() {
		var result = _catalog.FindTemplate("478ab83b-527b-4830-b2b8-2206bb9bf283");

		result.Should().NotBeNull();
		result!.Name.Should().Be("BlankMobilePageTemplate");
	}

	[Test]
	public void FindTemplate_Unknown_Returns_Null() {
		var result = _catalog.FindTemplate("NoSuchTemplate");

		result.Should().BeNull();
	}

	[Test]
	public void FindTemplate_Empty_Input_Returns_Null() {
		var result = _catalog.FindTemplate(string.Empty);

		result.Should().BeNull();
	}

	[Test]
	public void GetTemplates_When_Platform_Reports_Failure_Throws() {
		_applicationClient.ExecuteGetRequest(Arg.Any<string>())
			.Returns("""{"success": false, "errorInfo": { "message": "forbidden" }}""");
		SchemaTemplateCatalog catalog = new(_applicationClient, _serviceUrlBuilder);

		Action act = () => catalog.GetTemplates(PageSchemaType.Web);

		act.Should().Throw<System.InvalidOperationException>().WithMessage("forbidden");
	}

	[Test]
	public void GetTemplates_Cached_Across_Calls() {
		_catalog.GetTemplates(PageSchemaType.Web);
		_catalog.GetTemplates(PageSchemaType.Web);

		_applicationClient.Received(1)
			.ExecuteGetRequest("http://test/rest/schema.template.api/templates?schemaType=9");
	}
}
