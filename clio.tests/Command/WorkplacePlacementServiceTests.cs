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
		Action act = () => _sut.Place(Request());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the grant write failed");
		_env.Placements.Single().WorkplaceId.Should().Be(FakeEnvironment.MyApplicationsId,
			because: "the placement is moved only after every grant is written");
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

		internal List<WorkplaceRecord> Workplaces { get; } = [];
		internal List<WorkplaceJunctionRecord> Placements { get; } = [];
		internal List<WorkplaceJunctionRecord> Grants { get; } = [];
		internal List<AdminUnitRecord> AdminUnits { get; } = [new(SalesManagersId, "Sales managers")];
		internal Dictionary<string, (string Schema, Dictionary<Guid, Dictionary<string, string>> Rows)> Bindings { get; }
			= new(StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, (string Schema, Dictionary<Guid, Dictionary<string, string>> Rows)> OtherPackageBindings { get; }
			= new(StringComparer.OrdinalIgnoreCase);
		internal string FailUpsertOfSchema { get; set; }
		internal List<string> CreatedBindings { get; } = [];
		internal List<string> Upserts { get; } = [];
		internal bool FillDefaultsOnInsert { get; set; } = true;

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

		internal Guid SeedWorkplace(string name) {
			Guid id = Guid.NewGuid();
			Workplaces.Add(new WorkplaceRecord(id, name, 5, WebClientTypeId, DefaultTypeId, DefaultLoaderId));
			return id;
		}

		internal IReadOnlyDictionary<string, string> BoundRow(string bindingName, Guid id) =>
			Bindings.TryGetValue(bindingName, out var binding) && binding.Rows.TryGetValue(id, out var row) ? row : null;

		public Guid? FindClientTypeId(string name) => name == "Web" ? WebClientTypeId : null;

		public IReadOnlyList<SectionModuleRecord> FindSectionsByCode(string code) =>
			code == SectionCode ? [new SectionModuleRecord(SectionId, SectionCode)] : [];

		public IReadOnlyList<WorkplaceRecord> FindWorkplaces(string name, Guid clientTypeId) =>
			Workplaces.Where(w => w.Name == name && w.ClientTypeId == clientTypeId).ToList();

		public WorkplaceRecord GetWorkplace(Guid id) => Workplaces.FirstOrDefault(w => w.Id == id);

		public IReadOnlyList<WorkplaceJunctionRecord> FindSectionPlacements(Guid sectionId) =>
			Placements.Where(p => p.TargetId == sectionId).ToList();

		public IReadOnlyList<WorkplaceJunctionRecord> FindRoleGrants(Guid workplaceId) =>
			Grants.Where(g => g.WorkplaceId == workplaceId).ToList();

		public IReadOnlyList<AdminUnitRecord> FindAdminUnitsByName(string name) =>
			AdminUnits.Where(a => a.Name == name).ToList();

		public AdminUnitRecord GetAdminUnit(Guid id) => AdminUnits.FirstOrDefault(a => a.Id == id);

		public IReadOnlyList<string> FindPackagesShippingBinding(string bindingName) =>
			Bindings.ContainsKey(bindingName) ? [OtherPackage] : [];

		public IReadOnlyList<PackageBindingRecord> FindBindingsOfSchema(string entitySchemaName) =>
			Bindings.Where(b => b.Value.Schema == entitySchemaName).Select(b => new PackageBindingRecord(Package, b.Key))
				.Concat(OtherPackageBindings.Where(b => b.Value.Schema == entitySchemaName)
					.Select(b => new PackageBindingRecord(OtherPackage, b.Key)))
				.ToList();

		public IReadOnlyList<string> FindBindingNames(Guid packageUId, string entitySchemaName) =>
			Bindings.Where(b => b.Value.Schema == entitySchemaName).Select(b => b.Key).ToList();

		public DataBindingResult CreateBinding(CreateDataBindingDbOptions options) {
			options.Environment.Should().Be(Environment, because: "every binding call targets the caller's environment");
			options.RowsJson.Should().BeNull(because: "rows go in through the Id-matched upsert, never the Name-matched create");
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
				Workplaces.Add(new WorkplaceRecord(id, values["Name"], 9, Guid.Parse(values["SysApplicationClientType"]),
					FillDefaultsOnInsert ? DefaultTypeId : Guid.Empty, FillDefaultsOnInsert ? DefaultLoaderId : Guid.Empty));
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
			var binding = options.PackageName == OtherPackage && OtherPackageBindings.ContainsKey(options.BindingName)
				? OtherPackageBindings[options.BindingName]
				: Bindings[options.BindingName];
			return new BoundBindingProjection(options.BindingName, binding.Schema, Guid.NewGuid(),
				binding.Rows.Values.Select(row => (IReadOnlyDictionary<string, string>)row).ToList());
		}
	}
}
