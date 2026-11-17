using System;
using System.Collections.Generic;
using System.Linq;
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
/// <c>set-column-rights</c> over substitutes for the service and storage: the real planner and read-back verifier, the
/// list built from STORAGE (a locked column kept, a default column left out), defaults rather than a lock when enabling,
/// no list on a disable, the storage read-back that decides the result, and the refusals before any write.
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
	private IGranteeLookup _granteeLookup;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private IInteractiveConsole _console;
	private ILogger _logger;
	private List<string> _errors;
	private List<string> _infos;
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
		_granteeLookup = Substitute.For<IGranteeLookup>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_console = Substitute.For<IInteractiveConsole>();
		_logger = Substitute.For<ILogger>();
		_errors = new List<string>();
		_infos = new List<string>();
		_sent = false;
		_sentList = null;
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => _errors.Add((string)call[0]));
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => _infos.Add((string)call[0]));
		_granteeLookup.ResolveGranteeName(Role, Arg.Any<CreatioRequestOptions>()).Returns("Role");
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			new EntitySchemaPropertiesInfo("UsrHoliday", "Holiday", null, "(merged: all packages)", null, false, "Id",
				"Name", 3, 0, null, false, false, null, false, null, false, false, true, false, null, null,
				new[] {
					SchemaColumn("UsrAmount", AmountUId, "Amount"),
					SchemaColumn("UsrDescription", DescriptionUId, "Description"),
					SchemaColumn("Name", NameUId, "Name")
				}));
		_writer.Save(default, default, default, default).ReturnsForAnyArgs(call => {
			_sent = true;
			_sentList = (IReadOnlyList<ColumnRights>)call[2];
			return ObjectRightsSaveResult.Saved;
		});
		containerBuilder.AddTransient(_ => _reader);
		containerBuilder.AddTransient(_ => _storage);
		containerBuilder.AddTransient(_ => _writer);
		containerBuilder.AddTransient(_ => _granteeLookup);
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _console);
		containerBuilder.AddTransient(_ => _logger);
	}

	private static EntitySchemaPropertyColumnInfo SchemaColumn(string name, Guid uId, string title) =>
		new(name, uId, "own", title, null, "Text", false, false, null);

	private static ColumnRightRow Row(Guid grantee, int position, ColumnRightLevel level) =>
		new(grantee, grantee == Employees ? "All employees" : "Role", position, level);

	private static ColumnRights Stored(Guid column, params ColumnRightRow[] rows) => new(column, null, rows);

	private static ObjectRightsInfo Object(bool? on) =>
		new(true, "UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: on, Columns: Array.Empty<ColumnRights>());

	// The switch before and after (service read), and the stored rows before and after (storage read).
	private void Reads(bool? on, IReadOnlyList<ColumnRights> stored, bool? onAfter = null,
		IReadOnlyList<ColumnRights> storedAfter = null) {
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>())
			.Returns(Object(on), Object(onAfter ?? on));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>()).Returns(stored, storedAfter ?? stored);
	}

	private static SetColumnRightsOptions Grant(string access = "denied", bool enable = false, bool preview = false,
		string grantee = null, string column = "UsrAmount") =>
		new() {
			EntitySchemaName = "UsrHoliday", Column = column, Grantee = grantee ?? Role.ToString(), Access = access,
			EnableColumnPermissions = enable, Preview = preview, Confirm = !preview
		};

	private static SetColumnRightsOptions Switch(bool enable) =>
		new() { EntitySchemaName = "UsrHoliday", EnableColumnPermissions = enable, DisableColumnPermissions = !enable,
			Confirm = true };

	[Test]
	[Description("A grant while ON sends the list built from storage — a restricted column as stored, a locked column (no rows) with none, a default-only column left out — and succeeds when storage reads back as planned.")]
	public void Execute_ShouldSendTheListBuiltFromStorage_WhenSwitchIsOn() {
		// Arrange
		ColumnRights description = Stored(DescriptionUId, Row(Role, 0, ColumnRightLevel.Read));
		ColumnRights nameDefault = Stored(NameUId, ColumnRightsDefaults.Row);
		ColumnRights amountAfter = Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied));
		// UsrAmount has no stored row, so while ON it is a locked column; the grant gives it a row.
		Reads(true, [description, nameDefault], storedAfter: [amountAfter, description, nameDefault]);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(0, because: "storage holds the planned rows");
		_sentList.Select(column => column.ColumnUId).Should().BeEquivalentTo(new Guid?[] { AmountUId, DescriptionUId },
			because: "the default-only column is left out and every other column is named");
		_sentList.Single(column => column.ColumnUId == DescriptionUId).Rows.Should().Equal(description.Rows,
			because: "another column goes back as stored");
		_infos.Should().Contain(line => line.Contains("not bound to a package"), because: "the rows stay in this environment");
	}

	[Test]
	[Description("While ON, a column with no stored row other than the changed one is sent with none, so it stays locked, and the facts say so.")]
	public void Execute_ShouldKeepALockedColumnLocked_WhenAnotherColumnChanges() {
		// Arrange
		ColumnRights amount = Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Read));
		Reads(true, [amount, Stored(NameUId, ColumnRightsDefaults.Row)], storedAfter: [
			Stored(AmountUId, Row(Role, 0, ColumnRightLevel.Denied)), Stored(NameUId, ColumnRightsDefaults.Row)
		]);

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
		_infos.Should().Contain(line => line.Contains("Get the default row") && line.Contains("Name, UsrAmount, UsrDescription"),
			because: "the output names the columns that get the default");
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
	[Description("A save the service answered success for whose rows storage does not hold fails the call, naming what differs.")]
	public void Execute_ShouldFail_WhenStorageDoesNotMatchThePlan() {
		// Arrange
		Reads(true, [], storedAfter: []);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "only storage proves a column-rights save");
		_errors.Should().Contain(line => line.Contains("do not match the plan") && line.Contains("is missing"),
			because: "the failure says the planned row is not stored");
		_storage.Received(2).ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("When storage cannot be read the call is refused before any write: the list is built from it.")]
	public void Execute_ShouldRefuse_WhenStorageCannotBeRead() {
		// Arrange
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(Object(true));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>())
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
	[Description("A grant while OFF without enable, an unknown switch, an unknown column, an unknown grantee and an ambiguous title are refused before any write.")]
	public void Execute_ShouldRefuseBeforeAnyWrite_WhenTheCallCannotBePlanned() {
		// Arrange
		Guid unknown = Guid.NewGuid();
		_granteeLookup.ResolveGranteeName(unknown, Arg.Any<CreatioRequestOptions>()).Returns((string)null);
		_reader.FindObjectsByTitle("Holiday", Arg.Any<CreatioRequestOptions>()).Returns(new[] {
			new ObjectTitleMatch("UsrHoliday", "Holiday"), new ObjectTitleMatch("UsrHolidayOld", "Holiday")
		});
		_reader.GetObjectRights("Holiday", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "Holiday", null, false, Array.Empty<RoleOperationRights>()));
		SetColumnRightsOptions byTitle = Grant();
		byTitle.EntitySchemaName = "Holiday";

		// Act
		Reads(false, []);
		int off = _command.Execute(Grant());
		Reads(null, []);
		int unknownSwitch = _command.Execute(Grant(enable: true));
		Reads(true, []);
		int unknownColumn = _command.Execute(Grant(column: "UsrMissing"));
		int unknownGrantee = _command.Execute(Grant(grantee: unknown.ToString()));
		int ambiguous = _command.Execute(byTitle);

		// Assert
		new[] { off, unknownSwitch, unknownColumn, unknownGrantee, ambiguous }.Should().AllBeEquivalentTo(1,
			because: "each call is refused");
		_errors.Should().Contain(line => line.Contains("--enable-column-permissions"), because: "OFF names the flag");
		_errors.Should().Contain(line => line.Contains("no readable"), because: "an unknown switch is said");
		_errors.Should().Contain(line => line.Contains("has no column 'UsrMissing'"), because: "the column is named");
		_errors.Should().Contain(line => line.Contains("was not found in SysAdminUnit"), because: "the grantee is checked");
		_errors.Should().Contain(line => line.Contains("UsrHolidayOld"), because: "the candidates are listed");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A stored row without a level refuses a row change before any write, naming the row.")]
	public void Execute_ShouldRefuse_WhenAStoredRowIsUnreadable() {
		// Arrange
		Reads(true, [Stored(DescriptionUId, new ColumnRightRow(Employees, "All employees", 0, null, "(none)"))]);

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "the row could not be sent back as stored");
		_errors.Should().Contain(line => line.Contains("[0] All employees") && line.Contains("exactly as stored"),
			because: "the refusal names the row and the reason");
		_sent.Should().BeFalse(because: "nothing is written");
	}

	[Test]
	[Description("A preview writes nothing and shows the column's stored rows before and after.")]
	public void Execute_ShouldWriteNothing_WhenPreviewIsRequested() {
		// Arrange
		Reads(true, []);

		// Act
		int exitCode = _command.Execute(Grant(preview: true));

		// Assert
		exitCode.Should().Be(0, because: "a preview that would succeed is not a failure");
		_sent.Should().BeFalse(because: "a preview writes nothing");
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
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}
}
