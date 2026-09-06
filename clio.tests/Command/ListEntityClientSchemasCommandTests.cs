namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.EntitySchema;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
internal class ListEntityClientSchemasCommandTests : BaseCommandTests<ListEntityClientSchemasOptions> {
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string EntityUId = "aaaaaaaa-0000-0000-0000-000000000001";
	private const string SectionUId = "bbbbbbbb-0000-0000-0000-000000000001";
	private const string SectionCardUId = "cccccccc-0000-0000-0000-000000000001";
	private const string EditCardUId = "dddddddd-0000-0000-0000-000000000001";
	private const string MiniPageUId = "eeeeeeee-0000-0000-0000-000000000001";
	private const string TypeColumnUId = "11111111-0000-0000-0000-000000000001";
	private const string TypeValueGuid = "22222222-0000-0000-0000-000000000002";
	private const string TypeReferenceSchema = "ActivityType";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private IRuntimeEntitySchemaReader _runtimeEntitySchemaReader;
	private ILookupDefaultDisplayValueResolver _lookupDisplayValueResolver;
	private ListEntityClientSchemasCommand _command;

	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectQueryUrl);
		_command = Container.GetRequiredService<ListEntityClientSchemasCommand>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_runtimeEntitySchemaReader.ClearReceivedCalls();
		_lookupDisplayValueResolver.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		_lookupDisplayValueResolver = Substitute.For<ILookupDefaultDisplayValueResolver>();
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_runtimeEntitySchemaReader);
		containerBuilder.AddSingleton(_lookupDisplayValueResolver);
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
	[Description("TryResolve succeeds with an explanatory Note when a confirmed entity has no Classic section or edit-page rows.")]
	public void TryResolve_Should_Populate_Note_When_No_Page_Roles_Found() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			"""{ "success": true, "rows": [] }""",
			"""{ "success": true, "rows": [] }""");
		var options = new ListEntityClientSchemasOptions { EntityName = "Contact" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "a resolvable entity with no Classic UI roles is still a successful lookup, not a failure");
		response.Success.Should().BeTrue(because: "the command distinguishes an empty page-role graph from an error");
		response.Sections.Should().BeEmpty(because: "no SysModule rows matched the entity");
		response.EditPages.Should().BeEmpty(because: "no SysModuleEdit rows matched the entity");
		response.Note.Should().Contain("No SysModule sections or SysModuleEdit pages matched this entity",
			because: "an empty result must warn the caller it is not the same as 'nothing to migrate'");
		_applicationClient.Received(3).ExecutePostRequest(SelectQueryUrl, Arg.Any<string>());
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

	[Test]
	[Description("TryResolve fails with the DataService reason when a SelectQuery returns an errorInfo-only failure envelope (no success:false), not a misleading empty result.")]
	public void TryResolve_Should_Fail_When_SelectQuery_Returns_ErrorInfo_Only_Failure() {
		// Arrange - a restricted SysSchema read returns HTTP 200 with an errorInfo object and NO success:false;
		// the shared DataServiceSelectResponse detector must classify it as a failure, so the whole resolve fails
		// rather than reading zero rows and reporting a bogus "not found".
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{ "errorInfo": { "errorCode": "AccessDenied", "message": "Access to SysSchema is denied" } }""");
		var options = new ListEntityClientSchemasOptions { EntityName = "Contact" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeFalse(because: "an errorInfo-only failure envelope is a failure, not an empty entity lookup");
		response.Success.Should().BeFalse(because: "the command must not continue on a DataService failure");
		response.Error.Should().Contain("Access to SysSchema is denied",
			because: "the real DataService reason must surface instead of a misleading not-found message");
	}

	[Test]
	[Description("TryResolve still succeeds but surfaces an entity-cap warning when the entity lookup returns exactly the rowCount cap (50) rows.")]
	public void TryResolve_Should_Warn_When_Entity_Lookup_Hits_RowCount_Cap() {
		// Arrange - entity lookup (call 1) returns exactly 50 rows with a confirmed base row so resolution still succeeds;
		// sections (call 2) and edit pages (call 3) stay below their caps.
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			BuildEntityRowsResponse(50),
			BuildRowsResponse(0),
			BuildRowsResponse(0));
		var options = new ListEntityClientSchemasOptions { EntityName = "Contact" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "a confirmed base row still resolves the entity even at the lookup cap");
		response.Success.Should().BeTrue(because: "hitting the entity cap is a non-fatal warning, not a failure");
		response.Warnings.Should().Contain(
			"Entity schema lookup reached the rowCount cap (50); verify the entity result before using it.",
			because: "an entity lookup at the cap may be truncated and must be flagged to the caller");
	}

	[Test]
	[Description("TryResolve surfaces a section-cap warning when the section lookup returns exactly the rowCount cap (100) rows.")]
	public void TryResolve_Should_Warn_When_Section_Lookup_Hits_RowCount_Cap() {
		// Arrange - entity (call 1) resolves normally below its cap; sections (call 2) return exactly 100 rows;
		// edit pages (call 3) stay below their cap. Section rows carry no schema UIds, so no metadata batch call is made.
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			BuildRowsResponse(100),
			BuildRowsResponse(0));
		var options = new ListEntityClientSchemasOptions { EntityName = "Contact" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "a section lookup at its cap is still a successful resolve");
		response.Success.Should().BeTrue(because: "hitting the section cap is a non-fatal warning, not a failure");
		response.Warnings.Should().Contain(
			"Section lookup reached the rowCount cap (100); the section list may be truncated.",
			because: "a section list at the cap may be truncated and must be flagged to the caller");
	}

	[Test]
	[Description("TryResolve surfaces an edit-page-cap warning when the edit-page lookup returns exactly the rowCount cap (100) rows.")]
	public void TryResolve_Should_Warn_When_EditPage_Lookup_Hits_RowCount_Cap() {
		// Arrange - entity (call 1) resolves normally below its cap; sections (call 2) stay below their cap;
		// edit pages (call 3) return exactly 100 rows carrying no schema UIds, so no metadata batch call is made.
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			BuildRowsResponse(0),
			BuildRowsResponse(100));
		var options = new ListEntityClientSchemasOptions { EntityName = "Contact" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "an edit-page lookup at its cap is still a successful resolve");
		response.Success.Should().BeTrue(because: "hitting the edit-page cap is a non-fatal warning, not a failure");
		response.Warnings.Should().Contain(
			"Edit-page lookup reached the rowCount cap (100); the edit-page list may be truncated.",
			because: "an edit-page list at the cap may be truncated and must be flagged to the caller");
	}

	[Test]
	[Description("TryResolve leaves Warnings null on the happy path when every lookup returns fewer rows than its cap.")]
	public void TryResolve_Should_Not_Populate_Warnings_When_All_Lookups_Are_Below_Cap() {
		// Arrange - every lookup (entity, sections, edit pages) returns a below-cap row count.
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			BuildRowsResponse(0),
			BuildRowsResponse(0));
		var options = new ListEntityClientSchemasOptions { EntityName = "Contact" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "a fully below-cap resolve is the normal happy path");
		response.Success.Should().BeTrue(because: "no lookup failed and the entity resolved");
		response.Warnings.Should().BeNull(because: "no lookup reached its rowCount cap, so no warning should be emitted");
	}

	[Test]
	[Description("TryResolve sets a per-type edit page's TypeColumnDisplayValue by resolving its Type-lookup GUID to a display name for a typed entity.")]
	public void TryResolve_Should_Resolve_Type_Display_Name_For_Typed_Edit_Page() {
		// Arrange - entity (call 1) resolves; sections (call 2) empty; edit pages (call 3) carry one per-type page
		// whose TypeColumnValue is a GUID and whose SysModuleEntity.TypeColumnUId identifies the Type column. The row
		// carries no card/mini schema UIds, so no metadata batch query is made (3 SelectQuery calls total).
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			"""{ "success": true, "rows": [] }""",
			$$$"""
			{ "success": true, "rows": [{
			  "TypeColumnValue": "{{{TypeValueGuid}}}",
			  "TypeColumnUId": "{{{TypeColumnUId}}}"
			}] }
			""");
		_runtimeEntitySchemaReader.GetByName("Activity").Returns(SchemaWithTypeColumn(TypeReferenceSchema));
		_lookupDisplayValueResolver
			.Resolve(TypeReferenceSchema, Guid.Parse(TypeValueGuid), Arg.Any<RemoteCommandOptions>())
			.Returns(new LookupDefaultResolution("Task", null));
		var options = new ListEntityClientSchemasOptions { EntityName = "Activity" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "a resolvable typed entity is a successful lookup");
		MigrationEditPageInfo editPage = response.EditPages.Should()
			.ContainSingle(because: "one SysModuleEdit row should produce one edit page").Which;
		editPage.TypeColumnValue.Should().Be(TypeValueGuid,
			because: "the raw type GUID is preserved alongside the resolved display name");
		editPage.TypeColumnDisplayValue.Should().Be("Task",
			because: "the tool resolves the Type-lookup GUID to its caption so the plan can name the type without a separate read");
	}

	[Test]
	[Description("TryResolve leaves TypeColumnDisplayValue null when the resolver returns no display value (fail-soft marker), so the engine renders the GUID fallback.")]
	public void TryResolve_Should_Leave_Type_Display_Name_Null_When_Unresolved() {
		// Arrange - same typed shape, but the resolver reports an honest not-found marker with no display value.
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			"""{ "success": true, "rows": [] }""",
			$$$"""
			{ "success": true, "rows": [{
			  "TypeColumnValue": "{{{TypeValueGuid}}}",
			  "TypeColumnUId": "{{{TypeColumnUId}}}"
			}] }
			""");
		_runtimeEntitySchemaReader.GetByName("Activity").Returns(SchemaWithTypeColumn(TypeReferenceSchema));
		_lookupDisplayValueResolver
			.Resolve(TypeReferenceSchema, Guid.Parse(TypeValueGuid), Arg.Any<RemoteCommandOptions>())
			.Returns(new LookupDefaultResolution(null, "not-found-or-no-access"));
		var options = new ListEntityClientSchemasOptions { EntityName = "Activity" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "an unresolved type name is not a failure, only a missing readability aid");
		MigrationEditPageInfo editPage = response.EditPages.Single();
		editPage.TypeColumnValue.Should().Be(TypeValueGuid, because: "the raw type GUID is still returned for the engine fallback");
		editPage.TypeColumnDisplayValue.Should().BeNull(
			because: "with no resolvable display value the field stays null and the engine shows the GUID + resolve-on-stand note");
	}

	[Test]
	[Description("TryResolve resolves no type display names — and issues no schema-reader or lookup-resolver call — when the entity is not typed (the edit page has no per-type GUID value).")]
	public void TryResolve_Should_Not_Resolve_Type_Display_Name_When_Not_Typed() {
		// Arrange - a single default edit page with an empty type value: nothing to resolve, so enrichment must stay a
		// pure no-op that never touches the schema reader or the lookup resolver.
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			"""{ "success": true, "rows": [] }""",
			"""{ "success": true, "rows": [{ "TypeColumnValue": "" }] }""");
		var options = new ListEntityClientSchemasOptions { EntityName = "Activity" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "a non-typed entity resolves normally");
		response.EditPages.Single().TypeColumnDisplayValue.Should().BeNull(
			because: "a default (non-per-type) page has no type to name");
		_runtimeEntitySchemaReader.DidNotReceive().GetByName(Arg.Any<string>());
		_lookupDisplayValueResolver.DidNotReceive().Resolve(
			Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("TryResolve still succeeds (GUID-only) and never calls the lookup resolver when the schema reader throws while resolving the Type-column reference schema.")]
	public void TryResolve_Should_Degrade_To_Guid_Only_When_Schema_Reader_Throws() {
		// Arrange - a typed page, but the schema reader fails to yield the Type column's reference schema.
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{EntityUId}}}", "ExtendParent": false }] }""",
			"""{ "success": true, "rows": [] }""",
			$$$"""
			{ "success": true, "rows": [{
			  "TypeColumnValue": "{{{TypeValueGuid}}}",
			  "TypeColumnUId": "{{{TypeColumnUId}}}"
			}] }
			""");
		_runtimeEntitySchemaReader.GetByName("Activity")
			.Returns(_ => throw new System.InvalidOperationException("schema read denied"));
		var options = new ListEntityClientSchemasOptions { EntityName = "Activity" };

		// Act
		bool result = _command.TryResolve(options, out ListEntityClientSchemasResponse response);

		// Assert
		result.Should().BeTrue(because: "type-name enrichment is best-effort and must never fail the page-role resolve");
		response.Success.Should().BeTrue(because: "a reader fault degrades to GUID-only, not an error envelope");
		response.EditPages.Single().TypeColumnDisplayValue.Should().BeNull(
			because: "with no resolvable reference schema the field stays null");
		_lookupDisplayValueResolver.DidNotReceive().Resolve(
			Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains("Could not resolve type display names")));
	}

	private static RuntimeEntitySchemaResult SchemaWithTypeColumn(string referenceSchemaName) =>
		new(
			UId: Guid.NewGuid(),
			Name: "Activity",
			PrimaryColumnUId: Guid.NewGuid(),
			PrimaryDisplayColumnName: "Title",
			PrimaryDisplayColumnUId: null,
			Columns: new List<RuntimeEntitySchemaColumnResult> {
				new(
					UId: Guid.Parse(TypeColumnUId),
					Name: "Type",
					Caption: "Type",
					Description: null,
					DataValueType: 10,
					IsRequired: false,
					IsInherited: false,
					ReferenceSchemaName: referenceSchemaName)
			});

	private static string BuildEntityRowsResponse(int count) {
		var rows = new JArray {
			new JObject { ["UId"] = EntityUId, ["ExtendParent"] = false }
		};
		for (int i = 1; i < count; i++) {
			rows.Add(new JObject { ["UId"] = $"aaaaaaaa-0000-0000-0000-{i:D12}", ["ExtendParent"] = true });
		}
		return new JObject { ["success"] = true, ["rows"] = rows }.ToString();
	}

	private static string BuildRowsResponse(int count) {
		var rows = new JArray();
		for (int i = 0; i < count; i++) {
			rows.Add(new JObject());
		}
		return new JObject { ["success"] = true, ["rows"] = rows }.ToString();
	}
}
