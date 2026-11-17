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
/// <c>set-column-rights</c> end to end over substitutes for the service: the real planner and read-back verifier, the
/// refusals before any write, the whole column list handed to the writer, preview, and the read-back that decides the
/// result — a mismatch fails the call, a column dropped as all-edit is reported as such.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class SetColumnRightsCommandTests : BaseCommandTests<SetColumnRightsOptions> {

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid AmountUId = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid DescriptionUId = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");

	private SetColumnRightsCommand _command;
	private IObjectRightsReader _reader;
	private IColumnRightsWriter _writer;
	private IGranteeLookup _granteeLookup;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private IInteractiveConsole _console;
	private ILogger _logger;
	private List<string> _errors;
	private List<string> _infos;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetColumnRightsCommand>();
	}

	public override void TearDown() {
		_reader.ClearReceivedCalls();
		_writer.ClearReceivedCalls();
		_granteeLookup.ClearReceivedCalls();
		_columnManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_reader = Substitute.For<IObjectRightsReader>();
		_writer = Substitute.For<IColumnRightsWriter>();
		_granteeLookup = Substitute.For<IGranteeLookup>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_console = Substitute.For<IInteractiveConsole>();
		_logger = Substitute.For<ILogger>();
		_errors = new List<string>();
		_infos = new List<string>();
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => _errors.Add((string)call[0]));
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => _infos.Add((string)call[0]));
		_granteeLookup.ResolveGranteeName(Role, Arg.Any<CreatioRequestOptions>()).Returns("Role");
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			new EntitySchemaPropertiesInfo("UsrHoliday", "Holiday", null, "(merged: all packages)", null, false, "Id",
				"Name", 2, 0, null, false, false, null, false, null, false, false, true, false, null, null,
				new[] {
					new EntitySchemaPropertyColumnInfo("UsrAmount", AmountUId, "own", "Amount", null, "Decimal", false,
						false, null),
					new EntitySchemaPropertyColumnInfo("UsrDescription", DescriptionUId, "own", "Description", null, "Text",
						false, false, null)
				}));
		_writer.Save(default, default, default, default).ReturnsForAnyArgs(ObjectRightsSaveResult.Saved);
		containerBuilder.AddTransient(_ => _reader);
		containerBuilder.AddTransient(_ => _writer);
		containerBuilder.AddTransient(_ => _granteeLookup);
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _console);
		containerBuilder.AddTransient(_ => _logger);
	}

	private static ColumnRights Description() =>
		new(DescriptionUId, "Description", new[] { new ColumnRightRow(Employees, "All employees", 0, ColumnRightLevel.Read) });

	private static ColumnRights Amount(params ColumnRightRow[] rows) => new(AmountUId, "Amount", rows);

	private static ObjectRightsInfo Object(bool on, params ColumnRights[] columns) =>
		new(true, "UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), AdministratedByColumns: on,
			Columns: columns);

	// The first read is the object before the call; the second is the read-back after the save.
	private void Reads(ObjectRightsInfo before, ObjectRightsInfo after = null) =>
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(before, after ?? before);

	private static SetColumnRightsOptions Grant(string access = "denied", bool enable = false, bool preview = false,
		string grantee = null, string column = "UsrAmount") =>
		new() {
			EntitySchemaName = "UsrHoliday", Column = column, Grantee = grantee ?? Role.ToString(), Access = access,
			EnableColumnPermissions = enable, Preview = preview, Confirm = !preview
		};

	[Test]
	[Description("A grant hands the writer the WHOLE column list — the other column exactly as read, the changed one with the new row — and succeeds when the read-back matches.")]
	public void Execute_ShouldSaveTheWholeListAndSucceed_WhenTheReadBackMatches() {
		// Arrange
		ColumnRights description = Description();
		ObjectRightsInfo before = Object(true, description);
		ColumnRightsState saved = null;
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Do<ColumnRightsState>(state => saved = state), AmountUId,
			Arg.Any<CreatioRequestOptions>()).Returns(ObjectRightsSaveResult.Saved);
		Reads(before, Object(true, description,
			Amount(new ColumnRightRow(Role, "Role", 0, ColumnRightLevel.Denied))));

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(0, because: "the read-back shows the planned rows");
		saved.Should().NotBeNull(because: "the change is saved");
		saved.Columns.Should().HaveCount(2, because: "the save carries every column, not only the changed one");
		saved.Columns[0].Should().BeSameAs(description, because: "the other column goes back exactly as read");
		saved.Columns[1].Rows.Should().Equal(new[] { new ColumnRightRow(Role, "Role", 0, ColumnRightLevel.Denied) },
			because: "the changed column carries the new row");
		_infos.Should().Contain(line => line.Contains("not bound to a package"),
			because: "the result says the rows live only in this environment");
	}

	[Test]
	[Description("A grant while column permissions are OFF is refused before any write unless enable is passed; with enable the switch goes on in the save.")]
	public void Execute_ShouldRefuseAGrant_WhenColumnPermissionsAreOffWithoutEnable() {
		// Arrange
		Reads(Object(false));

		// Act
		int refused = _command.Execute(Grant());

		// Assert
		refused.Should().Be(1, because: "a grant never turns the switch on as a side effect");
		_errors.Should().Contain(line => line.Contains("--enable-column-permissions"),
			because: "the refusal names the flag that allows it");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
	}

	[Test]
	[Description("An unknown column is refused before any write, naming the object and the column.")]
	public void Execute_ShouldRefuse_WhenTheColumnIsUnknown() {
		// Arrange
		Reads(Object(true));

		// Act
		int exitCode = _command.Execute(Grant(column: "UsrMissing"));

		// Assert
		exitCode.Should().Be(1, because: "a column the schema does not have cannot be changed");
		_errors.Should().Contain(line => line.Contains("has no column 'UsrMissing'"), because: "the error names the column");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
	}

	[Test]
	[Description("An unknown grantee is refused before the object is even read.")]
	public void Execute_ShouldRefuse_WhenTheGranteeIsUnknown() {
		// Arrange
		Guid unknown = Guid.NewGuid();
		_granteeLookup.ResolveGranteeName(unknown, Arg.Any<CreatioRequestOptions>()).Returns((string)null);
		Reads(Object(true));

		// Act
		int exitCode = _command.Execute(Grant(grantee: unknown.ToString()));

		// Assert
		exitCode.Should().Be(1, because: "a row for a principal nobody holds must not be written");
		_errors.Should().Contain(line => line.Contains("was not found in SysAdminUnit"), because: "the error says why");
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
	}

	[Test]
	[Description("A title several objects share is refused with the candidates; nothing is written.")]
	public void Execute_ShouldRefuse_WhenTheTitleIsAmbiguous() {
		// Arrange
		_reader.GetObjectRights("Holiday", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "Holiday", null, false, Array.Empty<RoleOperationRights>()));
		_reader.FindObjectsByTitle("Holiday", Arg.Any<CreatioRequestOptions>()).Returns(new[] {
			new ObjectTitleMatch("UsrHoliday", "Holiday"), new ObjectTitleMatch("UsrHolidayOld", "Holiday")
		});
		SetColumnRightsOptions options = Grant();
		options.EntitySchemaName = "Holiday";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the tool never guesses between objects with the same title");
		_errors.Should().Contain(line => line.Contains("UsrHolidayOld"), because: "the refusal lists the candidates");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
	}

	[Test]
	[Description("A preview writes nothing and shows the column's rows before and after.")]
	public void Execute_ShouldWriteNothing_WhenPreviewIsRequested() {
		// Arrange
		Reads(Object(true, Description()));

		// Act
		int exitCode = _command.Execute(Grant(preview: true));

		// Assert
		exitCode.Should().Be(0, because: "a preview that would succeed is not a failure");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
		_infos.Should().Contain(line => line.StartsWith("PREVIEW"), because: "the output says nothing was changed");
		_infos.Should().Contain(line => line.Contains("rows after: [0] Role"), because: "the plan shows the row added");
		_reader.Received(1).GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A save the service answered success for but did not store fails the call, naming what differs.")]
	public void Execute_ShouldFail_WhenTheReadBackDoesNotMatchThePlan() {
		// Arrange
		Reads(Object(true, Description()), Object(true, Description()));

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "only the read-back proves a column-rights save");
		_errors.Should().Contain(line => line.Contains("do not match the plan") && line.Contains("is missing"),
			because: "the failure says the planned row is not stored");
	}

	[Test]
	[Description("A column left with rows that all grant edit is dropped by the service by design: the call succeeds and says so.")]
	public void Execute_ShouldReportTheDrop_WhenTheColumnEndsAllEdit() {
		// Arrange
		Reads(Object(true), Object(true));

		// Act
		int exitCode = _command.Execute(Grant(access: "edit"));

		// Assert
		exitCode.Should().Be(0, because: "an all-edit column is not stored by design, which is not a failure");
		_infos.Should().Contain(line => line.Contains("stored no row for it"),
			because: "the drop is reported, never silent");
	}

	[Test]
	[Description("Turning column permissions off alone sends no column list, so the stored rows are left as they are.")]
	public void Execute_ShouldSendNoColumnList_WhenOnlyDisabling() {
		// Arrange
		ColumnRights amount = Amount(new ColumnRightRow(Role, "Role", 0, ColumnRightLevel.Denied));
		Reads(Object(true, amount), Object(false, amount));

		// Act
		int exitCode = _command.Execute(new SetColumnRightsOptions {
			EntitySchemaName = "UsrHoliday", DisableColumnPermissions = true, Confirm = true
		});

		// Assert
		exitCode.Should().Be(0, because: "the switch is off on the read-back");
		_writer.Received(1).Save(Arg.Any<ObjectRightsSnapshot>(),
			Arg.Is<ColumnRightsState>(state => state.AdministratedByColumns == false), null, Arg.Any<CreatioRequestOptions>());
		_granteeLookup.DidNotReceiveWithAnyArgs().ResolveGranteeName(default, default);
	}

	[Test]
	[Description("A non-interactive run without --confirm is refused before any write.")]
	public void Execute_ShouldRefuse_WhenNotConfirmedNonInteractively() {
		// Arrange
		Reads(Object(true));
		_console.IsInteractive.Returns(false);
		SetColumnRightsOptions options = Grant();
		options.Confirm = false;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a destructive change needs confirmation");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
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

	[Test]
	[Description("A read with no readable switch refuses the call before any write, and says why.")]
	public void Execute_ShouldRefuse_WhenTheSwitchIsUnknown() {
		// Arrange
		Reads(new ObjectRightsInfo(true, "UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(),
			AdministratedByColumns: null, Columns: Array.Empty<ColumnRights>()));

		// Act
		int exitCode = _command.Execute(Grant(enable: true));

		// Assert
		exitCode.Should().Be(1, because: "an unknown switch is never taken for OFF or ON");
		_errors.Should().Contain(line => line.Contains("no readable") && line.Contains("Nothing was changed"),
			because: "the refusal says the switch could not be read");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
	}

	[Test]
	[Description("A row of the changed column without a position refuses the call before any write, naming the row and why the list must go back exactly.")]
	public void Execute_ShouldRefuse_WhenTheChangedColumnHasAnUnreadableRow() {
		// Arrange
		Reads(Object(true, Amount(new ColumnRightRow(Employees, "All employees", null, ColumnRightLevel.Read))));

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "the column's rows could not be sent back exactly as stored");
		_errors.Should().Contain(line => line.Contains("[?] All employees") && line.Contains("exactly as stored"),
			because: "the refusal names the row and the reason");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
	}

	[Test]
	[Description("A column entry whose UId is not a GUID refuses a row change before any write, naming the raw UId.")]
	public void Execute_ShouldRefuse_WhenAColumnUIdIsUnreadable() {
		// Arrange
		Reads(Object(true, new ColumnRights(null, "Odd",
			new[] { new ColumnRightRow(Employees, "All employees", 0, ColumnRightLevel.Read) }, "not-a-guid")));

		// Act
		int exitCode = _command.Execute(Grant());

		// Assert
		exitCode.Should().Be(1, because: "the entry could be the changed column, which a save would then store twice");
		_errors.Should().Contain(line => line.Contains("'not-a-guid'"), because: "the refusal names the raw UId");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
	}
}
