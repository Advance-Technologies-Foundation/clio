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
	private RelatedPageAddonService _service;
	private AddonSchemaDto _savedSchema;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_addonSchemaDesignerClient = Substitute.For<IAddonSchemaDesignerClient>();
		_entitySchemaDesignerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto { MetaData = """{"Pages":[],"TypeColumnUId":null}""" });
		_addonSchemaDesignerClient
			.When(client => client.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(callInfo => _savedSchema = callInfo.Arg<AddonSchemaDto>());
		// The object (entity schema) is resolved through the entity schema designer, not a SelectQuery.
		StubEntitySchema(new EntityDesignSchemaDto { UId = Guid.Parse(EntityUId), Name = "UsrDeliveryItem" });
		_service = new RelatedPageAddonService(
			_applicationClient, _serviceUrlBuilder, _addonSchemaDesignerClient, _entitySchemaDesignerClient);
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
	[Description("Rejects a page entry that sets both an explicit role UId and a role name, so the caller's audience is never silently dropped.")]
	public void Create_ShouldThrow_WhenPageSetsBothRoleAndRoleName() {
		// Arrange / Act
		Action act = () => _service.Create(Request(
			new RelatedPageSpec("UsrDeliveryItemFormPage", IsDefault: true,
				Role: "a29a3ba5-4b0d-de11-9a51-005056c00008", RoleName: "All external users")));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*both role and role-name*",
			because: "an ambiguous role + role-name pair must be rejected, not resolved by silently dropping role-name");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
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
}
