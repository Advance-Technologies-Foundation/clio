namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RelatedPageAddonServiceTests {
	private const string Base = "http://test";
	private const string SelectQueryUrl = Base + "/DataService/json/SyncReply/SelectQuery";
	private const string PackageUId = "aa000000-0000-0000-0000-000000000001";
	private const string EntityUId = "bb000000-0000-0000-0000-000000000002";
	private const string PageAUId = "cc000000-0000-0000-0000-00000000000a";
	private const string PageBUId = "cc000000-0000-0000-0000-00000000000b";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IAddonSchemaDesignerClient _addonSchemaDesignerClient;
	private IRemoteEntitySchemaDesignerClient _entitySchemaDesignerClient;
	private ILogger _logger;
	private RelatedPageAddonService _service;
	private AddonSchemaDto _savedSchema;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_addonSchemaDesignerClient = Substitute.For<IAddonSchemaDesignerClient>();
		_entitySchemaDesignerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto { MetaData = """{"Pages":[],"TypeColumnUId":null}""" });
		_addonSchemaDesignerClient
			.When(client => client.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(callInfo => _savedSchema = callInfo.Arg<AddonSchemaDto>());
		// The object (entity schema) is resolved through the entity schema designer, not a SelectQuery.
		StubEntitySchema(new EntityDesignSchemaDto { UId = Guid.Parse(EntityUId), Name = "UsrDeliveryItem" });
		_service = new RelatedPageAddonService(
			_applicationClient, _serviceUrlBuilder, _addonSchemaDesignerClient, _entitySchemaDesignerClient, _logger);
	}

	private void StubEntitySchema(EntityDesignSchemaDto schema) =>
		_entitySchemaDesignerClient
			.GetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(), Arg.Any<RemoteCommandOptions>())
			// Fully qualified: DesignerResponse exists in both Clio.Command and Clio.Command.EntitySchemaDesigner.
			.Returns(new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Success = true, Schema = schema
			});

	private void StubSelectQueue(params string[] responses) {
		Queue<string> queue = new(responses);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => queue.Dequeue());
	}

	private static string Rows(string uId) => $$"""{"success": true, "rows": [{"UId": "{{uId}}"}]}""";

	// SysSchema rows for the reverse page-UId -> name lookup expose the "Name" column.
	private static string NameRow(string name) => $$"""{"success": true, "rows": [{"Name": "{{name}}"}]}""";

	private void StubAddonMetadata(string metaData) =>
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto { MetaData = metaData });

	private static RelatedPageAddonRequest Request(params RelatedPageSpec[] pages) =>
		new("Custom", "UsrDeliveryItem", pages, null);

	[Test]
	[Description("Saves the RelatedPage add-on for a default+add page set and rebuilds static content so the change is visible.")]
	public void Create_ShouldSaveAddonAndRebuildConfiguration_WhenDefaultAndAddPagesProvided() {
		// Arrange
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act
		RelatedPageAddonResult result = _service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemAddPage", IsAdd: true)));

		// Assert
		result.EntitySchemaUId.Should().Be(EntityUId,
			because: "the result reports the resolved object schema UId");
		result.PackageUId.Should().Be(PackageUId,
			because: "the result reports the resolved package UId");
		result.PageCount.Should().Be(2,
			because: "both requested page entries were written");
		result.AddonName.Should().Be("RelatedPage",
			because: "the configured add-on is the RelatedPage add-on");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
		_addonSchemaDesignerClient.Received(1).ResetClientScriptCache();
		_addonSchemaDesignerClient.Received(1).BuildConfiguration();
	}

	[Test]
	[Description("Requests the RelatedPage add-on for the resolved object with the EntitySchemaManager target and full hierarchy.")]
	public void Create_ShouldRequestRelatedPageAddonForResolvedObject_WhenInvoked() {
		// Arrange
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		_service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		_addonSchemaDesignerClient.Received(1).GetSchema(Arg.Is<AddonGetRequestDto>(request =>
			request.AddonName == "RelatedPage"
			&& request.TargetSchemaManagerName == "EntitySchemaManager"
			&& request.UseFullHierarchy
			&& request.TargetSchemaUId == Guid.Parse(EntityUId)
			&& request.TargetPackageUId == Guid.Parse(PackageUId)
			&& request.TargetParentSchemaUId == Guid.Empty));
	}

	[Test]
	[Description("Writes the default and add page entries (with the correct IsDefault and Actions.Add flags) into the saved metadata.")]
	public void Create_ShouldWriteDefaultAndAddPageFlagsIntoMetadata_WhenBothProvided() {
		// Arrange
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act
		_service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemAddPage", IsAdd: true)));

		// Assert
		_savedSchema.Should().NotBeNull(
			because: "the add-on schema must be saved");
		JsonArray pages = JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray();
		pages.Count.Should().Be(2,
			because: "both page entries are written into the metadata");
		pages[0]!["PageSchemaUId"]!.GetValue<string>().Should().Be(PageAUId,
			because: "the first entry resolves to the default page's schema UId");
		pages[0]!["IsDefault"]!.GetValue<bool>().Should().BeTrue(
			because: "the first entry is the default page");
		pages[0]!["Actions"]!["Add"]!.GetValue<bool>().Should().BeFalse(
			because: "the default page is not the add page in this request");
		pages[1]!["PageSchemaUId"]!.GetValue<string>().Should().Be(PageBUId,
			because: "the second entry resolves to the add page's schema UId");
		pages[1]!["IsDefault"]!.GetValue<bool>().Should().BeFalse(
			because: "the add page is not the default page in this request");
		pages[1]!["Actions"]!["Add"]!.GetValue<bool>().Should().BeTrue(
			because: "the second entry is the add page");
	}

	[Test]
	[Description("Stores an explicit (known-audience) role UId — the portal role layered over the role-less base — and the top-level type-column UId into the saved metadata.")]
	public void Create_ShouldStoreRoleAndTypeColumnIntoMetadata_WhenProvided() {
		// Arrange — the portal role's fixed seeded UId (a supported audience, so no SysAdminUnit lookup is issued).
		const string portalRoleUId = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";
		const string typeColumnUId = "dd000000-0000-0000-0000-000000000003";
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act — a role-less base default plus a portal-scoped default layered on top (distinct audiences).
		_service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemPortalPage", IsDefault: true, Role: portalRoleUId)
		}, typeColumnUId));

		// Assert
		JsonNode metadata = JsonNode.Parse(_savedSchema.MetaData)!;
		metadata["TypeColumnUId"]!.GetValue<string>().Should().Be(typeColumnUId,
			because: "the type column UId is stored at the top of the metadata");
		JsonArray pages = metadata["Pages"]!.AsArray();
		pages[0]!["Role"].Should().BeNull(
			because: "the base default is role-less");
		pages[1]!["Role"]!.GetValue<string>().Should().Be(portalRoleUId,
			because: "an explicit (known-audience) role UId is written verbatim onto the role-scoped page entry");
	}

	[Test]
	[Description("Fails before touching the add-on when the package cannot be resolved.")]
	public void Create_ShouldThrowWithoutTouchingAddon_WhenPackageNotFound() {
		// Arrange
		StubSelectQueue("""{"success": true, "rows": []}""");

		// Act
		Action act = () => _service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*not found*",
			because: "an unresolved package must fail fast");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Fails before touching the add-on when the entity schema designer reports no such object.")]
	public void Create_ShouldThrowWithoutTouchingAddon_WhenObjectNotFound() {
		// Arrange — package resolves, then the entity schema designer returns no schema.
		StubSelectQueue(Rows(PackageUId));
		StubEntitySchema(null);

		// Act
		Action act = () => _service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*UsrDeliveryItem*not found*",
			because: "an object that does not resolve in the package must fail with a clear message");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Fails without saving when a page schema name cannot be resolved.")]
	public void Create_ShouldThrowWithoutSaving_WhenPageNotFound() {
		// Arrange — package + object resolve, then the page lookup returns no rows.
		StubSelectQueue(Rows(PackageUId), """{"success": true, "rows": []}""");

		// Act
		Action act = () => _service.Create(Request(new RelatedPageSpec("UsrMissingPage", IsDefault: true)));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*UsrMissingPage*not found*",
			because: "an unresolved page name must fail with a clear message naming the page");
		// Page resolution happens before the add-on round-trip, so neither GetSchema nor SaveSchema runs.
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Accepts an explicitly empty pages set as a reset-to-inline: writes an empty Pages configuration (the effective delete) and saves, with no base-default error.")]
	public void Create_ShouldWriteEmptyConfiguration_WhenPagesEmpty() {
		// Arrange — only the package is resolved; an empty set triggers no page or role lookups.
		StubSelectQueue(Rows(PackageUId));

		// Act
		RelatedPageAddonResult result = _service.Create(Request());

		// Assert
		result.PageCount.Should().Be(0,
			because: "an explicitly empty page set clears all bindings (reset to inline)");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
		JsonArray pages = JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray();
		pages.Count.Should().Be(0,
			because: "reset-to-inline writes an empty Pages array — the effective delete");
	}

	[Test]
	[Description("A reset-to-inline (empty pages) drops any supplied type-column-uid — even a malformed one — instead of persisting an unvalidated value; an empty configuration has no typed page sets.")]
	public void Create_ShouldDropTypeColumnUId_WhenPagesEmpty() {
		// Arrange — only the package is resolved for an empty set.
		StubSelectQueue(Rows(PackageUId));

		// Act — empty pages with a malformed type-column-uid (the empty-clear path skips the GUID guard, so the
		// value must be dropped here rather than written into the metadata).
		_service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem",
			Array.Empty<RelatedPageSpec>(), "not-a-guid"));

		// Assert
		JsonNode metadata = JsonNode.Parse(_savedSchema.MetaData)!;
		metadata["TypeColumnUId"].Should().BeNull(
			because: "a reset-to-inline has no typed sets, so a supplied (even malformed) type-column-uid is dropped, not persisted");
	}

	[Test]
	[Description("Rejects an add-only configuration (no is-default page) before any remote call, because every binding needs a base default record page.")]
	public void Create_ShouldThrow_WhenNoBaseDefaultPageProvided() {
		// Arrange / Act
		Action act = () => _service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemAddPage", IsAdd: true)));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*base default*",
			because: "an add-only configuration leaves records with no page to open");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects a typed-only configuration (every is-default page carries a type-column-value) so record types without a dedicated set still have an untyped fallback page.")]
	public void Create_ShouldThrow_WhenOnlyTypedDefaultPagesProvided() {
		// Arrange
		const string typeColumnUId = "af280321-e749-41dd-98e5-383906747e29";
		const string typeValue = "1b0bc159-150a-e111-a31b-00155d04c01d";

		// Act
		Action act = () => _service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: typeValue)
		}, typeColumnUId));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*base default*",
			because: "a typed-only configuration has no untyped fallback for record types without a dedicated set");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects a per-page type-column-value when no top-level type-column-uid is provided, since a typed page with no type column can never be matched to a record type.")]
	public void Create_ShouldThrow_WhenTypeColumnValueHasNoTypeColumnUid() {
		// Arrange
		const string typeValue = "1b0bc159-150a-e111-a31b-00155d04c01d";

		// Act — a valid base default is present (passes the base-default check), but a typed entry has no type-column-uid.
		Action act = () => _service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true),
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: typeValue)
		}, null));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*type-column-uid*",
			because: "a per-page type-column-value is unmatchable without the type column it keys on");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects a custom role NAME (outside the two supported audiences) before any remote call: the designer produces no such audience and runtime support for it is unverified.")]
	public void Create_ShouldRejectCustomRoleName_WhenNotAKnownAudience() {
		// Act — a role-less base default keeps the base-default check happy so we reach the audience guard.
		Action act = () => _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemRolePage", IsDefault: true, RoleName: "Sales Managers")
		}, null));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*unsupported audience*",
			because: "only the general and portal audiences are supported; a custom role name is rejected");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects a valid-but-unknown role UId (a custom role's GUID, not one of the two supported audiences) before any remote call.")]
	public void Create_ShouldRejectUnknownRoleUid_WhenNotAKnownAudience() {
		// Act — a well-formed GUID that is not the All-employees or All-external-users seeded Id.
		Action act = () => _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemRolePage", IsDefault: true, Role: "11112222-3333-4444-5555-666677778888")
		}, null));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*unsupported audience*",
			because: "a custom role UId is not one of the two supported audiences and must be rejected");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Maps a standard platform role name to its fixed seeded Id without issuing a SysAdminUnit lookup.")]
	public void Create_ShouldMapKnownSystemRoleNameToPlatformId_WithoutQuerying() {
		// Arrange — "All external users" is a standard platform role: it resolves to its fixed seeded Id
		// without a SysAdminUnit lookup, so the only SelectQueries are package and page resolution.
		const string portalRoleUId = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act — a role-less base default plus the portal ("All external users") set layered on top.
		_service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemPortalPage", IsDefault: true, RoleName: "All external users")
		}, null));

		// Assert
		JsonArray pages = JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray();
		pages[1]!["Role"]!.GetValue<string>().Should().Be(portalRoleUId,
			because: "the standard portal role name resolves to its fixed platform Id, not a queried unit with the same name");
	}

	[Test]
	[Description("Flows a replacing/derived object's parent schema UId from the entity designer into the add-on request's TargetParentSchemaUId.")]
	public void Create_ShouldPassParentSchemaUidFromEntityDesigner_WhenObjectIsDerived() {
		// Arrange — a replacing/derived object reports the parent schema it extends.
		const string parentUId = "99990000-0000-0000-0000-00000000000f";
		StubEntitySchema(new EntityDesignSchemaDto {
			UId = Guid.Parse(EntityUId),
			Name = "Case",
			ParentSchema = new EntityDesignSchemaDto { UId = Guid.Parse(parentUId), Name = "BaseCase" }
		});
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		_service.Create(Request(new RelatedPageSpec("CaseFormPage", IsDefault: true)));

		// Assert
		_addonSchemaDesignerClient.Received(1).GetSchema(Arg.Is<AddonGetRequestDto>(request =>
			request.TargetSchemaUId == Guid.Parse(EntityUId)
			&& request.TargetParentSchemaUId == Guid.Parse(parentUId)));
	}

	[Test]
	[Description("Rejects an explicit role that is not a GUID without saving, rather than persisting a malformed audience.")]
	public void Create_ShouldRejectWithoutSaving_WhenExplicitRoleIsNotAGuid() {
		// Arrange
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		Action act = () => _service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true, Role: "not-a-guid")));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*not a valid SysAdminUnit GUID*",
			because: "an explicit role must be a SysAdminUnit GUID, otherwise the audience would be malformed");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Writes the top-level type-column UId and an untyped default plus a typed per-record-type page entry into the metadata.")]
	public void Create_ShouldWriteTypeColumnAndPerTypePageIntoMetadata_WhenTypedPageProvided() {
		// Arrange — no role names, so the order is package, then one query per page.
		const string typeColumnUId = "af280321-e749-41dd-98e5-383906747e29";
		const string typeValue = "1b0bc159-150a-e111-a31b-00155d04c01d";
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act
		_service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true),
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: typeValue)
		}, typeColumnUId));

		// Assert
		JsonNode metadata = JsonNode.Parse(_savedSchema.MetaData)!;
		metadata["TypeColumnUId"]!.GetValue<string>().Should().Be(typeColumnUId,
			because: "the type column UId is stored once at the top of the related-page metadata");
		JsonArray pages = metadata["Pages"]!.AsArray();
		pages[0]!["TypeColumnValue"].Should().BeNull(
			because: "the first page is the untyped default set (the fallback when a record's type has no set)");
		pages[1]!["TypeColumnValue"]!.GetValue<string>().Should().Be(typeValue,
			because: "the typed page entry must carry the type value record Id it applies to");
	}

	[Test]
	[Description("Resolves a page-schema name only once even when several entries name the same page (the same page used as both default and add).")]
	public void Create_ShouldResolveRepeatedPageNameOnlyOnce_WhenSamePageUsedTwice() {
		// Arrange — one package query and ONE page query must satisfy two entries that share a page name.
		// If the page were re-queried per entry the queue would be exhausted and Dequeue would throw.
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		_service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsAdd: true)));

		// Assert
		JsonArray pages = JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray();
		pages.Count.Should().Be(2,
			because: "both entries are written even though they share a single resolved page UId");
		pages[0]!["PageSchemaUId"]!.GetValue<string>().Should().Be(PageAUId,
			because: "the default entry uses the single resolved page UId");
		pages[1]!["PageSchemaUId"]!.GetValue<string>().Should().Be(PageAUId,
			because: "the add entry reuses the same resolved page UId without a second query");
	}

	[Test]
	[Description("Flows the is-ssp-default flag from the spec into the saved page metadata (true on a marked entry, false by default).")]
	public void Create_ShouldWriteIsSspDefaultFlagIntoMetadata_WhenMarked() {
		// Arrange
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act
		_service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true, IsSspDefault: true),
			new RelatedPageSpec("UsrDeliveryItemAddPage", IsAdd: true)));

		// Assert
		JsonArray pages = JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray();
		pages[0]!["IsSspDefault"]!.GetValue<bool>().Should().BeTrue(
			because: "is-ssp-default must flow from the spec into the saved metadata");
		pages[1]!["IsSspDefault"]!.GetValue<bool>().Should().BeFalse(
			because: "an entry that does not set is-ssp-default defaults to false");
	}

	[Test]
	[Description("Replaces the object's existing related-page configuration wholesale rather than merging into the pages fetched by GetSchema.")]
	public void Create_ShouldReplaceExistingConfiguration_RatherThanMerging() {
		// Arrange — GetSchema returns a pre-existing config (a stale page and a stale type column).
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto {
				MetaData = """{"Pages":[{"UId":"old","PageSchemaUId":"99999999-9999-9999-9999-999999999999","IsDefault":true}],"TypeColumnUId":"88888888-8888-8888-8888-888888888888"}"""
			});
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		_service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		JsonNode metadata = JsonNode.Parse(_savedSchema.MetaData)!;
		JsonArray pages = metadata["Pages"]!.AsArray();
		pages.Count.Should().Be(1,
			because: "the saved configuration is exactly the request's pages, not a merge with the fetched pages");
		pages[0]!["PageSchemaUId"]!.GetValue<string>().Should().Be(PageAUId,
			because: "only the requested page is written; the stale pre-existing page is dropped");
		metadata["TypeColumnUId"].Should().BeNull(
			because: "the request omits type-column-uid, so the stale fetched type column is replaced with null");
	}

	[Test]
	[Description("Preserves an unknown top-level MetaData field on write: a field this tool does not model (e.g. a future platform addition) round-trips instead of being dropped, while the owned Pages/TypeColumnUId keys are still fully replaced.")]
	public void Create_ShouldPreserveUnknownTopLevelMetadataField_OnReplace() {
		// Arrange — the fetched add-on carries an extra top-level key the tool doesn't model, plus a stale page.
		StubAddonMetadata(
			"""{"Pages":[{"UId":"old","PageSchemaUId":"99999999-9999-9999-9999-999999999999","IsDefault":true}],"TypeColumnUId":null,"FutureField":"keepme"}""");
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		_service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		JsonObject metadata = JsonNode.Parse(_savedSchema.MetaData)!.AsObject();
		metadata["FutureField"]!.GetValue<string>().Should().Be("keepme",
			because: "an unknown top-level field must round-trip through the write, not be dropped by the wholesale replace");
		JsonArray writtenPages = metadata["Pages"]!.AsArray();
		writtenPages.Count.Should().Be(1,
			because: "the tool's own Pages key is still fully replaced (the stale entry is dropped)");
		writtenPages[0]!["PageSchemaUId"]!.GetValue<string>().Should().Be(PageAUId,
			because: "only the request's page is written into the owned Pages key");
	}

	[Test]
	[Description("Rejects a non-GUID type-column-uid before any remote call, since the platform can never match a malformed type column.")]
	public void Create_ShouldThrow_WhenTypeColumnUidIsNotAGuid() {
		// Arrange / Act — a valid base default is present, but the type-column-uid is not a GUID.
		Action act = () => _service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true)
		}, "not-a-guid"));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*not a valid GUID*",
			because: "a malformed type-column-uid must be rejected, not persisted into metadata");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects a resolved package UId that is not a GUID before resolving the object or touching the add-on.")]
	public void Create_ShouldThrow_WhenResolvedPackageUidIsNotAGuid() {
		// Arrange — the package query resolves a row whose UId is present but not a GUID.
		StubSelectQueue(Rows("not-a-guid"));

		// Act
		Action act = () => _service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*not a valid GUID*",
			because: "a malformed package UId must fail before any object or add-on call");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects a resolved page UId that is present but not a GUID, without saving the add-on.")]
	public void Create_ShouldThrow_WhenResolvedPageUidIsNotAGuid() {
		// Arrange — package resolves, then the page query resolves a row whose UId is not a GUID.
		StubSelectQueue(Rows(PackageUId), Rows("not-a-guid"));

		// Act
		Action act = () => _service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*not a valid GUID*",
			because: "a malformed page UId must be rejected rather than written into the saved metadata");
		// Page resolution happens before the add-on round-trip, so neither GetSchema nor SaveSchema runs.
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects a page entry whose role and role-name point at DIFFERENT audiences (employee UId + portal name): role would silently win and the role-name's intent would be dropped, so the genuine conflict is surfaced.")]
	public void Create_ShouldThrow_WhenRoleAndRoleNameConflict() {
		// Arrange / Act — role is the employees UId but role-name says the portal audience.
		Action act = () => _service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true,
				Role: "a29a3ba5-4b0d-de11-9a51-005056c00008", RoleName: "All external users")));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*different audiences*",
			because: "role and role-name naming different audiences is a genuine conflict and must be rejected");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Accepts a page that sets BOTH role and role-name when they resolve to the same audience — the shape get-related-page-addon returns — so a verbatim read-modify-write replay is not rejected and role wins.")]
	public void Create_ShouldAccept_WhenRoleAndConsistentRoleNameBothSet() {
		// Arrange — the base default carries both the raw employee UId and its friendly name, exactly as `get` emits.
		const string employees = "a29a3ba5-4b0d-de11-9a51-005056c00008";
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		RelatedPageAddonResult result = _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true, Role: employees, RoleName: "All employees")
		}, null));

		// Assert
		result.PageCount.Should().Be(1,
			because: "role + a consistent role-name (same audience) is the get output shape and must round-trip, not be rejected");
		JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray()[0]!["Role"]!.GetValue<string>().Should().Be(employees,
			because: "when both are set and agree, the explicit role UId wins");
	}

	[Test]
	[Description("End-to-end read-modify-write: entries returned by Get (which carry BOTH the raw role UId and the resolved role-name) feed straight back into Create without hand-editing — the round-trip the tool advertises, previously broken by the both-role-and-role-name guard.")]
	public void GetThenCreate_ShouldRoundTrip_WhenReplayingResolvedEntriesVerbatim() {
		// Arrange — Get decodes an employee default (name reverse-resolved); the entries are then replayed into
		// Create verbatim. Queue: [Get] package + page-name reverse; [Create] package + page-name forward.
		StubAddonMetadata(
			"""{"Pages":[{"UId":"u1","PageSchemaUId":"cc000000-0000-0000-0000-00000000000a","IsDefault":true,"Actions":{"Add":false},"Role":"a29a3ba5-4b0d-de11-9a51-005056c00008"}],"TypeColumnUId":null}""");
		StubSelectQueue(Rows(PackageUId), NameRow("UsrDeliveryItemFormPage"), Rows(PackageUId), Rows(PageAUId));

		// Act — read, then replay the decoded entries (raw role UId + resolved role-name both present) unchanged.
		RelatedPageAddonReadResult read = _service.Get(new RelatedPageAddonReadRequest("Custom", "UsrDeliveryItem"));
		RelatedPageAddonResult written = _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem",
			read.Pages.Select(p => new RelatedPageSpec(
				p.PageSchemaName, p.IsDefault, p.IsAdd, p.IsSspDefault, p.Role, p.TypeColumnValue, p.RoleName)).ToList(),
			read.TypeColumnUId));

		// Assert
		read.Pages[0].Role.Should().NotBeNull(because: "Get returns the raw role UId");
		read.Pages[0].RoleName.Should().Be("All employees", because: "Get also returns the resolved role-name");
		written.PageCount.Should().Be(1,
			because: "the get output (role + consistent role-name) must feed straight back into create without a conflict");
	}

	[Test]
	[Description("Get returns an empty configuration when the object's RelatedPage add-on has no pages.")]
	public void Get_ShouldReturnEmptyConfiguration_WhenObjectHasNoRelatedPages() {
		// Arrange — the default GetSchema stub returns an empty Pages array; only the package is queried.
		StubSelectQueue(Rows(PackageUId));

		// Act
		RelatedPageAddonReadResult result = _service.Get(new RelatedPageAddonReadRequest("Custom", "UsrDeliveryItem"));

		// Assert
		result.EntitySchemaUId.Should().Be(EntityUId,
			because: "the resolved object UId is reported even when there are no pages");
		result.PageCount.Should().Be(0,
			because: "an unconfigured object decodes to no related-page entries");
		result.Pages.Should().BeEmpty(
			because: "the empty Pages metadata yields no entries");
	}

	[Test]
	[Description("Get decodes the page set and reverse-resolves page names and the standard platform role names.")]
	public void Get_ShouldDecodeAndReverseResolvePages_WhenObjectIsConfigured() {
		// Arrange — addon has an employee default+add page and a portal default page, plus a type column.
		StubAddonMetadata(
			"""
			{"Pages":[{"UId":"u1","PageSchemaUId":"cc000000-0000-0000-0000-00000000000a","IsDefault":true,"IsSspDefault":false,"Actions":{"Add":true},"Role":"a29a3ba5-4b0d-de11-9a51-005056c00008","TypeColumnValue":null},{"UId":"u2","PageSchemaUId":"cc000000-0000-0000-0000-00000000000b","IsDefault":true,"IsSspDefault":false,"Actions":{"Add":false},"Role":"720b771c-e7a7-4f31-9cfb-52cd21c3739f","TypeColumnValue":null}],"TypeColumnUId":"af280321-e749-41dd-98e5-383906747e29"}
			""");
		// Resolution order: package, then one page-name reverse-lookup per entry.
		StubSelectQueue(Rows(PackageUId), NameRow("UsrDeliveryItemFormPage"), NameRow("UsrDeliveryItemPortalPage"));

		// Act
		RelatedPageAddonReadResult result = _service.Get(new RelatedPageAddonReadRequest("Custom", "UsrDeliveryItem"));

		// Assert
		result.TypeColumnUId.Should().Be("af280321-e749-41dd-98e5-383906747e29",
			because: "the top-level type column UId is decoded from the metadata");
		result.PageCount.Should().Be(2,
			because: "both page entries are decoded");
		result.Pages[0].PageSchemaUId.Should().Be(PageAUId,
			because: "the raw page UId is preserved for a safe read-modify-write");
		result.Pages[0].PageSchemaName.Should().Be("UsrDeliveryItemFormPage",
			because: "the page UId is reverse-resolved to its schema name");
		result.Pages[0].IsDefault.Should().BeTrue(
			because: "the employee entry is a default page");
		result.Pages[0].IsAdd.Should().BeTrue(
			because: "Actions.Add maps to the is-add flag");
		result.Pages[0].RoleName.Should().Be("All employees",
			because: "the standard employee role UId reverse-resolves to its name");
		result.Pages[1].RoleName.Should().Be("All external users",
			because: "the standard portal role UId reverse-resolves to its name");
		result.Pages[1].IsAdd.Should().BeFalse(
			because: "the portal entry has Actions.Add false");
	}

	[Test]
	[Description("Saves metadata whose only top-level keys are Pages and TypeColumnUId, pinning the verified RelatedPage add-on contract so a full MetaData replace can never silently introduce or drop a sibling field.")]
	public void Create_ShouldWriteOnlyPagesAndTypeColumnUidKeys_PinningTheAddonContract() {
		// Arrange
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		_service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		System.Text.Json.Nodes.JsonObject metadata = JsonNode.Parse(_savedSchema.MetaData)!.AsObject();
		metadata.Select(pair => pair.Key).Should().BeEquivalentTo(new[] { "Pages", "TypeColumnUId" },
			because: "the RelatedPage add-on metadata contract is exactly {Pages, TypeColumnUId}; a full replace must "
				+ "not introduce or drop sibling fields");
	}

	[Test]
	[Description("Serializes a role-less default page's Role as JSON null (the set applies to all users) rather than omitting it or writing an empty string.")]
	public void Create_ShouldSerializeRoleAsNull_WhenDefaultIsRoleLess() {
		// Arrange
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		_service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		JsonArray pages = JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray();
		pages[0]!["Role"].Should().BeNull(
			because: "a role-less page set applies to all users and is written with a null Role");
	}

	[Test]
	[Description("Rejects a portal-only configuration (the only untyped default is scoped to the portal role, with no general base) before any remote call, so the general audience keeps a page to open.")]
	public void Create_ShouldThrow_WhenBaseDefaultIsPortalOnly() {
		// Arrange / Act — a single default scoped to the portal role, with no general (All employees / role-less) base.
		Action act = () => _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemPortalPage", IsDefault: true, RoleName: "All external users")
		}, null));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*base default*",
			because: "a portal-only default leaves the general audience with no page; a general base default is required");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Accepts an 'All employees'-scoped base default (the shape the designer writes) as a valid general base.")]
	public void Create_ShouldAcceptAllEmployeesBaseDefault_AsGeneralBase() {
		// Arrange — "All employees" is a known platform role → no SysAdminUnit query; only package + page.
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId));

		// Act
		RelatedPageAddonResult result = _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true, RoleName: "All employees")
		}, null));

		// Assert
		result.PageCount.Should().Be(1,
			because: "an All employees base default is a valid general base (the designer's shape) and is saved");
		JsonArray pages = JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray();
		pages[0]!["Role"]!.GetValue<string>().Should().Be("a29a3ba5-4b0d-de11-9a51-005056c00008",
			because: "the All employees base is written with its seeded role Id");
	}

	[Test]
	[Description("Rejects two default pages targeting the same audience and type (ambiguous 'which page opens?') before any remote call.")]
	public void Create_ShouldThrow_WhenTwoDefaultsForSameAudienceAndType() {
		// Act — two untyped, role-less (general) defaults: the same (audience, type) cell.
		Action act = () => _service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemOtherPage", IsDefault: true)));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*more than one default page*",
			because: "each (audience, type) cell may have only one default page");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Treats a role-less default and an 'All employees' default as the same general cell, rejecting the pair as duplicate defaults.")]
	public void Create_ShouldThrow_WhenRoleLessAndAllEmployeesDefaultsCollide() {
		// Act — role-less applies to everyone and "All employees" to employees; both untyped defaults would both
		// match an employee, so they normalize to the same general cell.
		Action act = () => _service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemOtherPage", IsDefault: true, RoleName: "All employees")));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*more than one default page*",
			because: "role-less and 'All employees' collapse to one general cell; two defaults there are ambiguous");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects two add pages targeting the same audience and type before any remote call.")]
	public void Create_ShouldThrow_WhenTwoAddPagesForSameAudienceAndType() {
		// Act — a valid base default, then two add pages for the same general cell.
		Action act = () => _service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemAddPage", IsAdd: true),
			new RelatedPageSpec("UsrDeliveryItemOtherAddPage", IsAdd: true)));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*more than one add page*",
			because: "each (audience, type) cell may have only one add page");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Accepts several default pages for the same audience when their type-column-values differ: distinct (audience x type) cells are not duplicates, so the uniqueness guard does not over-reject and all entries are written.")]
	public void Create_ShouldAcceptSameAudienceDistinctTypes_WhenTypeColumnValuesDiffer() {
		// Arrange — an untyped general default plus two typed general defaults with DISTINCT type values.
		const string typeColumnUId = "af280321-e749-41dd-98e5-383906747e29";
		const string typeIncident = "1b0bc159-150a-e111-a31b-00155d04c01d";
		const string typeRequest = "2c0bc159-150a-e111-a31b-00155d04c01e";
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId), Rows(PageAUId));

		// Act
		RelatedPageAddonResult result = _service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true),
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: typeIncident),
			new RelatedPageSpec("CaseRequestPage", IsDefault: true, TypeColumnValue: typeRequest)
		}, typeColumnUId));

		// Assert
		result.PageCount.Should().Be(3,
			because: "the untyped default and two distinct-typed defaults are three separate cells, all valid");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
		JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray().Count.Should().Be(3,
			because: "distinct type-column-values are distinct cells; none is rejected as a duplicate default");
	}

	[Test]
	[Description("Treats the same type-column-value differing only in letter case as one cell: two general defaults for that type value are rejected as a duplicate default (the cell key is case-insensitive, like a lookup GUID).")]
	public void Create_ShouldThrow_WhenSameTypeValueDiffersOnlyInCase() {
		// Arrange
		const string typeColumnUId = "af280321-e749-41dd-98e5-383906747e29";

		// Act — a base default plus two typed defaults whose type value differs ONLY in letter case.
		Action act = () => _service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true),
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: "1B0BC159-150A-E111-A31B-00155D04C01D"),
			new RelatedPageSpec("CaseOtherPage", IsDefault: true, TypeColumnValue: "1b0bc159-150a-e111-a31b-00155d04c01d")
		}, typeColumnUId));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*more than one default page*",
			because: "a type-column-value is case-insensitive, so the two typed defaults are the same cell — a duplicate");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Reverse-resolves a page name only ONCE when several entries share the same PageSchemaUId (the same page reused across a Role x Type matrix), mirroring the write path's dedup — O(distinct pages) lookups, not O(entries).")]
	public void Get_ShouldResolvePageNameOnlyOnce_WhenSamePageUidRepeats() {
		// Arrange — two entries share PageAUId. Only ONE name-lookup row is queued, so if the read re-queried per
		// entry the second Dequeue would throw (queue exhausted) and the test would fail.
		StubAddonMetadata(
			"""{"Pages":[{"UId":"u1","PageSchemaUId":"cc000000-0000-0000-0000-00000000000a","IsDefault":true,"Actions":{"Add":false}},{"UId":"u2","PageSchemaUId":"cc000000-0000-0000-0000-00000000000a","IsDefault":false,"Actions":{"Add":true}}],"TypeColumnUId":null}""");
		StubSelectQueue(Rows(PackageUId), NameRow("UsrDeliveryItemFormPage"));

		// Act
		RelatedPageAddonReadResult result = _service.Get(new RelatedPageAddonReadRequest("Custom", "UsrDeliveryItem"));

		// Assert
		result.PageCount.Should().Be(2,
			because: "both entries decode even though they share one PageSchemaUId");
		result.Pages[0].PageSchemaName.Should().Be("UsrDeliveryItemFormPage",
			because: "the shared page UId resolves to its name");
		result.Pages[1].PageSchemaName.Should().Be("UsrDeliveryItemFormPage",
			because: "the repeated PageSchemaUId reuses the single resolved name without a second lookup");
	}

	[Test]
	[Description("Rejects a well-formed type-column-uid that is not a column of the resolved object, so typed page sets are never written against a column the platform cannot match (which would silently save dead pages).")]
	public void Create_ShouldThrow_WhenTypeColumnUidIsNotAColumnOfTheObject() {
		// Arrange — the object resolves WITH its columns, but the supplied type-column-uid matches none of them.
		const string realColumnUId = "af280321-e749-41dd-98e5-383906747e29";
		const string foreignColumnUId = "12341234-1234-1234-1234-123412341234";
		StubEntitySchema(new EntityDesignSchemaDto {
			UId = Guid.Parse(EntityUId),
			Name = "Case",
			Columns = new[] { new EntitySchemaColumnDto { UId = Guid.Parse(realColumnUId), Name = "Category" } }
		});
		StubSelectQueue(Rows(PackageUId));

		// Act — a valid base default plus a typed page, but the type-column-uid is a foreign GUID (not a real column).
		Action act = () => _service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true),
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: "1b0bc159-150a-e111-a31b-00155d04c01d")
		}, foreignColumnUId));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*not a column of*",
			because: "a type-column-uid that is not a real column of the object would write unmatchable typed pages");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Accepts a type-column-uid that matches an INHERITED column of the object (e.g. Category on Case), confirming the check spans inherited columns and does not false-reject a valid typed set.")]
	public void Create_ShouldAccept_WhenTypeColumnUidMatchesInheritedColumn() {
		// Arrange — the type column is inherited (the common case: Case.Category is inherited from the base object).
		const string categoryColumnUId = "af280321-e749-41dd-98e5-383906747e29";
		StubEntitySchema(new EntityDesignSchemaDto {
			UId = Guid.Parse(EntityUId),
			Name = "Case",
			InheritedColumns = new[] { new EntitySchemaColumnDto { UId = Guid.Parse(categoryColumnUId), Name = "Category" } }
		});
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act
		RelatedPageAddonResult result = _service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true),
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: "1b0bc159-150a-e111-a31b-00155d04c01d")
		}, categoryColumnUId));

		// Assert
		result.PageCount.Should().Be(2,
			because: "a type-column-uid matching an inherited column is valid and is saved");
		JsonNode.Parse(_savedSchema.MetaData)!["TypeColumnUId"]!.GetValue<string>().Should().Be(categoryColumnUId,
			because: "the validated inherited type column UId is written into the metadata");
	}

	[Test]
	[Description("Does not reject a typed set when the resolved object exposes no columns: an unverifiable check must never block a write (fail-soft, mirroring the codebase's best-effort existence checks).")]
	public void Create_ShouldNotValidateTypeColumn_WhenObjectExposesNoColumns() {
		// Arrange — the default stub resolves an object with no Columns/InheritedColumns, yet a type-column-uid is set.
		const string typeColumnUId = "af280321-e749-41dd-98e5-383906747e29";
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act
		RelatedPageAddonResult result = _service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true),
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: "1b0bc159-150a-e111-a31b-00155d04c01d")
		}, typeColumnUId));

		// Assert
		result.PageCount.Should().Be(2,
			because: "with no columns to verify against, the type-column check is skipped and the set is saved");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Get decodes tolerantly when metadata fields are wrong-typed: a flag stored as a string still reads, and a non-object entry in Pages is skipped rather than throwing a raw framework exception out of a read.")]
	public void Get_ShouldDecodeTolerantly_WhenMetadataFieldsAreWrongTyped() {
		// Arrange — IsDefault as the string "true", Actions.Add as the string "false", plus a stray non-object entry.
		StubAddonMetadata(
			"""{"Pages":[{"UId":"u1","PageSchemaUId":"cc000000-0000-0000-0000-00000000000a","IsDefault":"true","Actions":{"Add":"false"}},42],"TypeColumnUId":null}""");
		StubSelectQueue(Rows(PackageUId), NameRow("UsrDeliveryItemFormPage"));

		// Act
		RelatedPageAddonReadResult result = _service.Get(new RelatedPageAddonReadRequest("Custom", "UsrDeliveryItem"));

		// Assert
		result.PageCount.Should().Be(1,
			because: "the stray non-object entry is skipped and the one object entry decodes");
		result.Pages[0].IsDefault.Should().BeTrue(
			because: "IsDefault stored as the string \"true\" is read tolerantly as true");
		result.Pages[0].IsAdd.Should().BeFalse(
			because: "Actions.Add stored as the string \"false\" is read tolerantly as false");
	}

	[Test]
	[Description("Get returns an empty configuration (not a thrown exception) when the add-on metadata body is malformed / not JSON.")]
	public void Get_ShouldReturnEmpty_WhenMetadataIsMalformed() {
		// Arrange — a body that is not valid JSON; the tolerant parse yields an empty object, not a throw.
		StubAddonMetadata("not-json {");
		StubSelectQueue(Rows(PackageUId));

		// Act
		RelatedPageAddonReadResult result = _service.Get(new RelatedPageAddonReadRequest("Custom", "UsrDeliveryItem"));

		// Assert
		result.PageCount.Should().Be(0,
			because: "a malformed metadata body decodes to an empty set rather than throwing out of a read");
		result.Pages.Should().BeEmpty(
			because: "there are no entries to decode from an unparseable body");
	}

	[Test]
	[Description("Get returns an entry with null names (best-effort) when a stored PageSchemaUId no longer resolves to a page and the stored Role UId is outside the known platform audiences — the read-modify-write safety guarantee, not a throw or a dropped entry.")]
	public void Get_ShouldReturnEntryWithNullNames_WhenPageAndRoleDoNotResolve() {
		// Arrange — an add-on (e.g. configured in the Interface Designer) whose page UId no longer resolves and whose
		// role is a custom/since-changed UId outside KnownPlatformRoleNamesById. The raw UIds must survive for a safe
		// round-trip; only the reverse-resolved names degrade to null.
		StubAddonMetadata(
			"""{"Pages":[{"UId":"u1","PageSchemaUId":"cc000000-0000-0000-0000-0000000000ff","IsDefault":true,"Actions":{"Add":false},"Role":"99998888-7777-6666-5555-444433332222"}],"TypeColumnUId":null}""");
		// package resolves, then the page-name reverse lookup returns no rows (the page no longer exists).
		StubSelectQueue(Rows(PackageUId), """{"success": true, "rows": []}""");

		// Act
		RelatedPageAddonReadResult result = _service.Get(new RelatedPageAddonReadRequest("Custom", "UsrDeliveryItem"));

		// Assert
		result.PageCount.Should().Be(1,
			because: "an entry with unresolvable references is still returned for a safe read-modify-write, not dropped");
		result.Pages[0].PageSchemaUId.Should().Be("cc000000-0000-0000-0000-0000000000ff",
			because: "the raw page UId is preserved so the caller can round-trip it unchanged");
		result.Pages[0].PageSchemaName.Should().BeNull(
			because: "an unresolvable PageSchemaUId degrades to a null name rather than throwing or dropping the entry");
		result.Pages[0].Role.Should().Be("99998888-7777-6666-5555-444433332222",
			because: "the raw role UId is preserved for the round-trip");
		result.Pages[0].RoleName.Should().BeNull(
			because: "a role UId outside the known platform audiences degrades to a null name, not an error");
	}

	[Test]
	[Description("A page given by explicit page-schema-uid is written verbatim without any name resolution, so a page whose name no longer resolves still round-trips.")]
	public void Create_ShouldUsePageSchemaUId_WithoutResolvingName() {
		// Arrange — ONLY the package is queried; a page-schema-uid must not trigger a page-name SelectQuery (if it
		// did, the queue would be exhausted and Dequeue would throw).
		const string pageUId = "cc000000-0000-0000-0000-0000000000ff";
		StubSelectQueue(Rows(PackageUId));

		// Act — base default identified by UId only (no page-schema-name), role-less general audience.
		RelatedPageAddonResult result = _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec(null, IsDefault: true, PageSchemaUId: pageUId)
		}, null));

		// Assert
		result.PageCount.Should().Be(1, because: "a page identified by UId is written without a name lookup");
		JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray()[0]!["PageSchemaUId"]!.GetValue<string>()
			.Should().Be(pageUId, because: "the explicit page-schema-uid is stored verbatim");
	}

	[Test]
	[Description("When a page sets both page-schema-uid and page-schema-name, the explicit UId wins and no name resolution is issued.")]
	public void Create_ShouldPreferPageSchemaUId_OverName() {
		// Arrange — only the package is queried; no page-name lookup should happen.
		const string pageUId = "cc000000-0000-0000-0000-0000000000ff";
		StubSelectQueue(Rows(PackageUId));

		// Act
		RelatedPageAddonResult result = _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true, PageSchemaUId: pageUId)
		}, null));

		// Assert
		result.PageCount.Should().Be(1, because: "the entry is written");
		JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray()[0]!["PageSchemaUId"]!.GetValue<string>()
			.Should().Be(pageUId, because: "page-schema-uid wins over page-schema-name");
	}

	[Test]
	[Description("End-to-end read-modify-write of a page whose name no longer resolves: Get returns its raw pageSchemaUId with a null name; replaying via page-schema-uid re-sends it, so the binding is NOT silently dropped by the replace-not-merge write.")]
	public void GetThenCreate_ShouldRoundTripUnresolvablePage_ViaPageSchemaUId() {
		// Arrange — Get: the page-name reverse lookup returns no rows (name unresolvable) → PageSchemaName null, raw
		// UId survives. Create: only the package is queried (the UId short-circuits name resolution).
		StubAddonMetadata(
			"""{"Pages":[{"UId":"u1","PageSchemaUId":"cc000000-0000-0000-0000-0000000000ff","IsDefault":true,"Actions":{"Add":false},"Role":"a29a3ba5-4b0d-de11-9a51-005056c00008"}],"TypeColumnUId":null}""");
		StubSelectQueue(Rows(PackageUId), """{"success": true, "rows": []}""", Rows(PackageUId));

		// Act
		RelatedPageAddonReadResult read = _service.Get(new RelatedPageAddonReadRequest("Custom", "UsrDeliveryItem"));
		RelatedPageAddonResult written = _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem",
			read.Pages.Select(p => new RelatedPageSpec(
				p.PageSchemaName, p.IsDefault, p.IsAdd, p.IsSspDefault, p.Role, p.TypeColumnValue, p.RoleName,
				p.PageSchemaUId)).ToList(),
			read.TypeColumnUId));

		// Assert
		read.Pages[0].PageSchemaName.Should().BeNull(because: "the page name no longer resolves");
		read.Pages[0].PageSchemaUId.Should().Be("cc000000-0000-0000-0000-0000000000ff",
			because: "the raw page UId still survives Get for the round-trip");
		written.PageCount.Should().Be(1,
			because: "replaying via page-schema-uid re-sends the unresolvable page instead of dropping it");
		JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray()[0]!["PageSchemaUId"]!.GetValue<string>()
			.Should().Be("cc000000-0000-0000-0000-0000000000ff",
				because: "the original page UId is preserved through the round-trip");
	}

	[Test]
	[Description("Rejects a page entry that supplies neither page-schema-name nor page-schema-uid before any remote call.")]
	public void Create_ShouldThrow_WhenPageHasNeitherNameNorUId() {
		// Act
		Action act = () => _service.Create(Request(new RelatedPageSpec(null, IsDefault: true)));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*page-schema-name or a page-schema-uid*",
			because: "a page with no name and no UId cannot be resolved");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Rejects a non-GUID page-schema-uid before any remote call.")]
	public void Create_ShouldThrow_WhenPageSchemaUIdIsNotAGuid() {
		// Act
		Action act = () => _service.Create(Request(new RelatedPageSpec(null, IsDefault: true, PageSchemaUId: "not-a-guid")));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*page-schema-uid*not a valid GUID*",
			because: "a malformed page-schema-uid must be rejected, not written into metadata");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Accepts a supported audience role UId given in brace format ({…}) — recognition compares GUIDs, not strings — and stores it normalized to canonical D format, matching what the Interface Designer writes.")]
	public void Create_ShouldAcceptAndNormalizeBraceFormatRoleUId() {
		// Arrange — the portal role in brace format, layered on a role-less general base.
		const string portalBrace = "{720b771c-e7a7-4f31-9cfb-52cd21c3739f}";
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act
		RelatedPageAddonResult result = _service.Create(new RelatedPageAddonRequest("Custom", "UsrDeliveryItem", new[] {
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true),
			new RelatedPageSpec("UsrDeliveryItemPortalPage", IsDefault: true, Role: portalBrace)
		}, null));

		// Assert
		result.PageCount.Should().Be(2,
			because: "a valid portal role UId in brace format is a recognized audience, not rejected as unsupported");
		JsonNode.Parse(_savedSchema.MetaData)!["Pages"]!.AsArray()[1]!["Role"]!.GetValue<string>()
			.Should().Be("720b771c-e7a7-4f31-9cfb-52cd21c3739f",
				because: "the role is stored normalized to canonical D format, as the Interface Designer writes it");
	}

	[Test]
	[Description("A non-JSON page-name lookup response (e.g. an auth-redirect HTML page from an expired session) surfaces as a clean lookup failure, not a raw JSON parse exception — QuerySysSchemaRow now routes through the guarded ExecuteSelectQuery like every other lookup in the helper.")]
	public void Create_ShouldFailCleanly_WhenPageNameLookupReturnsNonJson() {
		// Arrange — package resolves, then the page-name SelectQuery returns an HTML login redirect.
		StubSelectQueue(Rows(PackageUId), "<!DOCTYPE html><html><body>login</body></html>");

		// Act
		Action act = () => _service.Create(Request(new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true)));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*Failed to query schema metadata*",
			because: "a non-JSON response must surface as a clean lookup failure, not a raw JObject.Parse exception");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Description("Normalizes type-column-uid and per-page type-column-value to canonical D format when stored, so a valid UId given in brace/N format matches what the Interface Designer writes (typed matching would otherwise silently fail at runtime).")]
	public void Create_ShouldNormalizeTypeColumnUidAndValue_ToDFormat() {
		// Arrange — the type column UId and the typed value both supplied in brace format.
		const string typeColumnBrace = "{af280321-e749-41dd-98e5-383906747e29}";
		const string typeValueBrace = "{1b0bc159-150a-e111-a31b-00155d04c01d}";
		StubSelectQueue(Rows(PackageUId), Rows(PageAUId), Rows(PageBUId));

		// Act — a base default plus a typed page; both type identifiers are in brace format.
		_service.Create(new RelatedPageAddonRequest("Custom", "Case", new[] {
			new RelatedPageSpec("CaseFormPage", IsDefault: true),
			new RelatedPageSpec("CaseIncidentPage", IsDefault: true, TypeColumnValue: typeValueBrace)
		}, typeColumnBrace));

		// Assert
		JsonNode metadata = JsonNode.Parse(_savedSchema.MetaData)!;
		metadata["TypeColumnUId"]!.GetValue<string>().Should().Be("af280321-e749-41dd-98e5-383906747e29",
			because: "type-column-uid is stored normalized to canonical D format, as the Interface Designer writes it");
		metadata["Pages"]!.AsArray()[1]!["TypeColumnValue"]!.GetValue<string>()
			.Should().Be("1b0bc159-150a-e111-a31b-00155d04c01d",
				because: "the typed value is stored normalized to canonical D format so runtime type matching succeeds");
	}
}
