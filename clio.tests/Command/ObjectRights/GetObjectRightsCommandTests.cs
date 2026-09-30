using System;
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
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetObjectRightsCommand>();
	}

	public override void TearDown() {
		_rightsReader.ClearReceivedCalls();
		_connectedObjects.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_rightsReader = Substitute.For<IObjectRightsReader>();
		_connectedObjects = Substitute.For<IConnectedObjectsResolver>();
		_logger = Substitute.For<ILogger>();
		// Default: no fan-out — the resolver returns just the root object.
		_connectedObjects.Resolve(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int?>())
			.Returns(callInfo => Resolution((string)callInfo[0]));
		containerBuilder.AddTransient(_ => _rightsReader);
		containerBuilder.AddTransient(_ => _connectedObjects);
		containerBuilder.AddTransient(_ => _logger);
	}

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
				&& m.Contains("available to all internal users")
				&& m.Contains("external users reach it only through an explicit grant")));
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
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the object the caller named could not be read");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("could not read object rights")));
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
				&& (m.Contains("could not read object rights") || m.Contains("schema not found"))));
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
			$"Object operation permissions for 'UsrOrder' and its connected objects (grantee {Role}):",
			$"  {GetObjectRightsCommand.PriorityRule}",
			"  UsrOrder: administered by operation permissions. Rows in priority order:",
			$"    [0] Sales managers ({Role}): read/create/edit",
			"  UsrStatus: administered by operation permissions. Rows in priority order:",
			$"    grantee {Role} has NO row (no operations granted).",
			"    A new row would go below every row; these decide first for a user who is also in those roles:",
			$"      [0] All employees ({Employees}): read/create/edit/delete",
			"  UsrOpen: not administered by operation permissions — available to all internal users; "
				+ "external users reach it only through an explicit grant.",
			"    It has no 'All employees' row: set-object-rights --enable-operation-permissions adds one with "
				+ "read/create/edit/delete below any stored rows, unless the grant is for All employees itself."
		}, because: "the output is the facts per object, and nothing more — no verdict");
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
	[Description("A read of the named object that times out fails the call and stops the listing: the connected objects are named as not read instead of waiting as long again.")]
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
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrOrder: could not read object rights")));
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("Stopped after the read of UsrOrder timed out")
			&& m.Contains("UsrStatus")));
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
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("UsrStatus: could not read object rights (timed out)")));
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

	[TestCase("Usr Order")]
	[TestCase("UsrOrder;")]
	[Description("A name that is not a schema identifier is refused before anything is read.")]
	public void Execute_ShouldRefuse_WhenNameIsNotAnIdentifier(string name) {
		// Arrange
		GetObjectRightsOptions options = new() { EntitySchemaName = name };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "only a plain schema identifier can name an object");
		_rightsReader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}
}
