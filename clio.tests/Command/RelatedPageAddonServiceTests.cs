using System;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.RelatedPages;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RelatedPageAddonServiceTests {

	private static JsonArray PagesOf(AddonSchemaDto dto) =>
		(JsonNode.Parse(dto.MetaData) as JsonObject)?["Pages"] as JsonArray;

	private static (IAddonSchemaDesignerClient client, AddonSchemaDto dto) ClientReturning(string metaData) {
		var dto = new AddonSchemaDto { MetaData = metaData };
		IAddonSchemaDesignerClient client = Substitute.For<IAddonSchemaDesignerClient>();
		client.GetSchema(Arg.Any<AddonGetRequestDto>()).Returns(dto);
		return (client, dto);
	}

	[Test]
	[Description("Default registration on empty metadata adds the page as the single default and runs save/cache/build.")]
	public void UpsertRelatedPage_EmptyMetadataDefault_AddsSingleDefaultPage() {
		(IAddonSchemaDesignerClient client, AddonSchemaDto dto) = ClientReturning(string.Empty);
		var service = new RelatedPageAddonService(client);
		var pageUId = Guid.NewGuid();

		service.UpsertRelatedPage(new AddonGetRequestDto { AddonName = "MobileRelatedPage" }, pageUId, isDefault: true);

		JsonArray pages = PagesOf(dto);
		pages.Should().ContainSingle();
		pages[0]!["PageSchemaUId"]!.ToString().Should().Be(pageUId.ToString());
		pages[0]!["IsDefault"]!.GetValue<bool>().Should().BeTrue();
		client.Received(1).SaveSchema(dto);
		client.Received(1).ResetClientScriptCache();
		client.Received(1).BuildConfiguration();
	}

	[Test]
	[Description("Works the same for the web RelatedPage add-on (add-on kind is carried by the request).")]
	public void UpsertRelatedPage_WebAddon_AddsDefaultPage() {
		(IAddonSchemaDesignerClient client, AddonSchemaDto dto) = ClientReturning(string.Empty);
		var service = new RelatedPageAddonService(client);
		var pageUId = Guid.NewGuid();

		service.UpsertRelatedPage(new AddonGetRequestDto { AddonName = "RelatedPage" }, pageUId, isDefault: true);

		PagesOf(dto)[0]!["PageSchemaUId"]!.ToString().Should().Be(pageUId.ToString());
		client.Received(1).GetSchema(Arg.Is<AddonGetRequestDto>(r => r.AddonName == "RelatedPage"));
	}

	[Test]
	[Description("Setting a new default clears IsDefault on the previously default page and appends the new one.")]
	public void UpsertRelatedPage_ExistingDefault_ClearsOldAndAddsNew() {
		var oldPage = Guid.NewGuid();
		string meta = $$"""
			{ "Pages": [ { "UId": "{{Guid.NewGuid()}}", "PageSchemaUId": "{{oldPage}}", "IsDefault": true } ] }
			""";
		(IAddonSchemaDesignerClient client, AddonSchemaDto dto) = ClientReturning(meta);
		var service = new RelatedPageAddonService(client);
		var newPage = Guid.NewGuid();

		service.UpsertRelatedPage(new AddonGetRequestDto(), newPage, isDefault: true);

		JsonArray pages = PagesOf(dto);
		pages.Should().HaveCount(2);
		pages.OfType<JsonObject>().Single(p => p["PageSchemaUId"]!.ToString() == oldPage.ToString())["IsDefault"]!.GetValue<bool>().Should().BeFalse();
		pages.OfType<JsonObject>().Single(p => p["PageSchemaUId"]!.ToString() == newPage.ToString())["IsDefault"]!.GetValue<bool>().Should().BeTrue();
	}

	[Test]
	[Description("Non-default registration adds the page without changing which page is default.")]
	public void UpsertRelatedPage_NonDefault_KeepsExistingDefault() {
		var existing = Guid.NewGuid();
		string meta = $$"""
			{ "Pages": [ { "UId": "{{Guid.NewGuid()}}", "PageSchemaUId": "{{existing}}", "IsDefault": true } ] }
			""";
		(IAddonSchemaDesignerClient client, AddonSchemaDto dto) = ClientReturning(meta);
		var service = new RelatedPageAddonService(client);
		var extraPage = Guid.NewGuid();

		service.UpsertRelatedPage(new AddonGetRequestDto(), extraPage, isDefault: false);

		JsonArray pages = PagesOf(dto);
		pages.Should().HaveCount(2);
		pages.OfType<JsonObject>().Single(p => p["PageSchemaUId"]!.ToString() == existing.ToString())["IsDefault"]!.GetValue<bool>().Should().BeTrue(because: "the existing default is untouched");
		pages.OfType<JsonObject>().Single(p => p["PageSchemaUId"]!.ToString() == extraPage.ToString())["IsDefault"]!.GetValue<bool>().Should().BeFalse();
	}

	[Test]
	[Description("When the page is already present, default registration marks it default in place (no duplicate).")]
	public void UpsertRelatedPage_PageAlreadyPresent_MarksDefaultInPlace() {
		var pageUId = Guid.NewGuid();
		string meta = $$"""
			{ "Pages": [ { "UId": "{{Guid.NewGuid()}}", "PageSchemaUId": "{{pageUId}}", "IsDefault": false } ] }
			""";
		(IAddonSchemaDesignerClient client, AddonSchemaDto dto) = ClientReturning(meta);
		var service = new RelatedPageAddonService(client);

		service.UpsertRelatedPage(new AddonGetRequestDto(), pageUId, isDefault: true);

		JsonArray pages = PagesOf(dto);
		pages.Should().ContainSingle(because: "the existing page is reused, not duplicated");
		pages[0]!["IsDefault"]!.GetValue<bool>().Should().BeTrue();
	}

	[Test]
	[Description("An empty page schema UId is rejected before any service call.")]
	public void UpsertRelatedPage_EmptyPageUId_Throws() {
		(IAddonSchemaDesignerClient client, _) = ClientReturning(string.Empty);
		var service = new RelatedPageAddonService(client);

		Action act = () => service.UpsertRelatedPage(new AddonGetRequestDto(), Guid.Empty, isDefault: true);

		act.Should().Throw<ArgumentException>();
		client.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("UpsertPage keeps exactly one default across repeated default calls with different pages.")]
	public void UpsertPage_SwitchesDefaultDeterministically() {
		var pageA = Guid.NewGuid();
		var pageB = Guid.NewGuid();
		var pages = new JsonArray();

		RelatedPageAddonService.UpsertPage(pages, pageA, isDefault: true);
		RelatedPageAddonService.UpsertPage(pages, pageB, isDefault: true);

		pages.Should().HaveCount(2);
		pages.OfType<JsonObject>().Count(p => p["IsDefault"]!.GetValue<bool>()).Should().Be(1);
		pages.OfType<JsonObject>().Single(p => p["IsDefault"]!.GetValue<bool>())["PageSchemaUId"]!.ToString().Should().Be(pageB.ToString());
	}
}
