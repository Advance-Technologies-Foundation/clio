using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

/// <summary>
/// <c>set-column-rights</c> over substitutes for the service, storage and the write lease: the real planner and
/// read-back verifier, the list built from STORAGE, the cross-check against the service, the lease and the re-read
/// right before the save, the storage read-back that decides the result, and every refusal before a write.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class SetColumnRightsCommandTests : BaseCommandTests<SetColumnRightsOptions> {

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid SchemaUId = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");
	private static readonly Guid AmountUId = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid DescriptionUId = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");
	private static readonly Guid NameUId = Guid.Parse("9e53fd7c-3333-4c5b-9c3a-2a7b0e7e0003");

	private SetColumnRightsCommand _command;
	private IObjectRightsReader _reader;
	private IColumnRightsStorageReader _storage;
	private IColumnRightsWriter _writer;
	private IAdministratedObjectWriteLease _lease;
	private IGranteeLookup _granteeLookup;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private IInteractiveConsole _console;
	private ILogger _logger;
	private List<string> _errors;
	private List<string> _infos;
	private List<string> _warnings;
	private IReadOnlyList<ColumnRights> _sentList;
	private bool _sent;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetColumnRightsCommand>();
	}

	public override void TearDown() {
		_reader.ClearReceivedCalls();
		_storage.ClearReceivedCalls();
		_writer.ClearReceivedCalls();
		_lease.ClearReceivedCalls();
		_granteeLookup.ClearReceivedCalls();
		_columnManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_reader = Substitute.For<IObjectRightsReader>();
		_storage = Substitute.For<IColumnRightsStorageReader>();
		_writer = Substitute.For<IColumnRightsWriter>();
		_lease = Substitute.For<IAdministratedObjectWriteLease>();
		_granteeLookup = Substitute.For<IGranteeLookup>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_console = Substitute.For<IInteractiveConsole>();
		_logger = Substitute.For<ILogger>();
		_errors = new List<string>();
		_infos = new List<string>();
		_warnings = new List<string>();
		_sent = false;
		_sentList = null;
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => _errors.Add((string)call[0]));
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => _infos.Add((string)call[0]));
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => _warnings.Add((string)call[0]));
		_lease.Acquire(default).ReturnsForAnyArgs(_ => Substitute.For<IDisposable>());
		_granteeLookup.ResolveGranteeName(Role, Arg.Any<CreatioRequestOptions>()).Returns("Role");
		_granteeLookup.ResolveGranteeName(Employees, Arg.Any<CreatioRequestOptions>()).Returns("All employees");
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			new EntitySchemaPropertiesInfo("UsrHoliday", "Holiday", null, "(merged: all packages)", null, false, "Id",
				"Name", 3, 0, null, false, false, null, false, null, false, false, true, false, null, null,
				new[] {
					SchemaColumn("UsrAmount", AmountUId, "Amount"),
					SchemaColumn("UsrDescription", DescriptionUId, "Description"),
					SchemaColumn("Name", NameUId, "Name")
				}));
		Saves(ObjectRightsSaveResult.Saved);
		containerBuilder.AddTransient(_ => _reader);
		containerBuilder.AddTransient(_ => _storage);
		containerBuilder.AddTransient(_ => _writer);
		containerBuilder.AddTransient(_ => _lease);
		containerBuilder.AddTransient(_ => _granteeLookup);
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _console);
		containerBuilder.AddTransient(_ => _logger);
	}

	private ObjectRightsSnapshot _sentSnapshot;

	private void Saves(ObjectRightsSaveResult result) =>
		_writer.Save(default, default, default, default).ReturnsForAnyArgs(call => {
			_sent = true;
			_sentSnapshot = (ObjectRightsSnapshot)call[0];
			_sentList = (IReadOnlyList<ColumnRights>)call[2];
			return result;
		});

	private static IReadOnlyList<object[]> Calls(object substitute, string method) =>
		ReceivedCallLog.CallsTo(substitute, method);

	private static EntitySchemaPropertyColumnInfo SchemaColumn(string name, Guid uId, string title) =>
		new(name, uId, "own", title, null, "Text", false, false, null);

	private static ColumnRightRow Row(Guid grantee, int position, ColumnRightLevel level) =>
		new(grantee, grantee == Employees ? "All employees" : "Role", position, level);

	private static ColumnRights Stored(Guid column, params ColumnRightRow[] rows) => new(column, null, rows);

	// What the service lists for the stored rows while the switch is on: every non-default column with its rows, every
	// schema column with no stored row with none; a default-only column is omitted. Nothing while it is off.
	private static IReadOnlyList<ColumnRights> ServiceView(bool? on, IReadOnlyList<ColumnRights> stored) {
		if (on != true) {
			return [];
		}
		HashSet<Guid?> withRows = stored.Select(column => column.ColumnUId).ToHashSet();
		return stored.Where(column => !ColumnRightsDefaults.IsDefaultOnly(column.Rows))
			.Concat(new[] { AmountUId, DescriptionUId, NameUId }.Where(uId => !withRows.Contains(uId))
				.Select(uId => new ColumnRights(uId, null, [])))
			.ToArray();
	}

	private static ObjectRightsInfo Object(bool? on, IReadOnlyList<ColumnRights> stored, bool operations = false,
		bool records = false) =>
		new(true, "UsrHoliday", "Holiday", operations, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: on, Columns: ServiceView(on, stored), Snapshot: new ObjectRightsSnapshot(new JsonObject()),
			AdministratedByRecords: records);

	// The service read (name resolution, the read under the lease, the read-back) and the storage reads (the plan's,
	// the one right before the save, the read-back).
	private void Reads(bool? on, IReadOnlyList<ColumnRights> stored, bool? onAfter = null,
		IReadOnlyList<ColumnRights> storedAfter = null, IReadOnlyList<ColumnRights> storedBeforeSave = null) {
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>())
			.Returns(Object(on, stored), Object(on, stored), Object(onAfter ?? on, storedAfter ?? stored));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>())
			.Returns(stored, storedBeforeSave ?? stored, storedAfter ?? stored);
		// The read by column UId, made when turning column permissions on, returns the same rows.
		_storage.ReadStoredRowsByColumns(default, default).ReturnsForAnyArgs(
			stored.Select(column => new StoredColumnRights(SchemaUId, column)).ToArray());
	}

	private static SetColumnRightsOptions Grant(string access = "denied", bool enable = false, bool preview = false,
		string grantee = null, string column = "UsrAmount", int? position = null) =>
		new() {
			EntitySchemaName = "UsrHoliday", Column = column, Grantee = grantee ?? Role.ToString(), Access = access,
			EnableColumnPermissions = enable, Preview = preview, Confirm = !preview, Position = position
		};

	private static SetColumnRightsOptions Remove(Guid grantee) =>
		new() { EntitySchemaName = "UsrHoliday", Column = "UsrAmount", Grantee = grantee.ToString(), RemoveRow = true,
			Confirm = true };

	private static SetColumnRightsOptions Switch(bool enable) =>
		new() { EntitySchemaName = "UsrHoliday", EnableColumnPermissions = enable, DisableColumnPermissions = !enable,
			Confirm = true };

	[Test]
	[Description("A grant while ON sends the list built from storage — a restricted column as stored, a column with no rows with none, a default-only column left out — under the write lease, and succeeds when storage reads back as planned.")]
	public void Execute_ShouldSendTheListBuiltFromStorage_WhenSwitchIsOn() {
		// Arrange
		ColumnRights description = Stored(DescriptionUId, Row(Role, 0, ColumnRightLevel.Read));
		ColumnRights nameDefault = Stored(NameUId, ColumnRightsDefaults.Row);
		Reads(true, [description, nameDefault],
			storedAfter: [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied)), description, nameDefault]);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(0, because: "storage holds the planned rows");
		_sentList.Select(column => column.ColumnUId).Should().BeEquivalentTo(new Guid?[] { AmountUId, DescriptionUId },
			because: "the default-only column is left out and every other column is named");
		_sentList.Single(column => column.ColumnUId == DescriptionUId).Rows.Should().Equal(description.Rows,
			because: "another column goes back as stored");
		Calls(_lease, nameof(IAdministratedObjectWriteLease.Acquire)).Should().ContainSingle(
			because: "a write holds the object's lease").Which[0].Should().Be("UsrHoliday", because: "it is keyed by the code");
		_infos.Should().Contain(line => line.Contains("not bound to a package"), because: "the rows stay in this environment");
	}

	[Test]
	[Description("While ON, a column with no stored row other than the changed one is sent with none, so it stays as it is, and the facts say so.")]
	public void Execute_ShouldKeepALockedColumnLocked_WhenAnotherColumnChanges() {
		// Arrange
		ColumnRights nameDefault = Stored(NameUId, ColumnRightsDefaults.Row);
		Reads(true, [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Read)), nameDefault],
			storedAfter: [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied)), nameDefault]);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(0, because: "storage holds the plan");
		_sentList.Single(column => column.ColumnUId == DescriptionUId).Rows.Should().BeEmpty(
			because: "a column with no rows while ON is sent with none, never left to the default");
		_infos.Should().Contain(line => line.Contains("Sent with no rows") && line.Contains("UsrDescription"),
			because: "the facts name the column kept without rows");
	}

	[Test]
	[Description("Enabling with nothing stored sends an empty list, so every column gets the default row instead of being locked, and the output names them.")]
	public void Execute_ShouldGiveDefaultsNotALock_WhenEnablingFromEmptyStorage() {
		// Arrange
		Reads(false, [], onAfter: true, storedAfter: [
			Stored(AmountUId, ColumnRightsDefaults.Row), Stored(DescriptionUId, ColumnRightsDefaults.Row),
			Stored(NameUId, ColumnRightsDefaults.Row)
		]);

		// Act
		int exitCode = _command.Execute(Switch(enable: true));

		// Assert
		exitCode.Should().Be(0, because: "every column holds the default row afterwards");
		_sentList.Should().NotBeNull(because: "a null list would leave every column with no rows: locked");
		_sentList.Should().BeEmpty(because: "no column has rows to keep");
		_infos.Should().Contain(line => line.Contains("Get the default row (All employees: edit)")
			&& line.Contains("Name, UsrAmount, UsrDescription"), because: "the output names the columns that get the default");
	}

	[Test]
	[Description("Enabling names the columns whose stored rows start to apply.")]
	public void Execute_ShouldNameTheColumnsThatStartToApply_WhenEnabling() {
		// Arrange
		ColumnRights amount = Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied));
		Reads(false, [amount], onAfter: true, storedAfter: [amount, Stored(DescriptionUId, ColumnRightsDefaults.Row),
			Stored(NameUId, ColumnRightsDefaults.Row)]);

		// Act
		int exitCode = _command.Execute(Switch(enable: true));

		// Assert
		exitCode.Should().Be(0, because: "storage holds the plan");
		_infos.Should().Contain(line => line.Contains("Columns whose stored rows start to apply (1): UsrAmount"),
			because: "the operator sees which stored restrictions take effect");
	}

	[Test]
	[Description("A disable sends no list, so every stored row is kept, and the output says the rows are kept, not applied.")]
	public void Execute_ShouldSendNoList_WhenDisabling() {
		// Arrange
		IReadOnlyList<ColumnRights> stored = [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied))];
		Reads(true, stored, onAfter: false);

		// Act
		int exitCode = _command.Execute(Switch(enable: false));

		// Assert
		exitCode.Should().Be(0, because: "the switch is off on the read-back");
		_sent.Should().BeTrue(because: "the switch is saved");
		_sentList.Should().BeNull(because: "null leaves the stored rows alone");
		_infos.Should().Contain(line => line.Contains("kept, not applied"), because: "the output says the rows stay");
	}

	[Test]
	[Description("While ON, storage that disagrees with the service's list (a column the service lists with rows that storage lacks, or a stored non-default column the service omits) refuses the call before any write.")]
	public void Execute_ShouldRefuse_WhenStorageDisagreesWithTheService() {
		// Arrange
		IReadOnlyList<ColumnRights> listed = [Stored(DescriptionUId, Row(Role, 0, ColumnRightLevel.Read))];
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsInfo(true,
			"UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: true, Columns: listed));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns(
			[Stored(NameUId, Row(Role, 0, ColumnRightLevel.Denied))]);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "a list built from a storage read that misses rows would lock columns");
		_errors.Should().Contain(line => line.Contains("disagree with what the service lists")
			&& line.Contains(DescriptionUId.ToString()) && line.Contains(NameUId.ToString()),
			because: "the refusal names each disagreeing column");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A call while another holds the write lease for the object is refused before storage is read or anything is written.")]
	public void Execute_ShouldRefuse_WhenAnotherCallHoldsTheLease() {
		// Arrange
		Reads(true, []);
		_lease.Acquire(default).ReturnsForAnyArgs(_ => throw new InvalidOperationException(
			"another call is changing the permissions of 'UsrHoliday' on this environment"));

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "two overlapping calls would revert each other");
		_errors.Should().Contain(line => line.Contains("another call is changing the permissions"),
			because: "the refusal says why");
		Calls(_reader, nameof(IObjectRightsReader.GetObjectRights)).Should().BeEmpty(
			because: "nothing is read while another write of the object runs");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("Stored rows that changed between the plan and the save abort the call: the save would overwrite the other change with the rows planned on.")]
	public void Execute_ShouldAbort_WhenStorageChangedBeforeTheSave() {
		// Arrange
		Reads(true, [], storedBeforeSave: [Stored(DescriptionUId, Row(Role, 0, ColumnRightLevel.Read))]);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "the plan no longer matches storage");
		_errors.Should().Contain(line => line.Contains("changed after the call read it")
			&& line.Contains("stored column permission rows changed"), because: "the abort says why");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A save the service answered success for whose rows storage does not hold fails the call, prints what differs, and says nothing was applied.")]
	public void Execute_ShouldFailAndSayUnchanged_WhenStorageDoesNotMatchThePlan() {
		// Arrange
		Reads(true, []);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "only storage proves a column-rights save");
		_errors.Should().Contain(line => line.Contains("do not match the plan") && line.Contains("is missing")
			&& line.Contains("nothing was applied"), because: "the failure names the difference and that storage is unchanged");
	}

	[Test]
	[Description("A read-back where the switch changed but the rows did not land says the change was PARTIALLY applied, naming the switch.")]
	public void Execute_ShouldSayPartiallyApplied_WhenOnlyTheSwitchLanded() {
		// Arrange
		ColumnRights amount = Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied));
		Reads(false, [amount], onAfter: true, storedAfter: [amount]);

		// Act
		int exitCode = _command.Execute(Switch(enable: true));

		// Assert
		exitCode.Should().Be(1, because: "the default rows planned for the other columns are not stored");
		_errors.Should().Contain(line => line.Contains("PARTIALLY applied")
			&& line.Contains("column permissions are ON (were OFF)"), because: "the operator learns the switch changed");
	}

	[Test]
	[Description("A save that reported an error and left storage as it was fails, printing the differences and that nothing was applied.")]
	public void Execute_ShouldFailWithTheDifferences_WhenTheSaveFailedAndNothingLanded() {
		// Arrange
		Saves(new ObjectRightsSaveResult("Request timeout", OutcomeUnknown: true));
		Reads(true, []);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "the save did not land");
		_errors.Should().Contain(line => line.Contains("may still be applied") && line.Contains("is missing")
			&& line.Contains("nothing was applied"), because: "the failure prints what differs and what storage holds");
	}

	[Test]
	[Description("A save that reported an error but whose rows storage holds as planned succeeds with a warning.")]
	public void Execute_ShouldSucceedWithAWarning_WhenTheSaveFailedButLanded() {
		// Arrange
		Saves(new ObjectRightsSaveResult("Request timeout", OutcomeUnknown: true));
		Reads(true, [], storedAfter: [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied))]);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(0, because: "storage holds the plan");
		_warnings.Should().Contain(line => line.Contains("reported an error") && line.Contains("match the plan"),
			because: "the operator learns the error did not stop the change");
	}

	[Test]
	[Description("A storage read-back that fails reports the save as NOT verified and fails the call.")]
	public void Execute_ShouldSayNotVerified_WhenTheStorageReadBackFails() {
		// Arrange
		Reads(true, []);
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns(
			_ => [], _ => [], _ => throw new InvalidOperationException("SelectQuery failed: timeout"));

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "a save nothing proves is not reported as done");
		_errors.Should().Contain(line => line.Contains("saved, but NOT verified") && line.Contains("timeout"),
			because: "the failure names the read-back that failed");
	}

	[Test]
	[Description("When the call's time limit leaves no room for the save and its read-back, the save is not sent.")]
	public void Execute_ShouldNotSave_WhenTheTimeLimitLeavesNoRoom() {
		// Arrange
		Reads(true, []);
		SetColumnRightsOptions options = Grant();
		options.TimeOut = 60_000;
		options.CallBudget = TimeSpan.FromSeconds(30);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a save whose read-back cannot finish would leave its outcome unknown");
		_errors.Should().Contain(line => line.Contains("The save was not sent"), because: "the refusal says so");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A read-back where the operation switch changed fails the call: the save sends it as read and must leave it.")]
	public void Execute_ShouldFail_WhenTheOperationSwitchChanged() {
		// Arrange
		IReadOnlyList<ColumnRights> after = [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied))];
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>())
			.Returns(Object(true, []), Object(true, []), Object(true, after, operations: true));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns([], [], after);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "a changed operation switch is not this call's change");
		_errors.Should().Contain(line => line.Contains("operation permissions are ON, they were OFF"),
			because: "the failure names the switch that changed");
	}

	[Test]
	[Description("A grant that repeats the stored level exits 0 with no change and writes nothing.")]
	public void Execute_ShouldReportNoChange_WhenTheRowAlreadyHasTheLevel() {
		// Arrange
		Reads(true, [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied))]);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(0, because: "a call that finds its change in place succeeds");
		_infos.Should().Contain(line => line.EndsWith("(no change)."), because: "the output says nothing changed");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A restriction placed after an All employees row with edit is warned about: which row wins is not measured for columns.")]
	public void Execute_ShouldWarn_WhenARestrictionSitsBelowAllEmployeesEdit() {
		// Arrange
		Reads(false, [], storedAfter: [Stored(AmountUId, ColumnRightsDefaults.Row, Row(Role, 1, ColumnRightLevel.Denied))],
			onAfter: true);

		// Act
		_command.Execute(Grant(enable: true, preview: true));

		// Assert
		_infos.Should().Contain(line => line.Contains("Warning: in column UsrAmount") && line.Contains("--position 0"),
			because: "the operator is told how to place the restriction first");
	}

	[Test]
	[Description("A position past the end is measured against the rows the change starts from — with the default row when enabling.")]
	public void Execute_ShouldRefuseWithTheBaseRows_WhenThePositionIsPastTheEnd() {
		// Arrange
		Reads(false, []);

		// Act
		int exitCode = _command.Execute(Grant(enable: true, position: 3));

		// Assert
		exitCode.Should().Be(1, because: "the position is past the end");
		_errors.Should().Contain(line => line.Contains("starts from 1 row(s)") && line.Contains("positions are 0..1"),
			because: "the bounds come from the rows the change starts from, the default row included");
	}

	[Test]
	[Description("Removing the only row of a default-only column is refused with why: the service would write the same default back.")]
	public void Execute_ShouldRefuse_WhenRemovingTheDefaultRow() {
		// Arrange
		Reads(true, [Stored(AmountUId, ColumnRightsDefaults.Row)]);

		// Act
		int exitCode = _command.Execute(Remove(Employees));

		// Assert
		exitCode.Should().Be(1, because: "the removal would change nothing");
		_errors.Should().Contain(line => line.Contains("only the default row") && line.Contains("same default row back"),
			because: "the refusal explains why");
	}

	[Test]
	[Description("When storage cannot be read the call is refused before any write: the list is built from it.")]
	public void Execute_ShouldRefuse_WhenStorageCannotBeRead() {
		// Arrange
		Reads(true, []);
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>())
			.Returns(_ => throw new InvalidOperationException("SelectQuery failed: Access denied"));

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "a list built without storage could reset other columns");
		_errors.Should().Contain(line => line.Contains("stored column permission rows") && line.Contains("Access denied"),
			because: "the refusal says why");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A grant while OFF without --enable-column-permissions is refused, naming the flag.")]
	public void Execute_ShouldRefuse_WhenGrantingWhileOffWithoutEnable() {
		// Arrange
		Reads(false, []);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "a grant never turns the switch on as a side effect");
		_errors.Should().ContainSingle(line => line.Contains("are OFF") && line.Contains("--enable-column-permissions"),
			because: "the refusal names the flag");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A read without a readable switch is refused, never taken for OFF or ON.")]
	public void Execute_ShouldRefuse_WhenTheSwitchIsUnknown() {
		// Arrange
		Reads(null, []);

		// Act
		int exitCode = _command.Execute(Grant(enable: true));

		// Assert
		exitCode.Should().Be(1, because: "what the save turns on or off is unknown");
		_errors.Should().ContainSingle(line => line.Contains("no readable"), because: "the refusal says why");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("An unknown column is refused, naming it.")]
	public void Execute_ShouldRefuse_WhenTheColumnIsUnknown() {
		// Arrange
		Reads(true, []);

		// Act
		int exitCode = _command.Execute(Grant(column: "UsrMissing"));

		// Assert
		exitCode.Should().Be(1, because: "the schema has no such column");
		_errors.Should().ContainSingle(line => line.Contains("has no column 'UsrMissing'"), because: "the column is named");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("An unknown grantee is refused before the object is read.")]
	public void Execute_ShouldRefuse_WhenTheGranteeIsUnknown() {
		// Arrange
		Guid unknown = Guid.NewGuid();
		_granteeLookup.ResolveGranteeName(unknown, Arg.Any<CreatioRequestOptions>()).Returns((string)null);

		// Act
		int exitCode = _command.Execute(Grant(grantee: unknown.ToString()));

		// Assert
		exitCode.Should().Be(1, because: "a row for a principal nobody holds must not be written");
		_errors.Should().ContainSingle(line => line.Contains("was not found in SysAdminUnit"), because: "the grantee is checked");
		Calls(_reader, nameof(IObjectRightsReader.GetObjectRights)).Should().BeEmpty(
			because: "the grantee is checked before the object is read");
	}

	[Test]
	[Description("A title is refused, naming the code to pass, as set-object-rights does: a write names its object by code.")]
	public void Execute_ShouldRefuse_WhenTheObjectIsNamedByItsTitle() {
		// Arrange
		_reader.GetObjectRights("Holiday", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "Holiday", null, false, Array.Empty<RoleOperationRights>()));
		_reader.FindObjectsByTitle("Holiday", Arg.Any<CreatioRequestOptions>())
			.Returns(new[] { new ObjectTitleMatch("UsrHoliday", "Holiday") });
		SetColumnRightsOptions options = Grant();
		options.EntitySchemaName = "Holiday";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a title is not unique and this call changes access rights");
		_errors.Should().ContainSingle(line => line.Contains("it is the title of UsrHoliday") && line.Contains("Pass the code"),
			because: "the refusal names the code to pass");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A stored row without a level refuses a row change before any write, naming the row.")]
	public void Execute_ShouldRefuse_WhenAStoredRowIsUnreadable() {
		// Arrange
		Reads(true, [Stored(DescriptionUId, new ColumnRightRow(Employees, "All employees", 0, null, "(none)"))]);
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsInfo(true,
			"UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: true, Columns: [
				Stored(DescriptionUId, new ColumnRightRow(Employees, "All employees", 0, null, "(none)")),
				new ColumnRights(AmountUId, null, []), new ColumnRights(NameUId, null, [])
			]));

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "the row could not be sent back as stored");
		_errors.Should().Contain(line => line.Contains("[0] All employees") && line.Contains("exactly as stored"),
			because: "the refusal names the row and the reason");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A preview writes nothing, takes no lease, and shows the column's stored rows before and after.")]
	public void Execute_ShouldWriteNothing_WhenPreviewIsRequested() {
		// Arrange
		Reads(true, []);

		// Act
		int exitCode = _command.Execute(Grant(preview: true));

		// Assert
		exitCode.Should().Be(0, because: "a preview that would succeed is not a failure");
		_sent.Should().BeFalse(because: "a preview writes nothing");
		Calls(_lease, nameof(IAdministratedObjectWriteLease.Acquire)).Should().BeEmpty(because: "a preview takes no lease");
		_infos.Should().Contain(line => line.Contains("Stored rows read: 0 row(s) in 0 column(s), counted"),
			because: "a preview shows how many stored rows the plan was built from");
		_infos.Should().Contain(line => line.StartsWith("PREVIEW"), because: "the output says nothing was changed");
		_infos.Should().Contain(line => line.Contains("stored rows after: [0] Role"), because: "the plan shows the row");
	}

	[Test]
	[Description("A non-interactive run without --confirm is refused before any write.")]
	public void Execute_ShouldRefuse_WhenNotConfirmedNonInteractively() {
		// Arrange
		Reads(true, []);
		_console.IsInteractive.Returns(false);
		SetColumnRightsOptions options = Grant();
		options.Confirm = false;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a destructive change needs confirmation");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("The read a save is built from, and the storage read-back, are counted; only the re-read right before the save, compared with the counted read, is not.")]
	public void Execute_ShouldCountThePlanningReadAndTheReadBack_WhenSaving() {
		// Arrange
		Reads(true, [], storedAfter: [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied))]);

		// Act
		_command.Execute(Grant());

		// Assert
		Calls(_storage, nameof(IColumnRightsStorageReader.ReadStoredColumns)).Select(call => (bool)call[2])
			.Should().Equal(new[] { true, false, true },
				because: "a short storage read must never be planned on, and a short read-back must not pass as complete");
	}

	[Test]
	[Description("The save goes out from the object read again right before it, not from the first read.")]
	public void Execute_ShouldSaveFromTheFreshRead_WhenTheObjectIsUnchanged() {
		// Arrange
		ObjectRightsInfo fresh = Object(true, []);
		IReadOnlyList<ColumnRights> after = [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied))];
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>())
			.Returns(Object(true, []), fresh, Object(true, after));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns([], [], after);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(0, because: "storage holds the plan");
		_sentSnapshot.Should().BeSameAs(fresh.Snapshot, because: "the switches are sent as they are right before the save");
	}

	[TestCase("operations", TestName = "Execute_ShouldAbort_WhenTheOperationSwitchChangedBeforeTheSave")]
	[TestCase("records", TestName = "Execute_ShouldAbort_WhenTheRecordSwitchChangedBeforeTheSave")]
	[TestCase("columns", TestName = "Execute_ShouldAbort_WhenTheColumnSwitchChangedBeforeTheSave")]
	[Description("A switch another client changed between the plan and the save aborts the call: the save would send it back as first read.")]
	public void Execute_ShouldAbort_WhenASwitchChangedBeforeTheSave(string changed) {
		// Arrange
		ObjectRightsInfo fresh = changed switch {
			"operations" => Object(true, [], operations: true),
			"records" => Object(true, [], records: true),
			_ => Object(false, [])
		};
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(Object(true, []), fresh);
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns([]);
		string expected = changed switch {
			"operations" => "operation permissions are ON (were OFF)",
			"records" => "record permissions are ON (were OFF)",
			_ => "column permissions are OFF (were ON)"
		};

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "the object changed after it was read");
		_errors.Should().ContainSingle(line => line.Contains("changed after the call read it") && line.Contains(expected),
			because: "the abort names the switch that changed");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("Turning column permissions on reads the rows a second time by the object's column UIds; rows of this object the first read missed refuse the call.")]
	public void Execute_ShouldRefuse_WhenTheReadByColumnFindsRowsTheFirstReadMissed() {
		// Arrange
		Reads(false, []);
		_storage.ReadStoredRowsByColumns(default, default).ReturnsForAnyArgs(
			[new StoredColumnRights(SchemaUId, Stored(DescriptionUId, Row(Role, 0, ColumnRightLevel.Denied)))]);

		// Act
		int exitCode = _command.Execute(Switch(enable: true));

		// Assert
		exitCode.Should().Be(1, because: "a list built from a short read would reset the missed column to the default");
		_errors.Should().ContainSingle(line => line.Contains("read by column UId differ"), because: "the refusal says why");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("Turning column permissions on says what the second read found, other objects' rows for inherited columns included, and keys it by the object's column UIds.")]
	public void Execute_ShouldReportTheSecondRead_WhenEnabling() {
		// Arrange
		Guid other = Guid.NewGuid();
		Reads(false, []);
		_storage.ReadStoredRowsByColumns(default, default).ReturnsForAnyArgs(
			[new StoredColumnRights(other, Stored(NameUId, Row(Role, 0, ColumnRightLevel.Denied)))]);

		// Act
		_command.Execute(new SetColumnRightsOptions { EntitySchemaName = "UsrHoliday", EnableColumnPermissions = true,
			Preview = true });

		// Assert
		_infos.Should().Contain(line => line.Contains("A second read by the object's column UIds found the same rows")
			&& line.Contains("1 row(s) of other objects"), because: "the corroboration is shown in the facts");
		Calls(_storage, nameof(IColumnRightsStorageReader.ReadStoredRowsByColumns)).Should().ContainSingle(
			because: "the rows are read once by column").Which[0].Should().BeEquivalentTo(
			new[] { AmountUId, DescriptionUId, NameUId }, because: "the read is keyed by the object's column UIds");
	}

	[Test]
	[Description("A read-back where the record switch changed fails the call, and the summary says the change was partially applied, naming the switch.")]
	public void Execute_ShouldFail_WhenTheRecordSwitchChanged() {
		// Arrange
		IReadOnlyList<ColumnRights> after = [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied))];
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>())
			.Returns(Object(true, []), Object(true, []), Object(true, after, records: true));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns([], [], after);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "a changed record switch is not this call's change");
		_errors.Should().ContainSingle(line => line.Contains("record permissions are ON, they were OFF")
			&& line.Contains("PARTIALLY applied") && line.Contains("record permissions are ON (were OFF)"),
			because: "the failure names the switch, and the summary includes it");
	}

	[Test]
	[Description("A removal of a row the grantee no longer has is no change: a repeated call succeeds and writes nothing.")]
	public void Execute_ShouldReportNoChange_WhenTheRowToRemoveIsGone() {
		// Arrange
		Reads(true, [Stored(AmountUId, Row(Employees, 0, ColumnRightLevel.Read))]);

		// Act
		int exitCode = _command.Execute(Remove(Role));

		// Assert
		exitCode.Should().Be(0, because: "the removal is already in place");
		_infos.Should().Contain(line => line.EndsWith("(no change)."), because: "the output says nothing changed");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("Removing a column's last row other than the default says, in the summary the host approves, that the column gets the default row and its access WIDENS.")]
	public void Execute_ShouldSayTheAccessWidens_WhenTheLastRowIsRemoved() {
		// Arrange
		Reads(true, [Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied))]);
		SetColumnRightsOptions options = Remove(Role);
		options.Confirm = false;
		options.Preview = true;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the preview ran");
		_infos.Should().Contain(line => line.StartsWith("PREVIEW") && line.Contains("WIDENS access"),
			because: "the summary line names the widening");
	}

	[TestCase(true, false, null, false, null, "--disable-column-permissions is a call of its own")]
	[TestCase(false, false, "denied", true, null, "--remove-row is a call of its own")]
	[TestCase(false, false, "write", false, null, "is not an access level")]
	[TestCase(false, false, null, false, null, "--access is required")]
	[TestCase(false, true, null, false, 0, "--enable-column-permissions goes with a grant")]
	[TestCase(false, false, "read", false, -1, "--position must be 0")]
	[Description("Argument combinations the command cannot read as one change are refused before any read or write.")]
	public void Execute_ShouldRefuseBeforeAnyRead_WhenTheArgumentsAreInvalid(bool disable, bool enable, string access,
		bool removeRow, int? position, string expected) {
		// Arrange
		SetColumnRightsOptions options = new() {
			EntitySchemaName = "UsrHoliday", Column = "UsrAmount", Grantee = Role.ToString(), Access = access,
			RemoveRow = removeRow, Position = position, EnableColumnPermissions = enable,
			DisableColumnPermissions = disable, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the arguments are refused");
		_errors.Should().Contain(line => line.Contains(expected), because: "the error says what is wrong");
		Calls(_reader, nameof(IObjectRightsReader.GetObjectRights)).Should().BeEmpty(because: "nothing is read");
	}
}
