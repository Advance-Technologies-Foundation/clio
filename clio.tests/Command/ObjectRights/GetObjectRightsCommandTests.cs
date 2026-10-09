using System;
using System.Linq;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

[TestFixture]
[Property("Module", "Command")]
public class GetObjectRightsCommandTests : BaseCommandTests<GetObjectRightsOptions> {

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private GetObjectRightsCommand _command;
	private IObjectRightsReader _rightsReader;
	private IConnectedObjectsResolver _connectedObjects;
	private IObjectRecordCounter _recordCounter;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetObjectRightsCommand>();
	}

	public override void TearDown() {
		_rightsReader.ClearReceivedCalls();
		_connectedObjects.ClearReceivedCalls();
		_recordCounter.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_rightsReader = Substitute.For<IObjectRightsReader>();
		_connectedObjects = Substitute.For<IConnectedObjectsResolver>();
		_recordCounter = Substitute.For<IObjectRecordCounter>();
		_logger = Substitute.For<ILogger>();
		// Default: no fan-out — the resolver returns just the root object.
		_connectedObjects.Resolve(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int?>())
			.Returns(callInfo => Resolution((string)callInfo[0]));
		containerBuilder.AddTransient(_ => _rightsReader);
		containerBuilder.AddTransient(_ => _connectedObjects);
		containerBuilder.AddTransient(_ => _recordCounter);
		containerBuilder.AddTransient(_ => _logger);
	}

	private const string RecordsOff = "    Record permissions: OFF, no default record rules.";

	private static ConnectedObjectsResolution Resolution(params string[] objects) =>
		new(objects, Array.Empty<string>());

	private static ObjectRightsInfo Administered(string name, params RoleOperationRights[] roles) =>
		new(true, name, name, true, roles);

	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private static RoleOperationRights RoleRow(bool read, bool create, bool edit, bool del, int position = 0) =>
		new(Role, "Sales managers", position, read, create, edit, del);

	private static RoleOperationRights EmployeesRow(int position) =>
		new(Employees, "All employees", position, true, true, true, true);

	private static ObjectRightsInfo NotAdministered(string name) =>
		new(true, name, name, false, Array.Empty<RoleOperationRights>());

	[Test]
	[Description("Without a grantee filter, reports every role's operations on the object.")]
	public void Execute_ShouldReportAllRoles_WhenNoGrantee() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder",
				RoleRow(true, true, true, false),
				EmployeesRow(1)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo($"    [0] Sales managers ({Role}): read/create/edit");
		_logger.Received().WriteInfo($"    [1] All employees ({Employees}): read/create/edit/delete");
		_logger.Received().WriteInfo($"  {GetObjectRightsCommand.PriorityRule}");
	}

	[Test]
	[Description("With a grantee filter, reports exactly the operations that role holds on each object — facts only, no coverage verdict.")]
	public void Execute_ShouldReportGranteeOperationsPerObjectWithoutVerdict_WhenIncludeConnectedIsSet() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", RoleRow(true, true, true, false)));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrStatus", RoleRow(true, false, false, false)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Role.ToString(), IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo($"    [0] Sales managers ({Role}): read/create/edit");
		_logger.Received().WriteInfo($"    [0] Sales managers ({Role}): read");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("can read") || m.Contains("cannot read")));
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("can read") || m.Contains("cannot read")));
	}

	[Test]
	[Description("With a grantee filter, an administered object where the role has no row is reported as having no operations granted.")]
	public void Execute_ShouldReportNoGrant_WhenGranteeHasNoRow() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Role.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a missing row is a fact, not a failure");
		_logger.Received().WriteInfo($"    grantee {Role} has NO row (no operations granted).");
	}

	[TestCase(null)]
	[TestCase("11111111-2222-3333-4444-555555555555")]
	[Description("A non-administered object is reported with the general platform fact: open to internal users, reachable by external users only through an explicit grant — with or without a grantee.")]
	public void Execute_ShouldReportNotAdministeredFact_WhenObjectIsNotAdministered(string grantee) {
		// Arrange
		_rightsReader.GetObjectRights("UsrOpen", Arg.Any<CreatioRequestOptions>()).Returns(NotAdministered("UsrOpen"));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOpen", Grantee = grantee };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m =>
				m.Contains("not administered by operation permissions")
				&& m.Contains("available to all internal users")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("external users reach")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("get-guidance object-rights")));
	}

	[Test]
	[Description("Returns a friendly error when --entity-schema-name is empty.")]
	public void Execute_ShouldReturnError_WhenEntitySchemaNameMissing() {
		// Arrange
		GetObjectRightsOptions options = new() { EntitySchemaName = "  " };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing entity schema name is an input error");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("entity-schema-name")));
		_rightsReader.DidNotReceive().GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A read failure on the ROOT object fails with exit code 1: the named object was not read.")]
	public void Execute_ShouldReturnError_WhenRootReadFails() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOrder", null, false, Array.Empty<RoleOperationRights>(), ReadError: "Request Error"));
		_rightsReader.FindObjectsByTitle("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(new[] { new ObjectTitleMatch("UsrOther", "UsrOrder") });
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the object the caller named could not be read");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("operation permissions could not be read")));
		_rightsReader.DidNotReceiveWithAnyArgs().FindObjectsByTitle(default, default);
		_rightsReader.DidNotReceive().GetObjectRights("UsrOther", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A root object that is not found fails with exit code 1, the same answer set-object-rights gives.")]
	public void Execute_ShouldReturnError_WhenRootSchemaNotFound() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrders", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "UsrOrders", null, false, Array.Empty<RoleOperationRights>()));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrders", Grantee = Role.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing root object means nothing was read");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrOrders") && m.Contains("not found")));
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("A connected object that cannot be read or is not found is reported with a warning; the run still succeeds because the root was read.")]
	public void Execute_ShouldWarn_WhenConnectedObjectUnreadable(bool found) {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", RoleRow(true, true, true, false)));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(found
				? new ObjectRightsInfo(true, "UsrStatus", null, false, Array.Empty<RoleOperationRights>(), ReadError: "denied")
				: new ObjectRightsInfo(false, "UsrStatus", null, false, Array.Empty<RoleOperationRights>()));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the root object was read");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("UsrStatus")
				&& (m.Contains("operation permissions could not be read") || m.Contains("the schema was not found"))));
	}

	[Test]
	[Description("A failed connected-object enumeration is reported with a warning; the root is still read.")]
	public void Execute_ShouldWarn_WhenConnectedEnumerationFails() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>())
			.Returns(new ConnectedObjectsResolution(new[] { "UsrOrder" }, Array.Empty<string>(), "schema read failed"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", RoleRow(true, true, true, false)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the root object itself was read");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("Could not enumerate") && m.Contains("schema read failed")));
	}

	[Test]
	[Description("A security/system lookup excluded from the connected set is named in a warning.")]
	public void Execute_ShouldWarn_WhenConnectedObjectExcluded() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>())
			.Returns(new ConnectedObjectsResolution(new[] { "UsrOrder" }, new[] { "SysAdminUnit" }));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", RoleRow(true, true, true, false)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "an excluded lookup is a warning, not a failure");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("SysAdminUnit") && m.Contains("security/system object")));
	}

	[TestCase("not-a-guid")]
	[TestCase("00000000-0000-0000-0000-000000000000")]
	[Description("An invalid or empty-GUID --grantee is an input error and reads nothing.")]
	public void Execute_ShouldReturnError_WhenGranteeInvalid(string grantee) {
		// Arrange
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = grantee };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "grantee must be a SysAdminUnit id");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("--grantee")));
		_rightsReader.DidNotReceive().GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("An administered object with no role rows is reported as such.")]
	public void Execute_ShouldReportNoRoleGrants_WhenAdministeredWithoutRows() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>()).Returns(Administered("UsrOrder"));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.StartsWith("  UsrOrder: administered by operation permissions, with NO rows")));
	}

	[Test]
	[Description("An exception thrown by the reader is caught and reported with exit code 1.")]
	public void Execute_ShouldReturnError_WhenReaderThrows() {
		// Arrange
		_rightsReader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unexpected failure is not a success");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("boom")));
	}

	[Test]
	[Description("With a grantee, the command prints exactly the header and one fact line per object — nothing else, so no verdict can creep back in.")]
	public void Execute_ShouldPrintExactlyTheFactLines_WhenGranteeIsGiven() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus", "UsrOpen"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", RoleRow(true, true, true, false)));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrStatus", EmployeesRow(0)));
		_rightsReader.GetObjectRights("UsrOpen", Arg.Any<CreatioRequestOptions>()).Returns(NotAdministered("UsrOpen"));
		System.Collections.Generic.List<string> lines = new();
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => lines.Add((string)call[0]));
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => lines.Add((string)call[0]));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Role.ToString(), IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		lines.Should().Equal(new[] {
			$"Object permissions for 'UsrOrder' and its connected objects (grantee {Role}):",
			$"  {GetObjectRightsCommand.PriorityRule}",
			$"  {GetObjectRightsCommand.GuidancePointer}",
			"  UsrOrder: administered by operation permissions. Rows in priority order:",
			$"    [0] Sales managers ({Role}): read/create/edit",
			RecordsOff,
			"  UsrStatus: administered by operation permissions. Rows in priority order:",
			$"    grantee {Role} has NO row (no operations granted).",
			"    A new row would go below every row; these decide first for a user who is also in those roles:",
			$"      [0] All employees ({Employees}): read/create/edit/delete",
			RecordsOff,
			"  UsrOpen: not administered by operation permissions (they are OFF) — available to all internal users.",
			"    It has no 'All employees' row: set-object-rights --enable-operation-permissions adds one with "
				+ "read/create/edit/delete below any stored rows, unless the grant is for All employees itself.",
			RecordsOff
		}, because: "the output is the facts per object — the operation rows unchanged, then the record layer — and "
			+ "nothing more: no verdict, and no record count for an object with record permissions OFF and no rule");
	}

	[Test]
	[Description("With --grantee, each of a grantee's rows is reported with its own position — never merged, because which row decides depends on the positions.")]
	public void Execute_ShouldReportEachRow_WhenGranteeHasDuplicateRows() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", RoleRow(false, false, false, false, 0), RoleRow(true, false, true, false, 2)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Role.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read succeeded");
		_logger.Received().WriteInfo($"    [0] Sales managers ({Role}): no operations");
		_logger.Received().WriteInfo($"    [2] Sales managers ({Role}): read/edit");
	}

	[Test]
	[Description("With --grantee, the rows above the grantee's row are listed: for a user who is also in one of those roles, that row decides first.")]
	public void Execute_ShouldListRowsAboveGrantee_WhenGranteeRowIsNotFirst() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0), RoleRow(true, true, true, false, 1)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Role.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read succeeded");
		_logger.Received().WriteInfo("    Rows above it, which decide first for a user who is also in those roles:");
		_logger.Received().WriteInfo($"      [0] All employees ({Employees}): read/create/edit/delete");
	}

	[Test]
	[Description("A non-administered object lists the rows the service returns for it as the rows that apply once operation permissions are turned on.")]
	public void Execute_ShouldListRowsThatWouldApply_WhenObjectNotAdministered() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOpen", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOpen", "UsrOpen", false, new[] { EmployeesRow(0) }));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOpen" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read succeeded");
		_logger.Received().WriteInfo("    Rows that apply if operation permissions are turned on:");
		_logger.Received().WriteInfo($"    [0] All employees ({Employees}): read/create/edit/delete");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("It has no 'All employees' row")));
	}

	[Test]
	[Description("A non-administered object whose stored rows have no All employees row says that turning operation permissions on with set-object-rights also adds one, so a reader does not take the listed rows for the whole effect of an enable.")]
	public void Execute_ShouldSayAnEnableAddsAllEmployees_WhenANotAdministeredObjectHasNoAllEmployeesRow() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOpen", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOpen", "UsrOpen", false, new[] { RoleRow(true, false, false, false) }));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOpen" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read succeeded");
		_logger.Received().WriteInfo($"    [0] Sales managers ({Role}): read");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("It has no 'All employees' row")
			&& m.Contains("adds one with read/create/edit/delete")));
	}

	[Test]
	[Description("A connected read that times out stops the listing: every further read against the same stand would most likely wait as long, so the remaining objects are named as not read instead of spending the whole read deadline.")]
	public void Execute_ShouldStopReadingConnectedObjects_WhenAReadTimesOut() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus", "UsrType"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrStatus", null, false, Array.Empty<RoleOperationRights>(),
				ReadError: "The request timed out.", TimedOut: true));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the named object was read; a connected object that could not be read only warns");
		_rightsReader.DidNotReceive().GetObjectRights("UsrType", Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("Stopped after the read of UsrStatus timed out")
			&& m.Contains("UsrType")));
	}

	[Test]
	[Description("On an object that is not administered every row is listed, also with --grantee: all of them start to decide once operation permissions are turned on, including the rows below the grantee's.")]
	public void Execute_ShouldListEveryRow_WhenTheObjectIsNotAdministeredAndAGranteeIsGiven() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOpen", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOpen", "UsrOpen", false,
				new[] { RoleRow(true, false, false, false, 0), EmployeesRow(1) }));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOpen", Grantee = Role.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read succeeded");
		_logger.Received().WriteInfo($"    [0] Sales managers ({Role}): read");
		_logger.Received().WriteInfo($"    [1] All employees ({Employees}): read/create/edit/delete");
	}

	[Test]
	[Description("A read of the named object that times out fails the call and stops: its connected objects are not listed, instead of waiting as long again, and the code is not resolved by title.")]
	public void Execute_ShouldFailAndStop_WhenTheRootReadTimesOut() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(ObjectRightsInfo.ReadFailed("UsrOrder", "The request timed out.", timedOut: true));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the object the caller named could not be read");
		_rightsReader.DidNotReceive().GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>());
		_connectedObjects.DidNotReceiveWithAnyArgs().Resolve(default, default, default);
		_rightsReader.DidNotReceiveWithAnyArgs().FindObjectsByTitle(default, default);
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrOrder: its operation permissions could not be read")));
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("Stopped after the read of UsrOrder timed out")
			&& m.Contains("its connected objects were not listed")));
	}

	[Test]
	[Description("A reader that throws a timeout the way Creatio's client does — wrapped in an AggregateException — stops the listing like one it reports in-band.")]
	public void Execute_ShouldStopReadingConnectedObjects_WhenAWrappedTimeoutIsThrown() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus", "UsrType"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new AggregateException(new System.Threading.Tasks.TaskCanceledException("timed out")));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the named object was read; a connected object that could not be read only warns");
		_rightsReader.DidNotReceive().GetObjectRights("UsrType", Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("UsrStatus: its operation permissions could not be read: timed out")));
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("Stopped after the read of UsrStatus timed out")));
	}

	[Test]
	[Description("A connected read that fails with a fault the server answered does not stop the listing: the next object may well be readable.")]
	public void Execute_ShouldReadTheNextObject_WhenAConnectedReadFailsWithoutATimeout() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus", "UsrType"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(ObjectRightsInfo.ReadFailed("UsrStatus", "503 Service Unavailable"));
		_rightsReader.GetObjectRights("UsrType", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrType", EmployeesRow(0)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a connected object that could not be read only warns");
		_rightsReader.Received(1).GetObjectRights("UsrType", Arg.Any<CreatioRequestOptions>());
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("Stopped after")));
	}

	[Test]
	[Description("get passes its own request timeout to the enumeration of the connected objects, so the schema read honours it like every other read.")]
	public void Execute_ShouldPassTheRequestTimeout_WhenEnumeratingConnectedObjects() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true, TimeOut = 12_345 };

		// Act
		_command.Execute(options);

		// Assert
		_connectedObjects.Received(1).Resolve("UsrOrder", true, 12_345);
	}

	[Test]
	[Description("With a read budget that is spent, the named object is still read and its connected objects are not listed, so an answer bounded by a deadline arrives with what was read.")]
	public void Execute_ShouldStopAtTheReadBudget_WhenItIsSpent() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus", "UsrType"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", IncludeConnected = true, ReadBudget = TimeSpan.Zero
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the named object was read");
		_connectedObjects.DidNotReceiveWithAnyArgs().Resolve(default, default, default);
		_rightsReader.DidNotReceive().GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains("the read budget of 0 s is spent")
			&& message.Contains("the connected objects of UsrOrder were not listed")));
	}

	[Test]
	[Description("A read budget spent while the connected objects are listed stops the reads, naming the objects not read, so an answer bounded by a deadline arrives with what was read.")]
	public void Execute_ShouldNameTheObjectsNotRead_WhenTheBudgetIsSpentWhileListing() {
		// Arrange
		TimeSpan elapsed = TimeSpan.Zero;
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(_ => {
			// The listing spends what was left of the budget.
			elapsed = TimeSpan.FromSeconds(91);
			return Resolution("UsrOrder", "UsrStatus", "UsrType");
		});
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", IncludeConnected = true,
			ReadDeadline = new RequestDeadline(TimeSpan.FromSeconds(90), () => elapsed)
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the named object was read");
		_rightsReader.DidNotReceive().GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("the read budget of 90 s is spent. Not read: UsrStatus, UsrType")));
	}

	[Test]
	[Description("A read budget that runs out between the check and the listing's own timeout is reported like a budget spent before the listing: the root, already read, is still reported, and the call does not fail.")]
	public void Execute_ShouldStillReportTheRoot_WhenTheBudgetRunsOutJustBeforeTheListing() {
		// Arrange
		int clockReads = 0;
		// The first look at the clock finds time left; every later one finds the budget spent.
		TimeSpan Elapsed() => ++clockReads == 1 ? TimeSpan.FromSeconds(89) : TimeSpan.FromSeconds(91);
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", IncludeConnected = true,
			ReadDeadline = new RequestDeadline(TimeSpan.FromSeconds(90), Elapsed)
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the named object was read");
		_logger.Received().WriteInfo("  UsrOrder: administered by operation permissions. Rows in priority order:");
		_connectedObjects.DidNotReceiveWithAnyArgs().Resolve(default, default, default);
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("the connected objects of UsrOrder were not listed")));
	}

	[Test]
	[Description("With a read budget, the listing of the connected objects gets at most what is left of it.")]
	public void Execute_ShouldBoundTheListingByTheBudget_WhenABudgetIsSet() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", IncludeConnected = true, TimeOut = 30_000, ReadBudget = TimeSpan.FromSeconds(5)
		};

		// Act
		_command.Execute(options);

		// Assert
		_connectedObjects.Received(1).Resolve("UsrOrder", true, Arg.Is<int?>(timeout => timeout > 0 && timeout <= 5_000));
	}

	[Test]
	[Description("With a read budget, every read gets the call's deadline: each of its requests is then cut to what is left of it (CreatioRequestOptions.ForNextRequest), so neither one read nor its several requests can take the answer past the caller's deadline.")]
	public void Execute_ShouldPassTheReadBudgetAsTheDeadline_WhenABudgetIsSet() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", TimeOut = 30_000, ReadBudget = TimeSpan.FromSeconds(5)
		};

		// Act
		_command.Execute(options);

		// Assert
		_rightsReader.Received(1).GetObjectRights("UsrOrder", Arg.Is<CreatioRequestOptions>(request =>
			request.Deadline != null && request.Deadline.Budget == TimeSpan.FromSeconds(5) && request.TimeOut == 30_000));
	}

	[Test]
	[Description("Without a read budget (the CLI) each read keeps the command's own timeout.")]
	public void Execute_ShouldKeepTheRequestTimeout_WhenNoBudgetIsSet() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", EmployeesRow(0)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", TimeOut = 30_000 };

		// Act
		_command.Execute(options);

		// Assert
		_rightsReader.Received(1).GetObjectRights("UsrOrder",
			Arg.Is<CreatioRequestOptions>(request => request.TimeOut == 30_000 && request.Deadline == null));
	}

	[TestCase("Usr Order")]
	[TestCase("UsrOrder;")]
	[Description("A name that is not a schema identifier is only ever a title: when no object has it, the call is refused and no object is read.")]
	public void Execute_ShouldRefuse_WhenTheNameIsNeitherACodeNorATitle(string name) {
		// Arrange
		_rightsReader.FindObjectsByTitle(name, Arg.Any<CreatioRequestOptions>()).Returns(Array.Empty<ObjectTitleMatch>());
		GetObjectRightsOptions options = new() { EntitySchemaName = name };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the name names no object");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("is not an object code (letters, digits and '_' only), "
			+ "and no object has it as its title")));
		_rightsReader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}

	// ---- the record layer (ENG-100406) ----

	private static ObjectRightsInfo WithRecords(string name, bool recordsOn, params DefaultRecordRule[] rules) =>
		Administered(name, EmployeesRow(0)) with { AdministratedByRecords = recordsOn, RecordRules = rules };

	private static DefaultRecordRule RecordRule(Guid author, Guid grantee) =>
		new(author, "Author", grantee, "Grantee", RecordRightLevel.Granted, RecordRightLevel.Delegated,
			RecordRightLevel.NotSet, true);

	[Test]
	[Description("With record permissions ON, every default record rule is listed with both ids, its three levels and the manager flag.")]
	public void Execute_ShouldListRecordRules_WhenRecordPermissionsAreOn() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(WithRecords("UsrOrder", true, RecordRule(Employees, Role)));

		// Act
		int exitCode = _command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder" });

		// Assert
		exitCode.Should().Be(0, because: "the read succeeded");
		_logger.Received().WriteInfo(Arg.Is<string>(line => line.StartsWith("    Record permissions: ON.")));
		_logger.Received().WriteInfo($"      Author ({Employees}) → Grantee ({Role}): read granted, edit delegated, delete -, "
			+ "do not apply for manager: true");
	}

	[Test]
	[Description("With record permissions OFF, stored rules are listed as not in effect, and the switch line says record rights are not evaluated.")]
	public void Execute_ShouldMarkStoredRules_WhenRecordPermissionsAreOff() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(WithRecords("UsrOrder", false, RecordRule(Employees, Role)));

		// Act
		_command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder" });

		// Assert
		_logger.Received().WriteInfo(Arg.Is<string>(line => line.Contains("Record permissions: OFF")
			&& line.Contains("not in effect while record permissions are off")));
	}

	[Test]
	[Description("With record permissions ON and no rule, the output states the built-in default: every user sees only the records they create.")]
	public void Execute_ShouldStateOwnRecordsDefault_WhenOnWithNoRule() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>()).Returns(WithRecords("UsrOrder", true));

		// Act
		_command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder" });

		// Assert
		_logger.Received().WriteInfo(Arg.Is<string>(line => line.Contains("NO default record rules")
			&& line.Contains("every user sees only the records they create")));
	}

	[Test]
	[Description("--author filters the rules by author; a filter that matches none says so with the total.")]
	public void Execute_ShouldFilterRulesByAuthor() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(WithRecords("UsrOrder", true, RecordRule(Employees, Role)));

		// Act
		int exitCode = _command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder", Author = Role.ToString() });

		// Assert
		exitCode.Should().Be(0, because: "a filter is not an error");
		_logger.Received().WriteInfo("      no rule matches the filter (1 rule(s) in all).");
	}

	[Test]
	[Description("--author that is not a GUID is refused before any read.")]
	public void Execute_ShouldRefuse_WhenAuthorIsNotGuid() {
		// Act
		int exitCode = _command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder", Author = "Sales" });

		// Assert
		exitCode.Should().Be(1, because: "an author is a SysAdminUnit id");
		_rightsReader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}

	[Test]
	[Description("The record count is read for the named object only, and a failed count is a warning that never fails the read.")]
	public void Execute_ShouldCountRootOnly_AndTolerateCountFailure() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(call => WithRecords((string)call[0], true));
		_recordCounter.CountRecords(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("SelectQuery failed: denied"));

		// Act
		int exitCode = _command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder", IncludeConnected = true });

		// Assert
		exitCode.Should().Be(0, because: "the count is a fact, not part of the read");
		_recordCounter.Received(1).CountRecords("UsrOrder", Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(line => line.Contains("UsrOrder: existing records not counted")));
	}

	[Test]
	[Description("--author and --grantee each keep only the matching default record rule.")]
	public void Execute_ShouldKeepOnlyMatchingRules_WhenFiltered() {
		// Arrange
		DefaultRecordRule byEmployees = RecordRule(Employees, Employees);
		DefaultRecordRule toRole = RecordRule(Role, Role);
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(WithRecords("UsrOrder", true, byEmployees, toRole));
		System.Collections.Generic.List<string> lines = new();
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => lines.Add((string)call[0]));

		// Act
		_command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder", Author = Employees.ToString() });
		string[] byAuthor = lines.Where(line => line.StartsWith("      ") && line.Contains(" → ")).ToArray();
		lines.Clear();
		_command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder", Grantee = Role.ToString() });
		string[] byGrantee = lines.Where(line => line.StartsWith("      ") && line.Contains(" → ")).ToArray();

		// Assert
		byAuthor.Should().ContainSingle(because: "only the rule with that author is listed")
			.Which.Should().StartWith($"      Author ({Employees}) → Grantee ({Employees})", because: "it is the matching rule");
		byGrantee.Should().ContainSingle(because: "only the rule with that grantee is listed")
			.Which.Should().StartWith($"      Author ({Role}) → Grantee ({Role})", because: "it is the matching rule");
	}

	[Test]
	[Description("An object with record permissions OFF and no stored rule is not counted: apply-default-record-rights has nothing to apply to it, and the count is the costliest query of the read.")]
	public void Execute_ShouldNotCount_WhenRecordPermissionsAreOffWithNoRule() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>()).Returns(WithRecords("UsrOrder", false));

		// Act
		int exitCode = _command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder" });

		// Assert
		exitCode.Should().Be(0, because: "the read succeeded");
		_recordCounter.DidNotReceiveWithAnyArgs().CountRecords(default, default);
	}

	[Test]
	[Description("An object with record permissions OFF but with stored rules is still counted: turning them on would bring the rules into effect.")]
	public void Execute_ShouldCount_WhenRecordPermissionsAreOffWithStoredRules() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(WithRecords("UsrOrder", false, RecordRule(Employees, Role)));
		_recordCounter.CountRecords("UsrOrder", Arg.Any<CreatioRequestOptions>()).Returns(42L);

		// Act
		_command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder" });

		// Assert
		_logger.Received().WriteInfo("  UsrOrder: 42 existing record(s), counted under the calling account.");
	}

	[Test]
	[Description("The named object's records are counted only after every object is read, so the count never takes the read budget of the connected objects.")]
	public void Execute_ShouldCountAfterTheConnectedReads() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true, Arg.Any<int?>()).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(call => WithRecords((string)call[0], true));

		// Act
		_command.Execute(new GetObjectRightsOptions { EntitySchemaName = "UsrOrder", IncludeConnected = true });

		// Assert
		Received.InOrder(() => {
			_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>());
			_recordCounter.CountRecords("UsrOrder", Arg.Any<CreatioRequestOptions>());
		});
	}

	[Test]
	[Description("An object with a title of its own is shown by its title next to its code, in the header and on its line, and an object found by its code is never looked up by title.")]
	public void Execute_ShouldShowTheTitleNextToTheCode_WhenTheObjectHasATitleOfItsOwn() {
		// Arrange
		_rightsReader.GetObjectRights("Feature", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "Feature", "Creatio functionality", true, new[] { EmployeesRow(0) }));
		GetObjectRightsOptions options = new() { EntitySchemaName = "Feature" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo("Object permissions for 'Creatio functionality' (Feature):");
		_logger.Received().WriteInfo(
			"  'Creatio functionality' (Feature): administered by operation permissions. Rows in priority order:");
		_rightsReader.DidNotReceiveWithAnyArgs().FindObjectsByTitle(default, default);
	}

	[Test]
	[Description("When no object has the code, the one object with that title is read, the output says it was found by its title, and its connected objects are resolved from its code.")]
	public void Execute_ShouldReadTheObjectWithTheTitle_WhenNoObjectHasTheCode() {
		// Arrange
		_rightsReader.GetObjectRights("Order", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "Order", null, false, Array.Empty<RoleOperationRights>()));
		_rightsReader.FindObjectsByTitle("Order", Arg.Any<CreatioRequestOptions>())
			.Returns(new[] { new ObjectTitleMatch("UsrOrder", "Order") });
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOrder", "Order", true, new[] { EmployeesRow(0) }));
		GetObjectRightsOptions options = new() { EntitySchemaName = "Order", IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the object with that title was read");
		_logger.Received().WriteInfo("  'Order' is not an object code: it is the title of UsrOrder, which is read.");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.StartsWith("Object permissions for 'Order' (UsrOrder)")));
		_connectedObjects.Received(1).Resolve("UsrOrder", true, Arg.Any<int?>());
	}

	[Test]
	[Description("A name that is not a schema identifier is looked up only as a title, and the one object with it is read.")]
	public void Execute_ShouldReadTheObjectWithTheTitle_WhenTheNameIsNotAnIdentifier() {
		// Arrange
		_rightsReader.FindObjectsByTitle("Creatio functionality", Arg.Any<CreatioRequestOptions>())
			.Returns(new[] { new ObjectTitleMatch("Feature", "Creatio functionality") });
		_rightsReader.GetObjectRights("Feature", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "Feature", "Creatio functionality", false, Array.Empty<RoleOperationRights>()));
		GetObjectRightsOptions options = new() { EntitySchemaName = " Creatio functionality " };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the object with that title was read");
		_rightsReader.Received(1).GetObjectRights("Feature", Arg.Any<CreatioRequestOptions>());
		_rightsReader.DidNotReceive().GetObjectRights("Creatio functionality", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Several objects with the title are refused, listing them by title and code, and none of them is read: the read never guesses which one was meant.")]
	public void Execute_ShouldRefuse_WhenSeveralObjectsHaveTheTitle() {
		// Arrange
		_rightsReader.GetObjectRights("Feature", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "Feature", null, false, Array.Empty<RoleOperationRights>()));
		_rightsReader.FindObjectsByTitle("Feature", Arg.Any<CreatioRequestOptions>())
			.Returns(new[] { new ObjectTitleMatch("Specification", "Feature"), new ObjectTitleMatch("UsrFeature", "Feature") });
		GetObjectRightsOptions options = new() { EntitySchemaName = "Feature" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the name is ambiguous");
		_logger.Received().WriteError("Error: 'Feature' is not an object code: it is the title of 2 objects: "
			+ "'Feature' (code: Specification); 'Feature' (code: UsrFeature). Re-run with the exact code.");
		_rightsReader.DidNotReceive().GetObjectRights("Specification", Arg.Any<CreatioRequestOptions>());
		_rightsReader.DidNotReceive().GetObjectRights("UsrFeature", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A code no object has and no object's title is reported as not found, after the title was looked up.")]
	public void Execute_ShouldReportNotFound_WhenNoObjectHasTheCodeOrTheTitle() {
		// Arrange
		_rightsReader.GetObjectRights("UsrMissing", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "UsrMissing", null, false, Array.Empty<RoleOperationRights>()));
		_rightsReader.FindObjectsByTitle("UsrMissing", Arg.Any<CreatioRequestOptions>()).Returns(Array.Empty<ObjectTitleMatch>());
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrMissing" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the name names no object");
		_logger.Received().WriteError("  UsrMissing: the schema was not found.");
		_rightsReader.Received(1).FindObjectsByTitle("UsrMissing", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A title lookup the service fails is reported as a failure, never as 'no object has this title'.")]
	public void Execute_ShouldFail_WhenTheTitleLookupFails() {
		// Arrange
		_rightsReader.FindObjectsByTitle("Usr Order", Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new System.Net.Http.HttpRequestException("connection reset"));
		GetObjectRightsOptions options = new() { EntitySchemaName = "Usr Order" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the lookup did not answer");
		_logger.Received().WriteError("Error: the lookup of the objects titled 'Usr Order' failed: connection reset");
	}

	[Test]
	[Description("A code no object has, whose title lookup then fails, is reported with both facts: the code names no object, and the lookup failed, so a typo is not taken for a passing fault.")]
	public void Execute_ShouldSayTheCodeNamesNoObject_WhenItsTitleLookupFails() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrdr", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "UsrOrdr", null, false, Array.Empty<RoleOperationRights>()));
		_rightsReader.FindObjectsByTitle("UsrOrdr", Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new System.Net.Http.HttpRequestException("connection reset"));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrdr" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the name names no object, and the title lookup did not answer");
		_logger.Received().WriteError("Error: no object has the code 'UsrOrdr', and the lookup of the objects titled "
			+ "'UsrOrdr' failed: connection reset");
	}
}
