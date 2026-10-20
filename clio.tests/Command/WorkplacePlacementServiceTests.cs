using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Behaviour of <see cref="WorkplacePlacementService"/> over an in-memory environment: the workplace is created or
/// reused, the section's My applications placement is moved, roles are granted, every row is bound into the
/// package, a re-run changes nothing, and a bad input changes nothing.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WorkplacePlacementServiceTests {

	private const string Environment = "dev";
	private const string Package = "UsrOrdersApp";
	private const string SectionCode = "UsrOrders";
	private const string WorkplaceName = "Orders";

	private FakeEnvironment _env;
	private IPackageTargetResolver _packageResolver;
	private INavigationCacheResetter _resetter;
	private WorkplacePlacementService _sut;

	[SetUp]
	public void SetUp() {
		_env = new FakeEnvironment();
		_packageResolver = Substitute.For<IPackageTargetResolver>();
		_packageResolver.Resolve(Arg.Any<string>(), true)
			.Returns(PackageTargetResolution.Resolved(Package, FakeEnvironment.PackageUId));
		_resetter = Substitute.For<INavigationCacheResetter>();
		_resetter.BuildBrowserSessionNote(Arg.Any<EnvironmentSettings>()).Returns("browser note");
		_sut = new WorkplacePlacementService(_env, _env, _packageResolver, _resetter,
			Substitute.For<IApplicationClient>(), new EnvironmentSettings());
	}

	private static WorkplacePlacementRequest Request(IReadOnlyList<string> roles = null, bool remove = true,
		string workplace = WorkplaceName, int? position = null) =>
		new(Environment, null, SectionCode, workplace, "web", roles ?? ["Sales managers"], remove, position);

	[Test]
	[Description("Creates the workplace, moves the My applications placement into it, grants the role and binds every row into the package with the workplace's full column set.")]
	public void Place_ShouldCreateWorkplaceMoveSectionAndGrantRole_WhenWorkplaceDoesNotExist() {
		// Arrange
		Guid myAppsRow = _env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Workplace.Created.Should().BeTrue(because: "no workplace with that name existed");
		result.Workplace.BindingName.Should().Be("SysWorkplace_Orders", because: "a new binding is named <Schema>_<Workplace>");
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionMoved, because: "the app should leave My applications");
		result.Section.PlacementId.Should().Be(myAppsRow, because: "a move changes the existing row's parent instead of adding one");
		result.Section.BindingName.Should().Be("SysModuleInWorkplace_UsrOrders",
			because: "the binding create-app already ships for the row is updated, not a parallel one");
		_env.Placements.Single().WorkplaceId.Should().Be(result.Workplace.Id,
			because: "the only placement of the section is now in the target workplace");
		result.Roles.Single().Action.Should().Be(WorkplacePlacementService.ActionGranted, because: "the role had no grant yet");
		_env.Grants.Should().ContainSingle(grant => grant.WorkplaceId == result.Workplace.Id
			&& grant.TargetId == FakeEnvironment.SalesManagersId, because: "the role now sees the workplace");
		IReadOnlyDictionary<string, string> boundWorkplace = _env.BoundRow("SysWorkplace_Orders", result.Workplace.Id);
		boundWorkplace.Keys.Should().Contain(["Id", "Name", "Position", "SysApplicationClientType", "Type", "LoaderId"],
			because: "an install supplies no defaults, so the binding must carry the whole column set");
		boundWorkplace["Type"].Should().Be(FakeEnvironment.DefaultTypeId.ToString(),
			because: "Type is read back from the live row the platform filled in");
		_env.BoundRow("SysAdminUnitInWorkplace_Orders", result.Roles.Single().GrantId).Should().NotBeNull(
			because: "the grant ships with the package");
		result.Warnings.Should().BeEmpty(because: "nothing unusual happened");
	}

	[Test]
	[Description("A second identical call creates nothing and reports every part as already present.")]
	public void Place_ShouldChangeNothing_WhenRunTwice() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		WorkplacePlacementResult first = _sut.Place(Request());
		int bindingsBefore = _env.CreatedBindings.Count;

		// Act
		WorkplacePlacementResult second = _sut.Place(Request());

		// Assert
		second.Workplace.Created.Should().BeFalse(because: "the workplace from the first run is reused");
		second.Workplace.Id.Should().Be(first.Workplace.Id, because: "it is the same workplace");
		second.Section.Action.Should().Be(WorkplacePlacementService.ActionAlreadyPresent, because: "the section is already there");
		second.Roles.Single().Action.Should().Be(WorkplacePlacementService.ActionAlreadyGranted, because: "the grant exists");
		_env.Workplaces.Should().HaveCount(2, because: "My applications plus the one target workplace, no duplicate");
		_env.Placements.Should().HaveCount(1, because: "a re-run must not duplicate the placement");
		_env.Grants.Should().HaveCount(1, because: "a re-run must not duplicate the grant");
		_env.CreatedBindings.Count.Should().Be(bindingsBefore, because: "every binding already exists");
		second.Workplace.BindingName.Should().Be(first.Workplace.BindingName, because: "the existing binding is found by its row");
	}

	[Test]
	[Description("Adds a new placement and leaves the My applications one when remove-from-my-applications is false.")]
	public void Place_ShouldAddPlacement_WhenRemoveFromMyApplicationsIsFalse() {
		// Arrange
		Guid myAppsRow = _env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request(remove: false));

		// Assert
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionCreated, because: "the caller asked for a second placement");
		_env.Placements.Should().HaveCount(2, because: "the My applications placement stays");
		_env.Placements.Single(row => row.Id == myAppsRow).WorkplaceId.Should().Be(FakeEnvironment.MyApplicationsId,
			because: "the original row is untouched");
	}

	[Test]
	[Description("Reuses the oldest of several same-named workplaces and warns about the others.")]
	public void Place_ShouldReuseOldestAndWarn_WhenSeveralWorkplacesShareTheName() {
		// Arrange
		Guid oldest = _env.SeedWorkplace(WorkplaceName);
		_env.SeedWorkplace(WorkplaceName);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Workplace.Id.Should().Be(oldest, because: "the oldest match is the stable choice across runs");
		result.Workplace.Created.Should().BeFalse(because: "an existing workplace was reused");
		result.Warnings.Should().Contain(warning => warning.Contains("2 workplaces are named"),
			because: "the agent must tell the user the name is ambiguous");
		result.Warnings.Should().Contain(warning => warning.Contains("now bound there"),
			because: "adopting a workplace the package did not ship changes what the package installs");
	}

	[Test]
	[Description("Refuses an ambiguous role name before any write.")]
	public void Place_ShouldThrowBeforeWriting_WhenRoleNameIsAmbiguous() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.AdminUnits.Add(new AdminUnitRecord(Guid.NewGuid(), "Sales managers"));

		// Act
		Action act = () => _sut.Place(Request());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*matches 2*",
			because: "a role name must identify exactly one SysAdminUnit");
		_env.Upserts.Should().BeEmpty(because: "every input is resolved before the first write");
		_env.CreatedBindings.Should().BeEmpty(because: "nothing is written on an input error");
	}

	[Test]
	[Description("Refuses an unknown section code before any write.")]
	public void Place_ShouldThrowBeforeWriting_WhenSectionIsUnknown() {
		// Arrange
		WorkplacePlacementRequest request = Request() with { SectionCode = "UsrMissing" };

		// Act
		Action act = () => _sut.Place(request);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*UsrMissing*",
			because: "the error names the code that was not found");
		_env.Upserts.Should().BeEmpty(because: "nothing is written on an input error");
	}

	[Test]
	[Description("Refuses a client type other than web or mobile.")]
	public void Place_ShouldThrow_WhenClientTypeIsUnknown() {
		// Arrange
		WorkplacePlacementRequest request = Request() with { ClientType = "desktop" };

		// Act
		Action act = () => _sut.Place(request);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*web*mobile*",
			because: "the error lists the accepted values");
	}

	[Test]
	[Description("Relays the package resolver's error when the target package cannot receive writes.")]
	public void Place_ShouldThrow_WhenPackageCannotBeResolved() {
		// Arrange
		_packageResolver.Resolve(Arg.Any<string>(), true)
			.Returns(PackageTargetResolution.Unresolvable("Package 'X' is locked."));

		// Act
		Action act = () => _sut.Place(Request());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*locked*",
			because: "a locked package cannot ship the bindings");
	}

	[Test]
	[Description("Warns that nobody sees the workplace when no role is granted and the section is added, not moved.")]
	public void Place_ShouldWarn_WhenNoRoleIsGranted() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request(roles: [], remove: false));

		// Assert
		result.Roles.Should().BeEmpty(because: "no role was asked for");
		result.Warnings.Should().Contain(warning => warning.Contains("nobody sees it"),
			because: "a workplace without a grant is invisible");
	}

	[Test]
	[Description("Resolves a role passed by id and updates the workplace position when one is given.")]
	public void Place_ShouldAcceptRoleIdAndPosition() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request(roles: [FakeEnvironment.SalesManagersId.ToString()],
			position: 7));

		// Assert
		result.Roles.Single().RoleId.Should().Be(FakeEnvironment.SalesManagersId, because: "a GUID is taken as an id");
		result.Workplace.Position.Should().Be(7, because: "the requested position is written by update and read back");
	}

	[Test]
	[Description("Refuses, before any write, to move the section out of My applications into a workplace no role sees, because that hides the app from everyone.")]
	public void Place_ShouldThrowBeforeWriting_WhenMoveWouldHideSection() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		Action act = () => _sut.Place(Request(roles: []));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*hide the app*",
			because: "a section moved into a workplace without a grant disappears from navigation");
		_env.Upserts.Should().BeEmpty(because: "the refusal comes before the first write");
		_env.Placements.Single().WorkplaceId.Should().Be(FakeEnvironment.MyApplicationsId,
			because: "the section stays in My applications");
	}

	[Test]
	[Description("Grants the roles before moving the section, so a failed grant leaves the section in My applications instead of in a workplace nobody sees.")]
	public void Place_ShouldLeaveSectionInMyApplications_WhenGrantFails() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.FailUpsertOfSchema = WorkplacePlacementService.RoleGrantSchema;

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Error.Should().Contain("failed", because: "the grant write failed");
		_env.Placements.Single().WorkplaceId.Should().Be(FakeEnvironment.MyApplicationsId,
			because: "the placement is moved only after every grant is written");
	}

	[Test]
	[Description("A failure after the first write returns what was already written and still resets the navigation cache, instead of an error that reads as 'nothing changed'.")]
	public void Place_ShouldReportWrittenRowsAndResetCache_WhenAWriteFailsAfterTheFirst() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.FailUpsertOfSchema = WorkplacePlacementService.SectionPlacementSchema;

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Error.Should().Contain("already on the environment",
			because: "the caller must not conclude that nothing changed");
		Guid workplaceId = _env.FindWorkplaces(WorkplaceName, FakeEnvironment.WebClientTypeId).Single().Id;
		result.Written.Should().Contain(write => write.Schema == WorkplacePlacementService.WorkplaceSchema
			&& write.Id == workplaceId, because: "the new workplace row exists");
		result.Written.Should().Contain(write => write.Schema == WorkplacePlacementService.RoleGrantSchema
			&& write.Id != null, because: "the grant row exists");
		result.Written.Should().Contain(write => write.Id == null && write.BindingName == "SysWorkplace_Orders",
			because: "a created binding is a write too");
		result.NextStep.Should().Contain("log out", because: "the written rows are seen only after a re-login");
		_resetter.Received(1).TryReset(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Still throws, without resetting the navigation cache, when a step fails before the first write.")]
	public void Place_ShouldThrow_WhenFailureComesBeforeTheFirstWrite() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		Action act = () => _sut.Place(Request(roles: ["Nobody"]));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "nothing was written, so there is nothing to report");
		_env.CreatedBindings.Should().BeEmpty(because: "the input is resolved before the first write");
		_resetter.DidNotReceiveWithAnyArgs().TryReset(default, default);
	}

	[Test]
	[Description("Reports a binding creation that failed as attempted, because it may have changed the environment.")]
	public void Place_ShouldReportFailedBindingCreation() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.FailCreateBinding = true;

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Written.Should().ContainSingle(write => write.Id == null && write.BindingName == "SysWorkplace_Orders",
			because: "a write is recorded before it is made");
		result.Error.Should().Contain("failed", because: "the caller learns what failed");
	}

	[Test]
	[Description("Binds a moved placement into the target package when no package ships it yet.")]
	public void Place_ShouldBindMovedPlacementInPackage_WhenNoPackageShipsIt() {
		// Arrange
		Guid row = _env.SeedMyApplicationsPlacement(bindInPackage: false);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionMoved, because: "the row is moved");
		_env.BoundRow("SysModuleInWorkplace_UsrOrders", row).Should().NotBeNull(
			because: "the moved row must ship with the package");
	}

	[Test]
	[Description("Refuses, before any write, to move a placement another package ships, because installing that package would undo the move; the error names the package.")]
	public void Place_ShouldThrowBeforeWriting_WhenAnotherPackageShipsThePlacement() {
		// Arrange
		Guid row = _env.SeedMyApplicationsPlacement(bindInPackage: false);
		_env.OtherPackageBindings["SysModuleInWorkplace_UsrOrders"] = ("SysModuleInWorkplace", new() {
			[row] = new() { ["Id"] = row.ToString(), ["SysWorkplace"] = FakeEnvironment.MyApplicationsId.ToString() }
		});

		// Act
		Action act = () => _sut.Place(Request());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage($"*'{FakeEnvironment.OtherPackage}'*",
			because: "two packages shipping one row with different workplaces is decided by install order");
		_env.Upserts.Should().BeEmpty(because: "the refusal comes before the first write");
	}

	[Test]
	[Description("Refuses the move when another package ships the placement even though the target package ships it too, because install order would decide where the section ends up.")]
	public void Place_ShouldThrowBeforeWriting_WhenTargetAndAnotherPackageBothShipThePlacement() {
		// Arrange
		Guid row = _env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.OtherPackageBindings["SysModuleInWorkplace_Custom"] = ("SysModuleInWorkplace", new() {
			[row] = new() { ["Id"] = row.ToString(), ["SysWorkplace"] = FakeEnvironment.MyApplicationsId.ToString() }
		});

		// Act
		Action act = () => _sut.Place(Request());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage($"*'{FakeEnvironment.OtherPackage}'*",
			because: "installing the other package would put the section back in My applications");
		_env.Upserts.Should().BeEmpty(because: "the refusal comes before the first write");
	}

	[Test]
	[Description("Finds another package's binding with one lookup, without reading every other package's bindings one by one.")]
	public void Place_ShouldNotReadOtherPackagesBindings_WhenCheckingTheMove() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		for (int index = 0; index < 50; index++) {
			Guid unrelated = Guid.NewGuid();
			_env.OtherPackageBindings[$"SysModuleInWorkplace_Other{index}"] = ("SysModuleInWorkplace", new() {
				[unrelated] = new() { ["Id"] = unrelated.ToString() }
			});
		}

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionMoved, because: "no other package ships the row");
		_env.ReadBindingPackages.Should().NotContain(FakeEnvironment.OtherPackage,
			because: "a per-binding read of every other package costs one round-trip per binding");
		_env.ShippingRowLookups.Should().Be(1,
			because: "one lookup answers which packages ship the moved placement");
	}

	[Test]
	[Description("Reads a binding whose bound data could not be parsed instead of treating it as not shipping the row.")]
	public void Place_ShouldReadUnverifiedBinding_WhenItsBoundDataCouldNotBeParsed() {
		// Arrange
		Guid row = _env.SeedMyApplicationsPlacement(bindInPackage: false);
		_env.OtherPackageBindings["SysModuleInWorkplace_Custom"] = ("SysModuleInWorkplace", new() {
			[row] = new() { ["Id"] = row.ToString() }
		});
		_env.UnparseableBindings.Add("SysModuleInWorkplace_Custom");

		// Act
		Action act = () => _sut.Place(Request());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage($"*'{FakeEnvironment.OtherPackage}'*",
			because: "the per-binding read still finds the row the other package ships");
		_env.ReadBindingPackages.Should().Contain(FakeEnvironment.OtherPackage,
			because: "only the unparseable binding is read on its own");
	}

	[Test]
	[Description("Records a write before making it, so an upsert that fails half-way is still reported as written.")]
	public void Place_ShouldReportTheFailingWrite_WhenTheFirstUpsertFails() {
		// Arrange: the workplace and its binding exist, so the failing upsert is the call's first write.
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid orders = _env.SeedWorkplace(WorkplaceName);
		_env.Bindings["SysWorkplace_Orders"] = ("SysWorkplace", new() { [orders] = new() { ["Id"] = orders.ToString() } });
		_env.FailUpsertOfSchema = WorkplacePlacementService.WorkplaceSchema;

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Error.Should().NotBeNull(because: "the workplace upsert failed");
		result.Written.Should().ContainSingle(write => write.Id == orders,
			because: "the live update may have happened before the binding save failed");
		_env.CreatedBindings.Should().BeEmpty(because: "the upsert was the first and only write");
	}

	[Test]
	[Description("Leaves an existing grant another package ships unbound, so two packages never ship one grant.")]
	public void Place_ShouldNotRebindGrant_WhenAnotherPackageShipsIt() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid sales = _env.SeedWorkplace(WorkplaceName);
		Guid grant = Guid.NewGuid();
		_env.Grants.Add(new WorkplaceJunctionRecord(grant, sales, FakeEnvironment.SalesManagersId));
		_env.OtherPackageBindings["SysWorkplace_Sales"] = ("SysWorkplace", new() {
			[sales] = new() { ["Id"] = sales.ToString() }
		});
		_env.OtherPackageBindings["SysAdminUnitInWorkplace_Sales"] = ("SysAdminUnitInWorkplace", new() {
			[grant] = new() { ["Id"] = grant.ToString() }
		});

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		WorkplacePlacementRole role = result.Roles.Single();
		role.Action.Should().Be(WorkplacePlacementService.ActionAlreadyGranted, because: "the grant exists");
		role.BindingName.Should().BeNull(because: "the grant belongs to the other package");
		_env.Bindings.Values.Should().NotContain(binding => binding.Rows.ContainsKey(grant),
			because: "binding it here too would let install order decide the grant");
	}

	[Test]
	[Description("Accepts an unchanged position for a workplace another package ships, so a re-run converges.")]
	public void Place_ShouldAcceptUnchangedPosition_ForAnotherPackagesWorkplace() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid sales = _env.SeedWorkplace(WorkplaceName);
		_env.OtherPackageBindings["SysWorkplace_Sales"] = ("SysWorkplace", new() {
			[sales] = new() { ["Id"] = sales.ToString() }
		});

		// Act
		WorkplacePlacementResult result = _sut.Place(Request(position: 5));

		// Assert
		result.Error.Should().BeNull(because: "position 5 is the workplace's current position, so nothing changes");
		result.Workplace.Position.Should().Be(5, because: "the workplace is untouched");
	}

	[Test]
	[Description("Uses an existing workplace another package ships without binding it into the target package, and warns.")]
	public void Place_ShouldUseButNotBindWorkplace_WhenAnotherPackageShipsIt() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid sales = _env.SeedWorkplace(WorkplaceName);
		_env.OtherPackageBindings["SysWorkplace_Sales"] = ("SysWorkplace", new() {
			[sales] = new() { ["Id"] = sales.ToString() }
		});

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Workplace.Id.Should().Be(sales, because: "the existing workplace is used");
		result.Workplace.BindingName.Should().BeNull(because: "the workplace row belongs to the other package");
		_env.Bindings.Values.Where(binding => binding.Schema == WorkplacePlacementService.WorkplaceSchema)
			.Should().NotContain(binding => binding.Rows.ContainsKey(sales),
				because: "binding a shared workplace lets either install overwrite or remove it");
		result.Warnings.Should().Contain(warning => warning.Contains(FakeEnvironment.OtherPackage),
			because: "the caller learns the dependency on the owning package");
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionMoved, because: "the section is still placed");
	}

	[Test]
	[Description("Refuses, before any write, a position change for a workplace another package ships.")]
	public void Place_ShouldThrowBeforeWriting_WhenPositionTargetsAnotherPackagesWorkplace() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid sales = _env.SeedWorkplace(WorkplaceName);
		_env.OtherPackageBindings["SysWorkplace_Sales"] = ("SysWorkplace", new() {
			[sales] = new() { ["Id"] = sales.ToString() }
		});

		// Act
		Action act = () => _sut.Place(Request(position: 2));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*position*",
			because: "the position of another package's workplace is not this package's to change");
		_env.Upserts.Should().BeEmpty(because: "the refusal comes before the first write");
	}

	[Test]
	[Description("Reads the target workplaces once per call and names the section's workplaces from the placement read, not one read per workplace.")]
	public void Place_ShouldNotRepeatReads() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		_env.FindWorkplacesCalls[WorkplaceName].Should().Be(1, because: "the loaded list is passed to every step");
		_env.SectionPlacementReads.Should().Be(2, because: "one read before the writes and one read-back after them");
		result.SectionWorkplaces.Should().ContainSingle(location => location.Name == WorkplaceName,
			because: "the name comes with the placement read");
	}

	[Test]
	[Description("Adds the section and warns when My applications cannot be identified, instead of silently leaving a second placement.")]
	public void Place_ShouldWarn_WhenMyApplicationsCannotBeIdentified() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.RenameMyApplications("Мої застосунки", withBinding: false);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionCreated,
			because: "nothing identifies the placement to move");
		result.Warnings.Should().Contain(warning => warning.Contains("could not be identified"),
			because: "the caller asked for a move and must learn it did not happen");
	}

	[Test]
	[Description("Never names a workplace binding SysWorkplace_MyApps, which identifies My applications.")]
	public void WorkplaceBindingName_ShouldAvoidMyApplicationsBinding() {
		// Act
		string name = WorkplacePlacementService.WorkplaceBindingName("MyApps");

		// Assert
		name.Should().NotBe(WorkplacePlacementService.MyApplicationsBindingName,
			because: "a user workplace in that binding would be taken for My applications");
		WorkplacePlacementService.WorkplaceBindingName("Orders").Should().Be("SysWorkplace_Orders",
			because: "other names keep the plain form");
	}

	[Test]
	[Description("Warns when the platform left Type or LoaderId empty, because the binding cannot carry them.")]
	public void Place_ShouldWarn_WhenTypeOrLoaderIsEmpty() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.FillDefaultsOnInsert = false;

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Warnings.Should().Contain(warning => warning.Contains("empty Type or LoaderId"),
			because: "an incomplete binding installs a broken workplace elsewhere");
	}

	[Test]
	[Description("mode=existing refuses a workplace that does not exist, before any write.")]
	public void Place_ShouldThrowBeforeWriting_WhenModeIsExistingAndWorkplaceIsMissing() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		Action act = () => _sut.Place(Request() with { Mode = "existing" });

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*mode=new*",
			because: "the error tells the caller how to create the workplace instead");
		_env.Upserts.Should().BeEmpty(because: "nothing is written when the mode cannot be satisfied");
	}

	[Test]
	[Description("mode=existing reuses the named workplace.")]
	public void Place_ShouldReuseWorkplace_WhenModeIsExisting() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid existing = _env.SeedWorkplace(WorkplaceName);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request() with { Mode = "existing" });

		// Assert
		result.Workplace.Id.Should().Be(existing, because: "the caller asked for the existing workplace");
		result.SectionWorkplaces.Select(location => location.Id).Should().Equal([existing],
			because: "the section was moved, so it is only in the target now");
	}

	[Test]
	[Description("workplace-type=portal creates the workplace with the Portal type on the live insert, and its binding carries Type, LoaderId and the client type.")]
	public void Place_ShouldCreatePortalWorkplace_WhenWorkplaceTypeIsPortal() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request() with { WorkplaceType = "portal" });

		// Assert
		_env.WorkplaceInserts.Single()["Type"].Should().Be(FakeEnvironment.PortalTypeId.ToString(),
			because: "the type is set on the insert, not left to the platform default");
		result.Workplace.Type.Should().Be("portal", because: "the result reports the type read back");
		IReadOnlyDictionary<string, string> bound = _env.BoundRow("SysWorkplace_Orders", result.Workplace.Id);
		bound["Type"].Should().Be(FakeEnvironment.PortalTypeId.ToString(),
			because: "an install creates the workplace from the binding, so it must carry the Portal type");
		bound["LoaderId"].Should().Be(FakeEnvironment.DefaultLoaderId.ToString(),
			because: "LoaderId is still read back from the platform when Type is passed explicitly");
		bound.Keys.Should().Contain("SysApplicationClientType", because: "a workplace without a client type does not render");
	}

	[Test]
	[Description("Without workplace-type the insert sends no Type and the result reports the platform default as general.")]
	public void Place_ShouldLeaveTypeToPlatform_WhenWorkplaceTypeIsOmitted() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		_env.WorkplaceInserts.Single().Keys.Should().NotContain("Type", because: "the behaviour without the argument is unchanged");
		result.Workplace.Type.Should().Be("general", because: "the default type is named by its code, not its translated name");
	}

	[TestCase("existing")]
	[TestCase(null)]
	[Description("A same-named workplace of another type is refused before any write and is never retyped.")]
	public void Place_ShouldRefuseBeforeWriting_WhenExistingWorkplaceHasAnotherType(string mode) {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid existing = _env.SeedWorkplace(WorkplaceName);

		// Act
		Action act = () => _sut.Place(Request() with { Mode = mode, WorkplaceType = "portal" });

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*type general, not portal*never changes*",
			because: "the error names the current type and says it was not changed");
		_env.Workplaces.Single(workplace => workplace.Id == existing).TypeId.Should().Be(FakeEnvironment.DefaultTypeId,
			because: "the existing workplace keeps its type");
		_env.Upserts.Should().BeEmpty(because: "the type is checked before the first write");
		_env.CreatedBindings.Should().BeEmpty(because: "nothing is written when the type does not match");
	}

	[Test]
	[Description("Among same-named workplaces, the one of the requested type is reused.")]
	public void Place_ShouldReuseWorkplaceOfRequestedType_WhenSeveralShareTheName() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.SeedWorkplace(WorkplaceName);
		Guid portal = _env.SeedWorkplace(WorkplaceName, FakeEnvironment.PortalTypeId);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request() with { WorkplaceType = "portal" });

		// Assert
		result.Workplace.Id.Should().Be(portal, because: "the name and the type both have to match");
		result.Workplace.Type.Should().Be("portal", because: "the reused workplace's type is reported");
	}

	[Test]
	[Description("Without workplace-type, the same-named workplace that already holds the section is reused and the move out of My applications is skipped, so no role is needed.")]
	public void Place_ShouldReuseHolderAndSkipMove_WhenSeveralShareTheNameWithoutWorkplaceType() {
		// Arrange
		Guid myAppsRow = _env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.SeedWorkplace(WorkplaceName);
		Guid holder = _env.SeedWorkplace(WorkplaceName);
		_env.Placements.Add(new WorkplaceJunctionRecord(Guid.NewGuid(), holder, FakeEnvironment.SectionId));

		// Act
		WorkplacePlacementResult result = _sut.Place(Request(roles: []));

		// Assert
		result.Workplace.Id.Should().Be(holder, because: "the workplace that already holds the section wins over the oldest");
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionAlreadyPresent,
			because: "the section is already in the reused workplace");
		_env.Placements.Single(row => row.Id == myAppsRow).WorkplaceId.Should().Be(FakeEnvironment.MyApplicationsId,
			because: "the move check is skipped, so the My applications row is neither refused nor moved");
	}

	[Test]
	[Description("With workplace-type, a same-named workplace of the requested type is reused even when another one holds the section, and the move check runs for it: no role sees it, so the move is refused before any write.")]
	public void Place_ShouldRefuseMoveIntoRequestedTypeWorkplace_WhenAnotherSameNamedOneHoldsTheSectionAndNoRoleSeesIt() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid general = _env.SeedWorkplace(WorkplaceName);
		_env.Placements.Add(new WorkplaceJunctionRecord(Guid.NewGuid(), general, FakeEnvironment.SectionId));
		_env.SeedWorkplace(WorkplaceName, FakeEnvironment.PortalTypeId);

		// Act
		Action act = () => _sut.Place(Request(roles: []) with { WorkplaceType = "portal" });

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*hide the app*",
			because: "the Portal workplace is the target, and no role sees it");
		_env.Upserts.Should().BeEmpty(because: "the refusal comes before the first write");
	}

	[Test]
	[Description("With workplace-type, the My applications row is moved into the same-named workplace of the requested type, not left because another same-named workplace holds the section.")]
	public void Place_ShouldMoveIntoRequestedTypeWorkplace_WhenAnotherSameNamedOneHoldsTheSection() {
		// Arrange
		Guid myAppsRow = _env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid general = _env.SeedWorkplace(WorkplaceName);
		_env.Placements.Add(new WorkplaceJunctionRecord(Guid.NewGuid(), general, FakeEnvironment.SectionId));
		Guid portal = _env.SeedWorkplace(WorkplaceName, FakeEnvironment.PortalTypeId);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request() with { WorkplaceType = "portal" });

		// Assert
		result.Workplace.Id.Should().Be(portal, because: "the name and the type both have to match");
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionMoved,
			because: "the section is not in the Portal workplace yet");
		result.Section.PlacementId.Should().Be(myAppsRow, because: "the My applications row is moved, not a new one added");
		_env.Placements.Single(row => row.Id == myAppsRow).WorkplaceId.Should().Be(portal,
			because: "the moved row now points at the Portal workplace");
		_env.Upserts.Should().Contain("SysModuleInWorkplace_UsrOrders",
			because: "the moved row is written through its existing binding");
	}

	[Test]
	[Description("The platform-seeded type id wins over another row that shares its code, so a custom type is never taken for General.")]
	public void Place_ShouldPreferSeededTypeId_WhenAnotherRowSharesTheCode() {
		// Arrange
		Guid seededGeneral = new("000a9225-728b-4778-a951-c42439ffe024");
		_env.WorkplaceTypes.Clear();
		_env.WorkplaceTypes.AddRange([new(Guid.NewGuid(), "Partner", 0), new(seededGeneral, "General", 0)]);
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request() with { WorkplaceType = "general" });

		// Assert
		_env.WorkplaceInserts.Single()["Type"].Should().Be(seededGeneral.ToString(),
			because: "a code shared by two rows does not identify a type; the seeded id does");
		result.Workplace.Type.Should().Be("general", because: "the seeded General row is reported by its argument value");
	}

	[Test]
	[Description("Refuses an unknown workplace-type value and a type the environment does not have, before any write.")]
	public void Place_ShouldThrowBeforeWriting_WhenWorkplaceTypeIsUnknownOrMissing() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		Action unknown = () => _sut.Place(Request() with { WorkplaceType = "external" });
		Action missing = () => _sut.Place(Request() with { WorkplaceType = "self-service" });

		// Assert
		unknown.Should().Throw<InvalidOperationException>().WithMessage("*general*portal*self-service*",
			because: "the error lists the accepted values");
		missing.Should().Throw<InvalidOperationException>().WithMessage("*self-service*not found*",
			because: "a type the environment lacks cannot be set");
		_env.Upserts.Should().BeEmpty(because: "the type is resolved before the first write");
	}

	[Test]
	[Description("A custom SysWorkplaceType row with an empty Code is not taken for General, so the code fallback still finds the only General row.")]
	public void Place_ShouldIgnoreTypeRowWithoutCode_WhenResolvingByCode() {
		// Arrange
		_env.WorkplaceTypes.Add(new(Guid.NewGuid(), "Custom", null));
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request() with { WorkplaceType = "general" });

		// Assert
		_env.WorkplaceInserts.Single()["Type"].Should().Be(FakeEnvironment.DefaultTypeId.ToString(),
			because: "a row with no code must not make code 0 ambiguous");
		result.Workplace.Type.Should().Be("general", because: "the General row is reported by its argument value");
	}

	[Test]
	[Description("Moving the section out of My applications into a portal workplace warns that internal users no longer reach it there; adding a placement does not.")]
	public void Place_ShouldWarn_WhenSectionIsMovedIntoNonGeneralWorkplace() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult moved = _sut.Place(Request() with { WorkplaceType = "portal" });
		WorkplacePlacementResult general = _sut.Place(Request(workplace: "Back office"));

		// Assert
		moved.Section.Action.Should().Be(WorkplacePlacementService.ActionMoved, because: "the default moves the row");
		moved.Warnings.Should().Contain(warning => warning.Contains("portal workplace")
			&& warning.Contains("remove-from-my-applications=false"),
			because: "the caller learns that internal users lost the app from My applications and how to keep it");
		general.Warnings.Should().NotContain(warning => warning.Contains("internal users"),
			because: "a placement into a General workplace needs no such warning");
	}

	[Test]
	[Description("No warning about internal users when the portal placement is added and the section stays in My applications.")]
	public void Place_ShouldNotWarnAboutInternalUsers_WhenPlacementIsAddedToPortalWorkplace() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request(remove: false) with { WorkplaceType = "portal" });

		// Assert
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionCreated, because: "a second placement is added");
		result.Warnings.Should().NotContain(warning => warning.Contains("internal users"),
			because: "the section is still in My applications");
	}

	[Test]
	[Description("When the platform does not apply the requested Type on the insert, the call fails after its first write instead of shipping a workplace of the wrong type.")]
	public void Place_ShouldFailAfterFirstWrite_WhenPlatformIgnoresRequestedType() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.IgnoreTypeOnInsert = true;

		// Act
		WorkplacePlacementResult result = _sut.Place(Request() with { WorkplaceType = "portal" });

		// Assert
		result.Error.Should().Contain("did not apply the requested type",
			because: "a success reporting type general would ship a General binding for a portal request");
		result.Written.Should().NotBeEmpty(because: "the workplace row was inserted before the type was read back");
		Guid workplaceId = _env.FindWorkplaces(WorkplaceName, FakeEnvironment.WebClientTypeId).Single().Id;
		_env.BoundRow("SysWorkplace_Orders", workplaceId).Keys.Should().NotContain("LoaderId",
			because: "the binding was not completed for a workplace of the wrong type");
	}

	[Test]
	[Description("mode=new with workplace-type refuses a same-named workplace of the requested type that the package does not ship, even when the package ships another same-named one.")]
	public void Place_ShouldRefuseForeignWorkplaceOfRequestedType_WhenModeIsNew() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid own = _env.SeedWorkplace(WorkplaceName);
		_env.Bindings["SysWorkplace_Orders"] = ("SysWorkplace", new() {
			[own] = new() { ["Id"] = own.ToString() }
		});
		_env.SeedWorkplace(WorkplaceName, FakeEnvironment.PortalTypeId);

		// Act
		Action act = () => _sut.Place(Request() with { Mode = "new", WorkplaceType = "portal" });

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*",
			because: "the Portal workplace was not made by this package, so mode=new must not use it");
		_env.Upserts.Should().BeEmpty(because: "the mode is checked before the first write");
	}

	[Test]
	[Description("mode=none refuses workplace-type, because no type is set or checked when nothing is written.")]
	public void Place_ShouldThrow_WhenModeIsNoneAndWorkplaceTypeIsGiven() {
		// Act
		Action act = () => _sut.Place(Request(roles: [], workplace: null) with { Mode = "none", WorkplaceType = "bogus" });

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*omit workplace-type*",
			because: "the caller must not believe the type was checked");
	}

	[Test]
	[Description("mode=new refuses a name another workplace already uses, but converges on its own re-run.")]
	public void Place_ShouldRefuseTakenNameButConverge_WhenModeIsNew() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.SeedWorkplace("Taken");

		// Act
		Action taken = () => _sut.Place(Request(workplace: "Taken") with { Mode = "new" });
		WorkplacePlacementResult first = _sut.Place(Request() with { Mode = "new" });
		WorkplacePlacementResult second = _sut.Place(Request() with { Mode = "new" });

		// Assert
		taken.Should().Throw<InvalidOperationException>().WithMessage("*already exists*",
			because: "a second same-named workplace would compete for every binding");
		first.Workplace.Created.Should().BeTrue(because: "the name was free");
		second.Workplace.Id.Should().Be(first.Workplace.Id,
			because: "a re-run finds its own workplace holding the section instead of refusing");
	}

	[Test]
	[Description("mode=new finishes an earlier call that created and bound the workplace but failed before placing the section.")]
	public void Place_ShouldConverge_WhenModeIsNewAndEarlierRunStoppedBeforePlacing() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		Guid own = _env.SeedWorkplace(WorkplaceName);
		_env.Bindings["SysWorkplace_Orders"] = ("SysWorkplace", new() {
			[own] = new() { ["Id"] = own.ToString() }
		});

		// Act
		WorkplacePlacementResult result = _sut.Place(Request() with { Mode = "new" });

		// Assert
		result.Workplace.Id.Should().Be(own, because: "the package already ships this workplace, so it is its own");
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionMoved, because: "the re-run finishes the move");
	}

	[Test]
	[Description("mode=none writes nothing and reports where the section is.")]
	public void Place_ShouldOnlyReport_WhenModeIsNone() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);

		// Act
		WorkplacePlacementResult result = _sut.Place(Request(roles: [], workplace: null) with { Mode = "none" });

		// Assert
		result.Workplace.Should().BeNull(because: "no workplace is targeted");
		result.SectionWorkplaces.Single().Name.Should().Be(WorkplacePlacementService.MyApplicationsWorkplaceName,
			because: "the section stays where create-app put it");
		_env.Upserts.Should().BeEmpty(because: "mode=none never writes");
		_packageResolver.DidNotReceiveWithAnyArgs().Resolve(default, default);
	}

	[Test]
	[Description("mode=none refuses roles, because a grant would be a write.")]
	public void Place_ShouldThrow_WhenModeIsNoneAndRolesAreGiven() {
		// Act
		Action act = () => _sut.Place(Request() with { Mode = "none" });

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*omit roles*",
			because: "the caller must not believe a role was granted");
	}

	[Test]
	[Description("Finds My applications through create-app's SysWorkplace_MyApps binding when the profile culture renames it, so the section is still moved, not duplicated.")]
	public void Place_ShouldMoveSection_WhenMyApplicationsNameIsLocalized() {
		// Arrange
		Guid myAppsRow = _env.SeedMyApplicationsPlacement(bindInPackage: true);
		_env.RenameMyApplications("Мої застосунки");

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Section.Action.Should().Be(WorkplacePlacementService.ActionMoved,
			because: "the shared workplace is identified by its binding, not by its localizable name");
		result.Section.PlacementId.Should().Be(myAppsRow, because: "the existing row changes parent");
	}

	[Test]
	[Description("Resets clio's navigation cache after a write, relays a failed reset as a warning, and returns the re-login and browser notes.")]
	public void Place_ShouldResetNavigationCacheAndReturnNextStep() {
		// Arrange
		_env.SeedMyApplicationsPlacement(bindInPackage: true);
		_resetter.TryReset(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>()).Returns("reset failed");

		// Act
		WorkplacePlacementResult result = _sut.Place(Request());

		// Assert
		result.Warnings.Should().Contain("reset failed", because: "a failed reset is reported, not hidden");
		result.NextStep.Should().Contain("log out").And.Contain("browser note",
			because: "the user needs both the re-login rule and the in-tab refresh call");
	}

	[TestCase("Orders app", "UsrX", "Ordersapp")]
	[TestCase("Замовлення", "UsrOrders", "UsrOrders")]
	[TestCase("Sales 2", "UsrX", "Sales2")]
	[Description("Builds a binding suffix from the ASCII letters and digits of the name, or of the fallback.")]
	public void BuildBindingSuffix_ShouldKeepAsciiLettersAndDigits(string name, string fallback, string expected) {
		// Act
		string suffix = WorkplacePlacementService.BuildBindingSuffix(name, fallback);

		// Assert
		suffix.Should().Be(expected, because: "a binding name is a folder name and must stay ASCII");
	}

	/// <summary>
	/// In-memory stand-in for the three workplace tables and the package bindings. Its binding service writes the
	/// live row the way the real one does (insert by new Id, update by existing Id) and records the bound rows.
	/// </summary>
	private sealed class FakeEnvironment : IWorkplaceRepository, IDataBindingDbService {

		internal const string OtherPackage = "UsrOtherApp";
		internal static readonly Guid PackageUId = Guid.NewGuid();
		internal static readonly Guid WebClientTypeId = Guid.NewGuid();
		internal static readonly Guid SectionId = Guid.NewGuid();
		internal static readonly Guid MyApplicationsId = Guid.NewGuid();
		internal static readonly Guid SalesManagersId = Guid.NewGuid();
		internal static readonly Guid DefaultTypeId = Guid.NewGuid();
		internal static readonly Guid DefaultLoaderId = Guid.NewGuid();
		internal static readonly Guid PortalTypeId = Guid.NewGuid();

		internal List<WorkplaceRecord> Workplaces { get; } = [];
		internal List<WorkplaceJunctionRecord> Placements { get; } = [];
		internal List<WorkplaceJunctionRecord> Grants { get; } = [];
		internal List<AdminUnitRecord> AdminUnits { get; } = [new(SalesManagersId, "Sales managers")];
		internal Dictionary<string, (string Schema, Dictionary<Guid, Dictionary<string, string>> Rows)> Bindings { get; }
			= new(StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, (string Schema, Dictionary<Guid, Dictionary<string, string>> Rows)> OtherPackageBindings { get; }
			= new(StringComparer.OrdinalIgnoreCase);
		internal string FailUpsertOfSchema { get; set; }
		internal bool FailCreateBinding { get; set; }
		internal List<string> ReadBindingPackages { get; } = [];
		internal Dictionary<string, int> FindWorkplacesCalls { get; } = [];
		internal int SectionPlacementReads { get; private set; }
		internal int ShippingRowLookups { get; private set; }
		internal HashSet<string> UnparseableBindings { get; } = new(StringComparer.OrdinalIgnoreCase);
		internal List<string> CreatedBindings { get; } = [];
		internal List<string> Upserts { get; } = [];
		internal bool FillDefaultsOnInsert { get; set; } = true;
		internal bool IgnoreTypeOnInsert { get; set; }
		internal List<Dictionary<string, string>> WorkplaceInserts { get; } = [];

		// Ids are not the seeded ones on purpose: a type is found by its culture-independent code.
		internal List<WorkplaceTypeRecord> WorkplaceTypes { get; } = [
			new(DefaultTypeId, "Allgemein", 0),
			new(PortalTypeId, "Portal", 1)
		];

		internal FakeEnvironment() {
			Workplaces.Add(new WorkplaceRecord(MyApplicationsId, WorkplacePlacementService.MyApplicationsWorkplaceName, 1,
				WebClientTypeId, DefaultTypeId, DefaultLoaderId));
		}

		internal Guid SeedMyApplicationsPlacement(bool bindInPackage) {
			Guid rowId = Guid.NewGuid();
			Placements.Add(new WorkplaceJunctionRecord(rowId, MyApplicationsId, SectionId));
			if (bindInPackage) {
				Bindings["SysModuleInWorkplace_UsrOrders"] = ("SysModuleInWorkplace", new() {
					[rowId] = new() { ["Id"] = rowId.ToString(), ["SysWorkplace"] = MyApplicationsId.ToString() }
				});
			}
			return rowId;
		}

		internal void RenameMyApplications(string name, bool withBinding = true) {
			WorkplaceRecord myApps = GetWorkplace(MyApplicationsId);
			Workplaces[Workplaces.IndexOf(myApps)] = myApps with { Name = name };
			if (!withBinding) {
				return;
			}
			Bindings[WorkplacePlacementService.MyApplicationsBindingName] = ("SysWorkplace", new() {
				[MyApplicationsId] = new() { ["Id"] = MyApplicationsId.ToString() }
			});
		}

		internal Guid SeedWorkplace(string name, Guid? typeId = null) {
			Guid id = Guid.NewGuid();
			Workplaces.Add(new WorkplaceRecord(id, name, 5, WebClientTypeId, typeId ?? DefaultTypeId, DefaultLoaderId));
			return id;
		}

		internal IReadOnlyDictionary<string, string> BoundRow(string bindingName, Guid id) =>
			Bindings.TryGetValue(bindingName, out var binding) && binding.Rows.TryGetValue(id, out var row) ? row : null;

		public Guid? FindClientTypeId(string name) => name == "Web" ? WebClientTypeId : null;

		public IReadOnlyList<WorkplaceTypeRecord> FindWorkplaceTypes() => WorkplaceTypes;

		public IReadOnlyList<SectionModuleRecord> FindSectionsByCode(string code) =>
			code == SectionCode ? [new SectionModuleRecord(SectionId, SectionCode)] : [];

		public IReadOnlyList<WorkplaceRecord> FindWorkplaces(string name, Guid clientTypeId) {
			FindWorkplacesCalls[name] = FindWorkplacesCalls.GetValueOrDefault(name) + 1;
			return Workplaces.Where(w => w.Name == name && w.ClientTypeId == clientTypeId).ToList();
		}

		public WorkplaceRecord GetWorkplace(Guid id) => Workplaces.FirstOrDefault(w => w.Id == id);

		public IReadOnlyList<WorkplaceJunctionRecord> FindSectionPlacements(Guid sectionId) {
			SectionPlacementReads++;
			return Placements.Where(p => p.TargetId == sectionId)
				.Select(p => p with { WorkplaceName = GetWorkplace(p.WorkplaceId)?.Name }).ToList();
		}

		public IReadOnlyList<WorkplaceJunctionRecord> FindRoleGrants(Guid workplaceId) =>
			Grants.Where(g => g.WorkplaceId == workplaceId).ToList();

		public IReadOnlyList<AdminUnitRecord> FindAdminUnitsByName(string name) =>
			AdminUnits.Where(a => a.Name == name).ToList();

		public AdminUnitRecord GetAdminUnit(Guid id) => AdminUnits.FirstOrDefault(a => a.Id == id);

		public IReadOnlyList<string> FindPackagesShippingBinding(string bindingName) =>
			Bindings.ContainsKey(bindingName) ? [OtherPackage] : [];

		public IReadOnlyList<PackageBindingRecord> FindBindingsShippingRow(string entitySchemaName, Guid rowId) {
			ShippingRowLookups++;
			return Bindings.Where(b => b.Value.Schema == entitySchemaName && b.Value.Rows.ContainsKey(rowId))
				.Select(b => new PackageBindingRecord(Package, b.Key))
				.Concat(OtherPackageBindings.Where(b => b.Value.Schema == entitySchemaName
						&& (UnparseableBindings.Contains(b.Key) || b.Value.Rows.ContainsKey(rowId)))
					.Select(b => new PackageBindingRecord(OtherPackage, b.Key, UnparseableBindings.Contains(b.Key))))
				.ToList();
		}

		public IReadOnlyList<string> FindBindingNames(Guid packageUId, string entitySchemaName) =>
			Bindings.Where(b => b.Value.Schema == entitySchemaName).Select(b => b.Key).ToList();

		public DataBindingResult CreateBinding(CreateDataBindingDbOptions options) {
			options.Environment.Should().Be(Environment, because: "every binding call targets the caller's environment");
			options.RowsJson.Should().BeNull(because: "rows go in through the Id-matched upsert, never the Name-matched create");
			if (FailCreateBinding) {
				throw new InvalidOperationException($"Creating {options.BindingName} failed.");
			}
			Bindings[options.BindingName] = (options.SchemaName, new());
			CreatedBindings.Add(options.BindingName);
			return new DataBindingResult(options.BindingName, [], []);
		}

		public void UpsertRow(UpsertDataBindingRowDbOptions options) {
			var binding = Bindings[options.BindingName];
			if (binding.Schema == FailUpsertOfSchema) {
				throw new InvalidOperationException($"Upsert into {options.BindingName} failed.");
			}
			Upserts.Add(options.BindingName);
			Dictionary<string, string> values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(options.ValuesJson)
				.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
			Guid id = Guid.Parse(values["Id"]);
			switch (binding.Schema) {
				case "SysWorkplace":
					WriteWorkplace(id, values);
					break;
				case "SysModuleInWorkplace":
					Write(Placements, id, Guid.Parse(values["SysWorkplace"]), Guid.Parse(values["SysModule"]));
					break;
				case "SysAdminUnitInWorkplace":
					Write(Grants, id, Guid.Parse(values["SysWorkplace"]), Guid.Parse(values["SysAdminUnit"]));
					break;
			}
			// Like the real binding, the projection keeps every column it ever carried.
			Dictionary<string, string> merged = binding.Rows.TryGetValue(id, out var bound) ? new(bound) : [];
			foreach (KeyValuePair<string, string> value in values) {
				merged[value.Key] = value.Value;
			}
			binding.Rows[id] = merged;
		}

		private void WriteWorkplace(Guid id, Dictionary<string, string> values) {
			WorkplaceRecord existing = GetWorkplace(id);
			if (existing is null) {
				WorkplaceInserts.Add(values);
				Guid typeId = values.TryGetValue("Type", out string type) && !IgnoreTypeOnInsert ? Guid.Parse(type)
					: FillDefaultsOnInsert ? DefaultTypeId : Guid.Empty;
				Workplaces.Add(new WorkplaceRecord(id, values["Name"], 9, Guid.Parse(values["SysApplicationClientType"]),
					typeId, FillDefaultsOnInsert ? DefaultLoaderId : Guid.Empty));
				return;
			}
			Workplaces[Workplaces.IndexOf(existing)] = existing with {
				Position = values.TryGetValue("Position", out string position) ? int.Parse(position) : existing.Position
			};
		}

		private static void Write(List<WorkplaceJunctionRecord> table, Guid id, Guid workplaceId, Guid targetId) {
			table.RemoveAll(row => row.Id == id);
			table.Add(new WorkplaceJunctionRecord(id, workplaceId, targetId));
		}

		public void RemoveRow(RemoveDataBindingRowDbOptions options) =>
			throw new AssertionException("The placement tool must never delete a row.");

		public BoundBindingProjection ReadBinding(ReadDataBindingDbOptions options) {
			ReadBindingPackages.Add(options.PackageName);
			var binding = options.PackageName == OtherPackage && OtherPackageBindings.ContainsKey(options.BindingName)
				? OtherPackageBindings[options.BindingName]
				: Bindings[options.BindingName];
			return new BoundBindingProjection(options.BindingName, binding.Schema, Guid.NewGuid(),
				binding.Rows.Values.Select(row => (IReadOnlyDictionary<string, string>)row).ToList());
		}
	}
}
