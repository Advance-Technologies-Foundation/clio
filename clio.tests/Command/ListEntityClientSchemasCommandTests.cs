namespace Clio.Tests.Command;

using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ListEntityClientSchemasCommandTests {
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string EntityUId = "aaaaaaaa-0000-0000-0000-000000000001";
	private const string SectionUId = "bbbbbbbb-0000-0000-0000-000000000001";
	private const string SectionCardUId = "cccccccc-0000-0000-0000-000000000001";
	private const string EditCardUId = "dddddddd-0000-0000-0000-000000000001";
	private const string MiniPageUId = "eeeeeeee-0000-0000-0000-000000000001";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private ListEntityClientSchemasCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectQueryUrl);
		_command = new ListEntityClientSchemasCommand(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[Test]
	[Description("TryResolve maps sections, edit pages, and mini pages with page kinds using one batched schema metadata query.")]
	public void TryResolve_Should_Map_Page_Roles_And_Batch_Schema_Metadata() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			$$$"""
			{ "success": true, "rows": [{
			  "Caption": "Contacts",
			  "Code": "ContactSection",
			  "SectionSchemaUId": "{{{SectionUId}}}",
			  "CardSchemaUId": "{{{SectionCardUId}}}",
			  "TypeColumnUId": "ffffffff-0000-0000-0000-000000000001"
			}] }
			""",
			$$$"""
			{ "success": true, "rows": [{
			  "TypeColumnValue": "Customer",
			  "CardSchemaUId": "{{{EditCardUId}}}",
			  "MiniPageSchemaUId": "{{{MiniPageUId}}}",
			  "MiniPageModes": "add"
			}] }
			""",
			$$$"""
			{ "success": true, "rows": [
			  { "UId": "{{{SectionUId}}}", "Name": "ContactSectionV2", "ParentName": "BaseModulePageV2" },
			  { "UId": "{{{SectionCardUId}}}", "Name": "ContactPageV2", "ParentName": "PageWithAreaFreedomTemplate" },
			  { "UId": "{{{EditCardUId}}}", "Name": "ContactClassicPage", "ParentName": "BasePageV2" },
			  { "UId": "{{{MiniPageUId}}}", "Name": "ContactMiniPage", "ParentName": "BaseMiniPageTemplate" }
			] }
			""");
		var options = new ListEntityClientSchemasOptions { EntityName = "Contact" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "the entity, role rows, and metadata rows all resolve");
		response.Success.Should().BeTrue(because: "the command should return a successful migration-unit response");
		response.EntityUId.Should().Be(EntityUId, because: "the exact base entity UId drives downstream filters");
		response.Sections.Should().ContainSingle(because: "one SysModule row should produce one section");
		MigrationSectionInfo section = response.Sections.Single();
		section.SectionSchema.Should().Be("ContactSectionV2", because: "section schema metadata is resolved by UId");
		section.CardSchema.Should().Be("ContactPageV2", because: "section card metadata is resolved by UId");
		section.CardSchemaUId.Should().Be(SectionCardUId, because: "the response preserves card UId provenance");
		section.Template.Should().Be("PageWithAreaFreedomTemplate", because: "the template feeds kind classification");
		section.Kind.Should().Be("freedom", because: "sections must be classified like edit pages");
		section.IsTyped.Should().BeTrue(because: "a non-empty type column UId marks a typed section");
		response.EditPages.Should().ContainSingle(because: "one SysModuleEdit row should produce one edit page");
		MigrationEditPageInfo editPage = response.EditPages.Single();
		editPage.CardSchema.Should().Be("ContactClassicPage", because: "edit-page card metadata is resolved by UId");
		editPage.Kind.Should().Be("classic", because: "BasePageV2 is a known Classic template");
		editPage.MiniPageSchema.Should().Be("ContactMiniPage", because: "mini-page metadata is resolved by UId");
		editPage.MiniPageSchemaUId.Should().Be(MiniPageUId, because: "the response preserves mini-page UId provenance");
		editPage.MiniPageTemplate.Should().Be("BaseMiniPageTemplate", because: "mini-page template is surfaced");
		editPage.MiniPageKind.Should().Be("freedom", because: "mini pages must be classified too");
		_applicationClient.Received(4).ExecutePostRequest(SelectQueryUrl, Arg.Any<string>());
	}

	[Test]
	[Description("TryResolve fails closed when entity rows do not include a confirmed base row.")]
	public void TryResolve_Should_Fail_When_Entity_Base_Row_Is_Not_Confirmed() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{ "success": true, "rows": [{ "UId": "bad-row" }] }""");
		var options = new ListEntityClientSchemasOptions { EntityName = "Contact" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeFalse(because: "unknown ExtendParent cannot safely identify the base entity schema");
		response.Success.Should().BeFalse(because: "the command should not continue with an unsafe entity UId");
		response.Error.Should().Contain("ExtendParent=false", because: "the message should explain the missing base-row signal");
		_applicationClient.Received(1).ExecutePostRequest(SelectQueryUrl, Arg.Any<string>());
	}
}
