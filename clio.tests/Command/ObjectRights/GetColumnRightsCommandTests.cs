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

[TestFixture]
[Property("Module", "Command")]
public class GetColumnRightsCommandTests : BaseCommandTests<GetColumnRightsOptions> {

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Admins = Guid.Parse("83a43ebc-f36b-1410-298d-001e8c82bcad");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid DescriptionUId = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");
	private static readonly Guid AmountUId = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid NameUId = Guid.Parse("9e53fd7c-3333-4c5b-9c3a-2a7b0e7e0003");

	private GetColumnRightsCommand _command;
	private IObjectRightsReader _rightsReader;
	private IColumnRightsStorageReader _storage;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetColumnRightsCommand>();
	}

	public override void TearDown() {
		_rightsReader.ClearReceivedCalls();
		_storage.ClearReceivedCalls();
		_columnManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_rightsReader = Substitute.For<IObjectRightsReader>();
		_storage = Substitute.For<IColumnRightsStorageReader>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		// Default: the object's columns resolve to their codes.
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(Schema(
			("UsrDescription", DescriptionUId), ("UsrAmount", AmountUId), ("Name", NameUId)));
		containerBuilder.AddTransient(_ => _rightsReader);
		containerBuilder.AddTransient(_ => _storage);
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
	}

	private static EntitySchemaPropertiesInfo Schema(params (string Name, Guid UId)[] columns) =>
		new("UsrHoliday", "Holiday", null, "(merged: all packages)", null, false, "Id", "Name", columns.Length, 0, null,
			false, false, null, false, null, false, false, true, false, null, null,
			columns.Select(column => new EntitySchemaPropertyColumnInfo(column.Name, column.UId, "own", column.Name,
				null, "Text", false, false, null)).ToList());

	private static ObjectRightsInfo Object(bool? administratedByColumns, params ColumnRights[] columns) =>
		new(true, "UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(),
			AdministratedByColumns: administratedByColumns, Columns: columns);

	private static ColumnRights Column(Guid uId, string caption, params ColumnRightRow[] rows) => new(uId, caption, rows);

	private static ColumnRightRow Row(Guid grantee, string name, int? position, ColumnRightLevel? level) =>
		new(grantee, name, position, level);

	private void Returns(ObjectRightsInfo info) =>
		_rightsReader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(info);

	private int Run(string column = null, string grantee = null, string name = "UsrHoliday") =>
		_command.Execute(new GetColumnRightsOptions { EntitySchemaName = name, Column = column, Grantee = grantee });

	[Test]
	[Description("With column permissions OFF and no stored row, says so plainly — every column is governed only by object and record permissions — and never prints an empty list.")]
	public void Execute_ShouldSayColumnPermissionsAreOff_WhenSwitchIsOff() {
		// Arrange
		Returns(Object(false));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "an OFF switch is a fact, not a failure");
		_logger.Received().WriteInfo($"  {GetColumnRightsCommand.SwitchOff}");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains("Columns with rows")));
		_columnManager.DidNotReceiveWithAnyArgs().GetSchemaProperties(default);
	}

	[Test]
	[Description("With column permissions ON and no row, states only the measured fact that no column has a permission row.")]
	public void Execute_ShouldSayNoColumnHasARow_WhenSwitchIsOnWithoutRows() {
		// Arrange
		Returns(Object(true));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "an empty column layer is a fact, not a failure");
		_logger.Received().WriteInfo($"  {GetColumnRightsCommand.OnWithoutRows}");
		_logger.DidNotReceive().WriteInfo($"  {GetColumnRightsCommand.SwitchOff}");
	}

	[Test]
	[Description("With column permissions ON, each column is shown by its code (resolved from its UId) and caption, with its rows in stored position order and the level as a word.")]
	public void Execute_ShouldListEachColumnByCodeAndCaption_WhenSwitchIsOnWithRows() {
		// Arrange
		Returns(Object(true,
			Column(DescriptionUId, "Description",
				Row(Admins, "System administrators", 0, ColumnRightLevel.Edit),
				Row(Employees, "All employees", 1, ColumnRightLevel.Denied)),
			Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Read))));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		Received.InOrder(() => {
			_logger.WriteInfo("Column permissions for 'Holiday' (UsrHoliday):");
			_logger.WriteInfo($"  {GetColumnRightsCommand.OrderRule}");
			_logger.WriteInfo("  Column permissions: ON. Columns with rows:");
			_logger.WriteInfo("    UsrAmount 'Amount':");
			_logger.WriteInfo($"      [0] All employees ({Employees}): read");
			_logger.WriteInfo("    UsrDescription 'Description':");
			_logger.WriteInfo($"      [0] System administrators ({Admins}): edit");
			_logger.WriteInfo($"      [1] All employees ({Employees}): denied");
		});
	}

	[Test]
	[Description("A level the service returned that is none of denied/read/edit is shown as the number it is, never as a known level.")]
	public void Execute_ShouldShowUnknownLevel_WhenRowCarriesAnUnknownLevel() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, (ColumnRightLevel)7))));

		// Act
		Run();

		// Assert
		_logger.Received().WriteInfo($"      [0] All employees ({Employees}): unknown (7)");
	}

	[Test]
	[Description("Rows stored while column permissions are OFF are listed under the OFF line, not as rows in effect.")]
	public void Execute_ShouldListStoredRowsUnderOff_WhenSwitchIsOffWithRows() {
		// Arrange
		Returns(Object(false, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Denied))));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		Received.InOrder(() => {
			_logger.WriteInfo($"  {GetColumnRightsCommand.SwitchOff}");
			_logger.WriteInfo("  Rows stored while column permissions are OFF:");
			_logger.WriteInfo("    UsrAmount 'Amount':");
			_logger.WriteInfo($"      [0] All employees ({Employees}): denied");
		});
	}

	[Test]
	[Description("A failed service read fails the call (exit 1) and is never reported as column permissions OFF.")]
	public void Execute_ShouldFail_WhenTheReadFails() {
		// Arrange
		Returns(ObjectRightsInfo.ReadFailed("UsrHoliday", "Access denied"));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(1, because: "a read that did not happen is a failure");
		_logger.Received().WriteError("  UsrHoliday: its column permissions could not be read: Access denied.");
		_logger.DidNotReceive().WriteInfo($"  {GetColumnRightsCommand.SwitchOff}");
	}

	[Test]
	[Description("A service failure that escapes the reader is reported the same way as a failed read, and fails the call.")]
	public void Execute_ShouldFail_WhenTheReaderThrowsAServiceFailure() {
		// Arrange
		_rightsReader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new TimeoutException("timed out"));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(1, because: "a read that timed out did not happen");
		_logger.Received().WriteError(Arg.Is<string>(line =>
			line.StartsWith("  UsrHoliday: its column permissions could not be read:") && line.Contains("timed out")));
	}

	[Test]
	[Description("--column shows only that column, matched by its code through the entity schema, and the header names it by the schema's spelling, not the caller's casing.")]
	public void Execute_ShouldShowOnlyTheNamedColumn_WhenColumnIsGiven() {
		// Arrange
		Returns(Object(true,
			Column(DescriptionUId, "Description", Row(Employees, "All employees", 0, ColumnRightLevel.Denied)),
			Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Read))));

		// Act
		int exitCode = Run(column: "usramount");

		// Assert
		exitCode.Should().Be(0, because: "the column exists");
		_logger.Received().WriteInfo("    UsrAmount 'Amount':");
		_logger.DidNotReceive().WriteInfo("    UsrDescription 'Description':");
		_logger.Received().WriteInfo("Column permissions for 'Holiday' (UsrHoliday) (column UsrAmount):");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains("usramount")));
	}

	[Test]
	[Description("--column naming a column of the object that has no row reports it has none, by the code as the schema spells it.")]
	public void Execute_ShouldSayTheColumnHasNoRows_WhenNamedColumnIsUnrestricted() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Read))));

		// Act
		int exitCode = Run(column: "NAME");

		// Assert
		exitCode.Should().Be(0, because: "an unrestricted column is a fact");
		_logger.Received().WriteInfo("    Name: no column permission rows.");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains("NAME")));
		_logger.DidNotReceive().WriteInfo("    UsrAmount 'Amount':");
	}

	[Test]
	[Description("--column naming no column of the object fails the call instead of answering with nothing.")]
	public void Execute_ShouldFail_WhenNamedColumnIsNotAColumnOfTheObject() {
		// Arrange
		Returns(Object(true));

		// Act
		int exitCode = Run(column: "UsrMissing");

		// Assert
		exitCode.Should().Be(1, because: "a typo in the column must not read as 'no rows'");
		_logger.Received().WriteError("Error: 'Holiday' (UsrHoliday) has no column 'UsrMissing'.");
	}

	[Test]
	[Description("--column that is not a column code is refused with the reason before anything is read: neither the rights nor the schema.")]
	public void Execute_ShouldRefuse_WhenColumnIsNotACode() {
		// Arrange & Act
		int exitCode = Run(column: "Amount; drop");

		// Assert
		exitCode.Should().Be(1, because: "only a column code is accepted");
		_logger.Received(1).WriteError(
			"Error: --column 'Amount; drop' is not a column code (letters, digits and '_' only).");
		_rightsReader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		_columnManager.DidNotReceiveWithAnyArgs().GetSchemaProperties(default);
	}

	[Test]
	[Description("When the columns cannot be read, each column is shown by its UId and caption with a warning, and the call still succeeds.")]
	public void Execute_ShouldShowColumnsByUId_WhenColumnCodesCannotBeRead() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Read))));
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new EntitySchemaDesignerException("schema read failed"));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the rights were read; only the column codes are missing");
		_logger.Received().WriteWarning(Arg.Is<string>(line => line.Contains("could not be read")
			&& line.Contains("schema read failed")));
		_logger.Received().WriteInfo($"    column {AmountUId} 'Amount' (code not resolved):");
	}

	[Test]
	[Description("With --column, columns that cannot be read fail the call: the column cannot be found.")]
	public void Execute_ShouldFail_WhenColumnIsGivenAndColumnCodesCannotBeRead() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Read))));
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new EntitySchemaDesignerException("schema read failed"));

		// Act
		int exitCode = Run(column: "UsrAmount");

		// Assert
		exitCode.Should().Be(1, because: "the filter cannot be applied without the column codes");
		_logger.Received().WriteError(Arg.Is<string>(line => line.Contains("--column UsrAmount cannot be found")));
	}

	[Test]
	[Description("--grantee shows, per column, the grantee's row first and then the column's other rows in stored position order: which row wins is not measured for columns, so no row is left out.")]
	public void Execute_ShouldShowGranteeRowThenOtherRows_WhenGranteeIsGiven() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount",
			Row(Admins, "System administrators", 0, ColumnRightLevel.Edit),
			Row(Role, "Sales managers", 1, ColumnRightLevel.Read),
			Row(Employees, "All employees", 2, ColumnRightLevel.Denied))));

		// Act
		int exitCode = Run(grantee: Role.ToString());

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		Received.InOrder(() => {
			_logger.WriteInfo($"      [1] Sales managers ({Role}): read");
			_logger.WriteInfo("      Other rows of this column:");
			_logger.WriteInfo($"        [0] System administrators ({Admins}): edit");
			_logger.WriteInfo($"        [2] All employees ({Employees}): denied");
		});
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains("above")));
	}

	[Test]
	[Description("--grantee with two rows in one column shows both of them first, then every other row of the column, including one stored between them.")]
	public void Execute_ShouldShowEveryGranteeRowThenTheOthers_WhenGranteeHasTwoRowsInOneColumn() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount",
			Row(Admins, "System administrators", 0, ColumnRightLevel.Edit),
			Row(Role, "Sales managers", 1, ColumnRightLevel.Read),
			Row(Employees, "All employees", 2, ColumnRightLevel.Denied),
			Row(Role, "Sales managers", 3, ColumnRightLevel.Denied))));

		// Act
		int exitCode = Run(grantee: Role.ToString());

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		Received.InOrder(() => {
			_logger.WriteInfo($"      [1] Sales managers ({Role}): read");
			_logger.WriteInfo($"      [3] Sales managers ({Role}): denied");
			_logger.WriteInfo("      Other rows of this column:");
			_logger.WriteInfo($"        [0] System administrators ({Admins}): edit");
			_logger.WriteInfo($"        [2] All employees ({Employees}): denied");
		});
		_logger.DidNotReceive().WriteInfo($"        [3] Sales managers ({Role}): denied");
	}

	[Test]
	[Description("--grantee with no row in a column says so and lists every row of that column.")]
	public void Execute_ShouldListEveryRow_WhenGranteeHasNoRowInTheColumn() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Denied))));

		// Act
		Run(grantee: Role.ToString());

		// Assert
		_logger.Received().WriteInfo($"      grantee {Role} has NO row in this column. Its rows:");
		_logger.Received().WriteInfo($"        [0] All employees ({Employees}): denied");
	}

	[Test]
	[Description("--grantee that is not a GUID is refused before anything is read.")]
	public void Execute_ShouldRefuse_WhenGranteeIsNotAGuid() {
		// Arrange & Act
		int exitCode = Run(grantee: "Sales managers");

		// Assert
		exitCode.Should().Be(1, because: "the grantee is a SysAdminUnit id");
		_logger.Received().WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
		_rightsReader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}

	[Test]
	[Description("When no object has the code, the one object with that title is read and the output says it was found by its title.")]
	public void Execute_ShouldReadTheObjectByTitle_WhenNoObjectHasTheCode() {
		// Arrange
		_rightsReader.GetObjectRights("Holiday", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "Holiday", null, false, Array.Empty<RoleOperationRights>()));
		_rightsReader.FindObjectsByTitle("Holiday", Arg.Any<CreatioRequestOptions>())
			.Returns(new[] { new ObjectTitleMatch("UsrHoliday", "Holiday") });
		Returns(Object(true));

		// Act
		int exitCode = Run(name: "Holiday");

		// Assert
		exitCode.Should().Be(0, because: "the title names one object");
		_logger.Received().WriteInfo("  'Holiday' is not an object code: it is the title of UsrHoliday, which is read.");
	}

	[Test]
	[Description("A title several objects share is refused, listing them, and nothing is read: the read never guesses between objects.")]
	public void Execute_ShouldRefuse_WhenSeveralObjectsHaveTheTitle() {
		// Arrange
		_rightsReader.GetObjectRights("Holiday", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "Holiday", null, false, Array.Empty<RoleOperationRights>()));
		_rightsReader.FindObjectsByTitle("Holiday", Arg.Any<CreatioRequestOptions>())
			.Returns(new[] { new ObjectTitleMatch("UsrHoliday", "Holiday"), new ObjectTitleMatch("UsrDayOff", "Holiday") });

		// Act
		int exitCode = Run(name: "Holiday");

		// Assert
		exitCode.Should().Be(1, because: "an ambiguous title is refused");
		_logger.Received().WriteError(Arg.Is<string>(line => line.Contains("is the title of 2 objects")
			&& line.EndsWith("Re-run with the exact code.")));
		_rightsReader.DidNotReceive().GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("The column codes are read with the request timeout, so the schema read honours the call's limits like the rights read.")]
	public void Execute_ShouldReadColumnCodesWithTheRequestTimeout_WhenColumnsHaveRows() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Read))));
		GetColumnRightsOptions options = new() { EntitySchemaName = "UsrHoliday", TimeOut = 30_000 };

		// Act
		_command.Execute(options);

		// Assert
		_columnManager.Received(1).GetSchemaProperties(Arg.Is<GetEntitySchemaPropertiesOptions>(read =>
			read.SchemaName == "UsrHoliday" && read.RuntimeReadTimeoutMilliseconds == 30_000 && read.Package == null));
	}

	[Test]
	[Description("When the service did not return the column switch, the output says so and lists the stored rows; it never says column permissions are OFF or ON.")]
	public void Execute_ShouldSayTheSwitchIsUnknown_WhenTheServiceDidNotReturnIt() {
		// Arrange
		Returns(Object(null, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Denied))));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the rows were read; only the switch is missing");
		Received.InOrder(() => {
			_logger.WriteInfo($"  {GetColumnRightsCommand.SwitchMissing}");
			_logger.WriteInfo("  Stored rows:");
			_logger.WriteInfo("    UsrAmount 'Amount':");
			_logger.WriteInfo($"      [0] All employees ({Employees}): denied");
		});
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains("OFF") || line.Contains(": ON")));
	}

	[Test]
	[Description("When the service returned neither the column switch nor any row, the output says the switch is unknown and that no column has a row.")]
	public void Execute_ShouldSayTheSwitchIsUnknownAndNoRows_WhenTheServiceReturnedNeither() {
		// Arrange
		Returns(Object(null));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received(1).WriteInfo($"  {GetColumnRightsCommand.SwitchMissing}");
		_logger.Received(1).WriteInfo("  No column has a permission row.");
		_logger.DidNotReceive().WriteInfo($"  {GetColumnRightsCommand.SwitchOff}");
		_logger.DidNotReceive().WriteInfo($"  {GetColumnRightsCommand.OnWithoutRows}");
	}

	[Test]
	[Description("A row without a position is shown as [?], never with a position the service did not return.")]
	public void Execute_ShouldShowAQuestionMark_WhenRowHasNoPosition() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount",
			Row(Admins, "System administrators", 0, ColumnRightLevel.Edit),
			Row(Employees, "All employees", null, ColumnRightLevel.Denied))));

		// Act
		Run();

		// Assert
		Received.InOrder(() => {
			_logger.WriteInfo($"      [0] System administrators ({Admins}): edit");
			_logger.WriteInfo($"      [?] All employees ({Employees}): denied");
		});
	}

	[Test]
	[Description("A level the service returned that is not a number is shown as returned, unknown (value), not as unknown (no level).")]
	public void Execute_ShouldShowTheReturnedValue_WhenLevelIsNotANumber() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount",
			new ColumnRightRow(Employees, "All employees", 0, null, "{\"value\":1}"))));

		// Act
		Run();

		// Assert
		_logger.Received().WriteInfo($"      [0] All employees ({Employees}): unknown ({{\"value\":1}})");
	}

	[Test]
	[Description("A stored column whose UId the schema, read successfully, does not have is shown as not a column of the schema, never as a code that was not resolved.")]
	public void Execute_ShouldSayNotAColumnOfTheSchema_WhenTheSchemaWasReadAndLacksTheColumn() {
		// Arrange
		Guid gone = Guid.Parse("9e53fd7c-9999-4c5b-9c3a-2a7b0e7e0009");
		ReturnsWithStorage(true, Column(gone, null, Row(Employees, "All employees", 0, ColumnRightLevel.Denied)));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo($"    column {gone} (not a column of the schema):");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains("code not resolved")));
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("A column whose UId the service returned is not a GUID is shown with that value as returned; one with no UId says so.")]
	public void Execute_ShouldShowTheReturnedUId_WhenColumnUIdIsNotAGuid() {
		// Arrange
		Returns(Object(true,
			new ColumnRights(null, "First", new[] { Row(Employees, "All employees", 0, ColumnRightLevel.Denied) },
				"not-a-guid"),
			new ColumnRights(null, "Second", new[] { Row(Admins, "System administrators", 0, ColumnRightLevel.Read) })));

		// Act
		Run();

		// Assert
		_logger.Received().WriteInfo("    column 'First' (column UId 'not-a-guid' is not a GUID):");
		_logger.Received().WriteInfo("    column 'Second' (no column UId):");
	}

	[Test]
	[Description("Columns with a code come first, by code; columns without one follow, by caption, whatever order the service listed them in.")]
	public void Execute_ShouldListResolvedColumnsByCodeThenUnresolvedByCaption_WhenSomeCodesAreUnknown() {
		// Arrange
		Guid zeta = Guid.Parse("9e53fd7c-aaaa-4c5b-9c3a-2a7b0e7e000a");
		Guid alpha = Guid.Parse("9e53fd7c-bbbb-4c5b-9c3a-2a7b0e7e000b");
		ColumnRightRow row = Row(Employees, "All employees", 0, ColumnRightLevel.Denied);
		Returns(Object(true,
			Column(zeta, "Zeta", row),
			Column(AmountUId, "Amount", row),
			Column(alpha, "alpha", row),
			Column(NameUId, "Name", row)));

		// Act
		Run();

		// Assert
		Received.InOrder(() => {
			_logger.WriteInfo("    Name 'Name':");
			_logger.WriteInfo("    UsrAmount 'Amount':");
			_logger.WriteInfo($"    column {alpha} 'alpha' (not a column of the schema):");
			_logger.WriteInfo($"    column {zeta} 'Zeta' (not a column of the schema):");
		});
	}

	[Test]
	[Description("When the call's time limit is spent before the columns are read, the schema is not read: a warning names the spent limit and each column is shown by its UId and caption.")]
	public void Execute_ShouldNotReadTheSchema_WhenTheCallsTimeLimitIsSpent() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Read))));
		GetColumnRightsOptions options = new() { EntitySchemaName = "UsrHoliday", ReadBudget = TimeSpan.Zero };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the rights were read; only the column codes are missing");
		_columnManager.DidNotReceiveWithAnyArgs().GetSchemaProperties(default);
		_logger.Received(1).WriteWarning("  The columns of UsrHoliday could not be read (the call's time limit is spent): "
			+ "columns are shown by their UId and caption.");
		_logger.Received().WriteInfo($"    column {AmountUId} 'Amount' (code not resolved):");
	}

	[Test]
	[Description("--column with column permissions OFF shows only that column's stored rows, under the OFF line.")]
	public void Execute_ShouldShowOnlyTheNamedColumnUnderOff_WhenColumnIsGivenAndSwitchIsOff() {
		// Arrange
		Returns(Object(false,
			Column(DescriptionUId, "Description", Row(Employees, "All employees", 0, ColumnRightLevel.Denied)),
			Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Read))));

		// Act
		int exitCode = Run(column: "UsrAmount");

		// Assert
		exitCode.Should().Be(0, because: "the column exists");
		Received.InOrder(() => {
			_logger.WriteInfo("Column permissions for 'Holiday' (UsrHoliday) (column UsrAmount):");
			_logger.WriteInfo($"  {GetColumnRightsCommand.SwitchOff}");
			_logger.WriteInfo("  Rows stored while column permissions are OFF:");
			_logger.WriteInfo("    UsrAmount 'Amount':");
			_logger.WriteInfo($"      [0] All employees ({Employees}): read");
		});
		_logger.DidNotReceive().WriteInfo("    UsrDescription 'Description':");
	}

	private static readonly Guid SchemaUId = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");

	// An object whose schema UId was read, so its stored rows are read from storage.
	private void ReturnsWithStorage(bool? on, params ColumnRights[] stored) {
		_rightsReader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsInfo(true,
			"UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: on, Columns: Array.Empty<ColumnRights>()));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns(stored);
	}

	[Test]
	[Description("With column permissions ON, storage is shown: a restricted column with its rows, a column with only the default row (All employees, edit) as default — never as a restriction — and a schema column with no stored row as one only system administrators can access.")]
	public void Execute_ShouldShowRestrictedDefaultAndLockedColumns_WhenStorageIsReadWhileOn() {
		// Arrange
		ReturnsWithStorage(true,
			Column(AmountUId, null, Row(Role, "Role", 0, ColumnRightLevel.Denied)),
			Column(NameUId, null, Row(Employees, "All employees", 0, ColumnRightLevel.Edit)));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the read ran");
		_logger.Received().WriteInfo("    UsrAmount:");
		_logger.Received().WriteInfo($"      [0] Role ({Role}): denied");
		_logger.Received().WriteInfo($"  {GetColumnRightsCommand.DefaultOnly} (1): Name.");
		_logger.Received().WriteInfo($"  {GetColumnRightsCommand.NoRowsLocked} (1): UsrDescription.");
		_logger.DidNotReceive().WriteInfo("    Name:");
	}

	[Test]
	[Description("With column permissions OFF, the stored rows are listed as stored but not applied, and the default-only columns are summarised as on the ON branch, never listed as restrictions.")]
	public void Execute_ShouldListStoredRowsAndSummariseDefaults_WhenStorageIsReadWhileOff() {
		// Arrange
		ReturnsWithStorage(false, Column(AmountUId, null, Row(Role, "Role", 0, ColumnRightLevel.Denied)),
			Column(NameUId, null, Row(Employees, "All employees", 0, ColumnRightLevel.Edit)));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the read ran");
		_logger.Received().WriteInfo($"  {GetColumnRightsCommand.SwitchOff}");
		_logger.Received().WriteInfo("  Rows stored while column permissions are OFF:");
		_logger.Received().WriteInfo($"      [0] Role ({Role}): denied");
		_logger.Received().WriteInfo($"  {GetColumnRightsCommand.DefaultOnly} (1): Name.");
		_logger.DidNotReceive().WriteInfo("    Name:");
	}

	[Test]
	[Description("--column naming a column with no stored row while ON reports it as one only system administrators can access.")]
	public void Execute_ShouldReportTheNamedColumnAsLocked_WhenItHasNoStoredRowWhileOn() {
		// Arrange
		ReturnsWithStorage(true, Column(AmountUId, null, Row(Role, "Role", 0, ColumnRightLevel.Denied)));

		// Act
		int exitCode = Run(column: "usrdescription");

		// Assert
		exitCode.Should().Be(0, because: "the column exists");
		_logger.Received().WriteInfo($"  {GetColumnRightsCommand.NoRowsLocked} (1): UsrDescription.");
		_logger.DidNotReceive().WriteInfo("    UsrAmount:");
		_logger.Received().WriteInfo("  Column permissions: ON.");
		_logger.DidNotReceive().WriteInfo($"  {GetColumnRightsCommand.OnWithoutRows}");
	}

	[Test]
	[Description("When storage cannot be read, the service read is shown with a warning that default columns are not listed by it, and the columns it lists with no rows are shown as accessible only to system administrators.")]
	public void Execute_ShouldFallBackWithAWarning_WhenStorageCannotBeRead() {
		// Arrange
		_rightsReader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsInfo(true,
			"UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: true,
			Columns: new[] {
				Column(AmountUId, "Amount", Row(Role, "Role", 0, ColumnRightLevel.Denied)),
				Column(DescriptionUId, "Description")
			}));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>())
			.Returns(_ => throw new InvalidOperationException("SelectQuery failed: Access denied"));

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the service read still answers");
		_logger.Received().WriteWarning(Arg.Is<string>(line => line.Contains("Access denied")
			&& line.Contains("not shown at all")));
		_logger.Received().WriteInfo("    UsrAmount 'Amount':");
		_logger.Received().WriteInfo($"  {GetColumnRightsCommand.NoRowsLocked} (1): UsrDescription.");
	}
}
