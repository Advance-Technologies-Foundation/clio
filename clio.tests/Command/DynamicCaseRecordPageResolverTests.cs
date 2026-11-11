using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.RelatedPages;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class DynamicCaseRecordPageResolverTests {

	private const string Package = "UsrOrders";
	private const string AllEmployeesId = "a29a3ba5-4b0d-de11-9a51-005056c00008";
	private const string AllPortalUsersId = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";

	private IRelatedPageAddonService _addons;
	private IEntityClientSchemaReader _cards;
	private DynamicCaseRecordPageResolver _resolver;

	[SetUp]
	public void Setup() {
		_addons = Substitute.For<IRelatedPageAddonService>();
		_cards = Substitute.For<IEntityClientSchemaReader>();
		_resolver = new DynamicCaseRecordPageResolver(_addons, _cards);
	}

	[TearDown]
	public void TearDown() {
		_addons.ClearReceivedCalls();
		_cards.ClearReceivedCalls();
	}

	private static RelatedPageEntry Entry(string page, bool isDefault = true, bool isSspDefault = false,
		string role = null, string roleName = null, string type = null) =>
		new(Guid.NewGuid().ToString(), page, isDefault, false, isSspDefault, role, roleName, type);

	private void AddonLists(string entity, params RelatedPageEntry[] pages) =>
		_addons.Get(Arg.Is<RelatedPageAddonReadRequest>(request => request.EntitySchemaName == entity))
			.Returns(new RelatedPageAddonReadResult(entity, Guid.NewGuid().ToString(), Package, Guid.NewGuid().ToString(),
				"RelatedPage", null, pages.Length, pages));

	private void CardsAre(string entity, params MigrationEditPageInfo[] cards) =>
		_cards.TryResolve(Arg.Is<ListEntityClientSchemasOptions>(options => options.EntityName == entity),
				out Arg.Any<ListEntityClientSchemasResponse>())
			.Returns(call => {
				call[1] = new ListEntityClientSchemasResponse {
					Success = true, Entity = entity, EditPages = [.. cards], Sections = []
				};
				return true;
			});

	[Test]
	[Description("A page the caller names wins and nothing is read: the page is still checked later, by the page step.")]
	public void Resolve_ShouldUseTheExplicitPage_WithoutReadingRegistrations() {
		// Arrange & Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", " UsrOrders_FormPage ");

		// Assert
		resolution.PageSchemaNames.Should().Equal(["UsrOrders_FormPage"], because: "the named page is the target");
		resolution.ResolvedBy.Should().Be(DynamicCaseRecordPageResolver.ResolvedByExplicit, because: "it was named");
		_addons.DidNotReceiveWithAnyArgs().Get(default!);
		_cards.DidNotReceiveWithAnyArgs().TryResolve(default!, out _);
	}

	[Test]
	[Description("The object's untyped default page for employees in the RelatedPage add-on is the record page.")]
	public void Resolve_ShouldUseTheUntypedAddonDefault() {
		// Arrange
		AddonLists("UsrOrder", Entry("UsrOrders_FormPage"), Entry("UsrOrders_AddPage", isDefault: false));

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", null);

		// Assert
		resolution.PageSchemaNames.Should().Equal(["UsrOrders_FormPage"], because: "that is the page the object opens");
		resolution.ResolvedBy.Should().Be(DynamicCaseRecordPageResolver.ResolvedByRelatedPageAddon,
			because: "the add-on decided it");
		_cards.DidNotReceiveWithAnyArgs().TryResolve(default!, out _);
	}

	[Test]
	[Description("An object whose add-on lists only typed defaults (the Activity shape) is ambiguous: the candidates are returned so the caller can name one.")]
	public void Resolve_ShouldBeAmbiguous_WhenOnlyTypedDefaultsExist() {
		// Arrange
		AddonLists("Activity", Entry("Tasks_FormPage", type: "fbe0acdc-cfc0-df11-b00f-001d60e938c6"),
			Entry("Calls_FormPage", type: "e2831dec-cfc0-df11-b00f-001d60e938c6"));

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "Activity", null);

		// Assert
		resolution.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageAmbiguous,
			because: "picking one record type's page would decorate one type and not the others");
		resolution.Candidates.Should().HaveCount(2, because: "every typed page is a choice");
		resolution.Candidates.Should().Contain(candidate => candidate.PageSchemaName == "Tasks_FormPage"
			&& candidate.TypeColumnValue == "fbe0acdc-cfc0-df11-b00f-001d60e938c6",
			because: "each candidate carries the record type it opens for");
		resolution.ErrorMessage.Should().Contain("page-schema-name", because: "the refusal names the remedy");
	}

	[Test]
	[Description("The portal default listed first (the Case shape) is skipped: the employee default is the record page an agent decorates.")]
	public void Resolve_ShouldSkipThePortalDefault_WhenItIsListedFirst() {
		// Arrange
		AddonLists("Case",
			Entry("Portal_Case_FormPage", isSspDefault: true, role: AllPortalUsersId, roleName: "All external users"),
			Entry("Cases_FormPage", role: AllEmployeesId, roleName: "All employees"));

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "Case", null);

		// Assert
		resolution.PageSchemaNames.Should().Equal(["Cases_FormPage"],
			because: "the portal page belongs to another audience and is not the employees' record page");
	}

	[Test]
	[Description("An object with no add-on default falls back to its SysModuleEdit card, accepted when the card is a Freedom UI page.")]
	public void Resolve_ShouldFallBackToAFreedomCard_WhenTheAddonIsEmpty() {
		// Arrange
		AddonLists("UsrOrder");
		CardsAre("UsrOrder", new MigrationEditPageInfo {
			CardSchema = "UsrOrders_FormPage", Kind = ListEntityClientSchemasCommand.KindFreedom
		});

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", null);

		// Assert
		resolution.PageSchemaNames.Should().Equal(["UsrOrders_FormPage"], because: "the card is the record page");
		resolution.ResolvedBy.Should().Be(DynamicCaseRecordPageResolver.ResolvedBySysModuleEdit,
			because: "the card registration decided it");
	}

	[Test]
	[Description("A Classic card is refused as record-page-not-freedom with the create-page and create-related-page-addon remedy.")]
	public void Resolve_ShouldRefuseAClassicCard() {
		// Arrange
		AddonLists("UsrOrder");
		CardsAre("UsrOrder", new MigrationEditPageInfo {
			CardSchema = "UsrOrderPage", Kind = ListEntityClientSchemasCommand.KindClassic
		});

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", null);

		// Assert
		resolution.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageNotFreedom,
			because: "a Classic page cannot carry the Freedom UI stage bar");
		resolution.ErrorMessage.Should().Contain("create-page").And.Contain("create-related-page-addon",
			because: "the refusal names the remedy");
	}

	[Test]
	[Description("When the add-on cannot be read and no card exists the refusal is record-page-not-found and still names why the add-on gave nothing.")]
	public void Resolve_ShouldReportNotFound_WithTheAddonFailure_WhenNothingResolves() {
		// Arrange
		_addons.Get(Arg.Any<RelatedPageAddonReadRequest>()).Throws(new InvalidOperationException("add-on read failed"));
		CardsAre("UsrOrder");

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", null);

		// Assert
		resolution.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageNotFound, because: "no page was found");
		resolution.ErrorMessage.Should().Contain("add-on read failed",
			because: "a silent add-on failure would read as an object that has no record page");
	}

	[TestCase(ListEntityClientSchemasCommand.KindClassic)]
	[TestCase(ListEntityClientSchemasCommand.KindFreedom)]
	[Description("An add-on that cannot be read is not an empty one: the card registration is not consulted, so a Classic card cannot be blamed and another Freedom card cannot get the bar.")]
	public void Resolve_ShouldNotFallBackToACard_WhenTheAddonCannotBeRead(string cardKind) {
		// Arrange
		_addons.Get(Arg.Any<RelatedPageAddonReadRequest>()).Throws(new InvalidOperationException("schema is not available"));
		CardsAre("UsrOrder", new MigrationEditPageInfo { CardSchema = "UsrOrderPage", Kind = cardKind });

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", null);

		// Assert
		resolution.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageNotFound,
			because: "the page the object opens is unknown, which is neither a Classic page nor this card");
		resolution.ErrorMessage.Should().Contain("schema is not available").And.Contain("page-schema-name",
			because: "the refusal names why and the remedy");
		_cards.DidNotReceiveWithAnyArgs().TryResolve(default!, out _);
	}

	[Test]
	[Description("Several untyped card registrations are ambiguous: their order is the database's, and one may be a portal card.")]
	public void Resolve_ShouldBeAmbiguous_WhenSeveralUntypedCardsExist() {
		// Arrange
		AddonLists("Order");
		CardsAre("Order",
			new MigrationEditPageInfo { CardSchema = "PortalOrderPage", Kind = ListEntityClientSchemasCommand.KindClassic },
			new MigrationEditPageInfo { CardSchema = "OrderPageV2", Kind = ListEntityClientSchemasCommand.KindClassic });

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "Order", null);

		// Assert
		resolution.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageAmbiguous, because: "two pages are registered");
		resolution.Candidates.Select(candidate => candidate.PageSchemaName).Should()
			.BeEquivalentTo(["PortalOrderPage", "OrderPageV2"], because: "every registered page is a choice");
		resolution.ResolvedBy.Should().Be(DynamicCaseRecordPageResolver.ResolvedBySysModuleEdit,
			because: "the card registrations decided it");
	}

	[Test]
	[Description("Typed card registrations only (no add-on default) are ambiguous, not not-found.")]
	public void Resolve_ShouldBeAmbiguous_WhenOnlyTypedCardsExist() {
		// Arrange
		AddonLists("Activity");
		CardsAre("Activity",
			new MigrationEditPageInfo {
				CardSchema = "TaskPage", TypeColumnValue = "fbe0acdc-cfc0-df11-b00f-001d60e938c6",
				Kind = ListEntityClientSchemasCommand.KindFreedom
			},
			new MigrationEditPageInfo {
				CardSchema = "CallPage", TypeColumnValue = "e2831dec-cfc0-df11-b00f-001d60e938c6",
				Kind = ListEntityClientSchemasCommand.KindFreedom
			});

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "Activity", null);

		// Assert
		resolution.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageAmbiguous, because: "each type opens its own page");
		resolution.Candidates.Should().HaveCount(2, because: "every typed card is a choice");
		resolution.ResolvedBy.Should().Be(DynamicCaseRecordPageResolver.ResolvedBySysModuleEdit,
			because: "the card registrations decided it");
	}

	[Test]
	[Description("A default bound to a custom role is another audience's page: the employee default is chosen next to it.")]
	public void Resolve_ShouldSkipACustomRoleDefault_WhenAnEmployeeDefaultExists() {
		// Arrange
		AddonLists("UsrOrder",
			Entry("UsrOrders_SalesPage", role: "3f1d8f0c-0d0a-4c4e-9a43-2a1f5c7e9b11", roleName: "Sales department"),
			Entry("UsrOrders_FormPage", role: AllEmployeesId, roleName: "All employees"));

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", null);

		// Assert
		resolution.PageSchemaNames.Should().Equal(["UsrOrders_FormPage"],
			because: "decorating the custom-role page would change what another audience sees");
	}

	[Test]
	[Description("When the only add-on default belongs to a custom role, the add-on names no employee page and the card registration decides.")]
	public void Resolve_ShouldFallBackToTheCard_WhenTheOnlyDefaultHasACustomRole() {
		// Arrange
		AddonLists("UsrOrder",
			Entry("UsrOrders_SalesPage", role: "3f1d8f0c-0d0a-4c4e-9a43-2a1f5c7e9b11", roleName: "Sales department"));
		CardsAre("UsrOrder", new MigrationEditPageInfo {
			CardSchema = "UsrOrders_FormPage", Kind = ListEntityClientSchemasCommand.KindFreedom
		});

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", null);

		// Assert
		resolution.PageSchemaNames.Should().Equal(["UsrOrders_FormPage"], because: "the card is the employees' page");
		resolution.ResolvedBy.Should().Be(DynamicCaseRecordPageResolver.ResolvedBySysModuleEdit,
			because: "the add-on had no employee default");
	}

	[Test]
	[Description("An add-on that lists two untyped employee defaults is ambiguous.")]
	public void Resolve_ShouldBeAmbiguous_WhenTheAddonListsTwoUntypedDefaults() {
		// Arrange
		AddonLists("UsrOrder", Entry("UsrOrders_FormPage"), Entry("UsrOrders_OtherPage"));

		// Act
		RecordPageResolution resolution = _resolver.Resolve(Package, "UsrOrder", null);

		// Assert
		resolution.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageAmbiguous, because: "two defaults compete");
		resolution.ErrorMessage.Should().Contain("lists 2 default pages", because: "the reason names the count");
	}
}
