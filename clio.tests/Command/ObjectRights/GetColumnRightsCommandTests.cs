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
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"  {GetColumnRightsCommand.SwitchOff}", because: "the output states this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain(line => line.Contains("Columns with rows"), because: "the output must not state this");
		ReceivedCallLog.CallsTo(_columnManager, nameof(IRemoteEntitySchemaColumnManager.GetSchemaProperties)).Should()
			.BeEmpty(because: "the schema is not read");
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
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"  {GetColumnRightsCommand.OnWithoutRows}", because: "the output states this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain($"  {GetColumnRightsCommand.SwitchOff}", because: "the output must not state this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainInOrder(new[] {
			"Column permissions for 'Holiday' (UsrHoliday):",
			$"  {GetColumnRightsCommand.OrderRule}",
			"  Column permissions: ON. Columns with rows:",
			"    UsrAmount 'Amount':",
			$"      [0] All employees ({Employees}): read",
			"    UsrDescription 'Description':",
			$"      [0] System administrators ({Admins}): edit",
			$"      [1] All employees ({Employees}): denied"
		}, because: "the output states these lines in this order");
	}

	[Test]
	[Description("A level the service returned that is none of denied/read/edit is shown as the number it is, never as a known level.")]
	public void Execute_ShouldShowUnknownLevel_WhenRowCarriesAnUnknownLevel() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, (ColumnRightLevel)7))));

		// Act
		Run();

		// Assert
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"      [0] All employees ({Employees}): unknown (7)", because: "the output states this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainInOrder(new[] {
			$"  {GetColumnRightsCommand.SwitchOff}",
			"  Rows stored while column permissions are OFF:",
			"    UsrAmount 'Amount':",
			$"      [0] All employees ({Employees}): denied"
		}, because: "the output states these lines in this order");
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
		Lines(nameof(ILogger.WriteError)).Should().Contain("  UsrHoliday: its column permissions could not be read: Access denied.", because: "the error says this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain($"  {GetColumnRightsCommand.SwitchOff}", because: "the output must not state this");
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
		Lines(nameof(ILogger.WriteError)).Should().Contain(line => 
			line.StartsWith("  UsrHoliday: its column permissions could not be read:") && line.Contains("timed out"), because: "the error says this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().Contain("    UsrAmount 'Amount':", because: "the output states this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain("    UsrDescription 'Description':", because: "the output must not state this");
		Lines(nameof(ILogger.WriteInfo)).Should().Contain("Column permissions for 'Holiday' (UsrHoliday) (column UsrAmount):", because: "the output states this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain(line => line.Contains("usramount"), because: "the output must not state this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().Contain("    Name: no column permission rows.", because: "the output states this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain(line => line.Contains("NAME"), because: "the output must not state this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain("    UsrAmount 'Amount':", because: "the output must not state this");
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
		Lines(nameof(ILogger.WriteError)).Should().Contain("Error: 'Holiday' (UsrHoliday) has no column 'UsrMissing'.", because: "the error says this");
	}

	[Test]
	[Description("--column that is not a column code is refused with the reason before anything is read: neither the rights nor the schema.")]
	public void Execute_ShouldRefuse_WhenColumnIsNotACode() {
		// Arrange & Act
		int exitCode = Run(column: "Amount; drop");

		// Assert
		exitCode.Should().Be(1, because: "only a column code is accepted");
		Lines(nameof(ILogger.WriteError)).Should().ContainSingle(line => line == "Error: --column 'Amount; drop' is not a column code (letters, digits and '_' only).", because: "the error says this, once");
		ReceivedCallLog.CallsTo(_rightsReader, nameof(IObjectRightsReader.GetObjectRights)).Should()
			.BeEmpty(because: "the rights are not read");
		ReceivedCallLog.CallsTo(_columnManager, nameof(IRemoteEntitySchemaColumnManager.GetSchemaProperties)).Should()
			.BeEmpty(because: "the schema is not read");
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
		Lines(nameof(ILogger.WriteWarning)).Should().Contain(line =>  line.Contains("could not be read")
			&& line.Contains("schema read failed"), because: "the warning says this");
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"    column {AmountUId} 'Amount' (code not resolved):", because: "the output states this");
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
		Lines(nameof(ILogger.WriteError)).Should().Contain(line =>  line.Contains("--column UsrAmount cannot be found"), because: "the error says this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainInOrder(new[] {
			$"      [1] Sales managers ({Role}): read",
			"      Other rows of this column:",
			$"        [0] System administrators ({Admins}): edit",
			$"        [2] All employees ({Employees}): denied"
		}, because: "the output states these lines in this order");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain(line => line.Contains("above"), because: "the output must not state this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainInOrder(new[] {
			$"      [1] Sales managers ({Role}): read",
			$"      [3] Sales managers ({Role}): denied",
			"      Other rows of this column:",
			$"        [0] System administrators ({Admins}): edit",
			$"        [2] All employees ({Employees}): denied"
		}, because: "the output states these lines in this order");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain($"        [3] Sales managers ({Role}): denied", because: "the output must not state this");
	}

	[Test]
	[Description("--grantee with no row in a column says so and lists every row of that column.")]
	public void Execute_ShouldListEveryRow_WhenGranteeHasNoRowInTheColumn() {
		// Arrange
		Returns(Object(true, Column(AmountUId, "Amount", Row(Employees, "All employees", 0, ColumnRightLevel.Denied))));

		// Act
		Run(grantee: Role.ToString());

		// Assert
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"      grantee {Role} has NO row in this column. Its rows:", because: "the output states this");
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"        [0] All employees ({Employees}): denied", because: "the output states this");
	}

	[Test]
	[Description("--grantee that is not a GUID is refused before anything is read.")]
	public void Execute_ShouldRefuse_WhenGranteeIsNotAGuid() {
		// Arrange & Act
		int exitCode = Run(grantee: "Sales managers");

		// Assert
		exitCode.Should().Be(1, because: "the grantee is a SysAdminUnit id");
		Lines(nameof(ILogger.WriteError)).Should().Contain("Error: --grantee must be a SysAdminUnit id (GUID).", because: "the error says this");
		ReceivedCallLog.CallsTo(_rightsReader, nameof(IObjectRightsReader.GetObjectRights)).Should()
			.BeEmpty(because: "the rights are not read");
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
		Lines(nameof(ILogger.WriteInfo)).Should().Contain("  'Holiday' is not an object code: it is the title of UsrHoliday, which is read.", because: "the output states this");
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
		Lines(nameof(ILogger.WriteError)).Should().Contain(line =>  line.Contains("is the title of 2 objects")
			&& line.EndsWith("Re-run with the exact code."), because: "the error says this");
		ReceivedCallLog.CallsTo(_rightsReader, nameof(IObjectRightsReader.GetObjectRights))
			.Should().NotContain(call => (string)call[0] == "UsrHoliday", because: "no object is guessed between the candidates");
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
		ReceivedCallLog.CallsTo(_columnManager, nameof(IRemoteEntitySchemaColumnManager.GetSchemaProperties))
			.Select(call => (GetEntitySchemaPropertiesOptions)call[0])
			.Should().ContainSingle(read =>
			read.SchemaName == "UsrHoliday" && read.RuntimeReadTimeoutMilliseconds == 30_000 && read.Package == null, because: "the schema is read once, with the call's limits");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainInOrder(new[] {
			$"  {GetColumnRightsCommand.SwitchMissing}",
			"  Stored rows:",
			"    UsrAmount 'Amount':",
			$"      [0] All employees ({Employees}): denied"
		}, because: "the output states these lines in this order");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain(line => line.Contains("OFF") || line.Contains(": ON"), because: "the output must not state this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainSingle(line => line == $"  {GetColumnRightsCommand.SwitchMissing}", because: "the output states this, once");
		Lines(nameof(ILogger.WriteInfo)).Should().ContainSingle(line => line == "  No column has a permission row.", because: "the output states this, once");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain($"  {GetColumnRightsCommand.SwitchOff}", because: "the output must not state this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain($"  {GetColumnRightsCommand.OnWithoutRows}", because: "the output must not state this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainInOrder(new[] {
			$"      [0] System administrators ({Admins}): edit",
			$"      [?] All employees ({Employees}): denied"
		}, because: "the output states these lines in this order");
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
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"      [0] All employees ({Employees}): unknown ({{\"value\":1}})", because: "the output states this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"    column {gone} (not a column of the schema):", because: "the output states this");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain(line => line.Contains("code not resolved"), because: "the output must not state this");
		Lines(nameof(ILogger.WriteWarning)).Should().BeEmpty(because: "no such warning is written");
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
		Lines(nameof(ILogger.WriteInfo)).Should().Contain("    column 'First' (column UId 'not-a-guid' is not a GUID):", because: "the output states this");
		Lines(nameof(ILogger.WriteInfo)).Should().Contain("    column 'Second' (no column UId):", because: "the output states this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainInOrder(new[] {
			"    Name 'Name':",
			"    UsrAmount 'Amount':",
			$"    column {alpha} 'alpha' (not a column of the schema):",
			$"    column {zeta} 'Zeta' (not a column of the schema):"
		}, because: "the output states these lines in this order");
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
		ReceivedCallLog.CallsTo(_columnManager, nameof(IRemoteEntitySchemaColumnManager.GetSchemaProperties)).Should()
			.BeEmpty(because: "the schema is not read");
		Lines(nameof(ILogger.WriteWarning)).Should().ContainSingle(line => line == "  The columns of UsrHoliday could not be read (the call's time limit is spent): "
			+ "columns are shown by their UId and caption.", because: "the warning says this, once");
		Lines(nameof(ILogger.WriteInfo)).Should().Contain($"    column {AmountUId} 'Amount' (code not resolved):", because: "the output states this");
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
		Lines(nameof(ILogger.WriteInfo)).Should().ContainInOrder(new[] {
			"Column permissions for 'Holiday' (UsrHoliday) (column UsrAmount):",
			$"  {GetColumnRightsCommand.SwitchOff}",
			"  Rows stored while column permissions are OFF:",
			"    UsrAmount 'Amount':",
			$"      [0] All employees ({Employees}): read"
		}, because: "the output states these lines in this order");
		Lines(nameof(ILogger.WriteInfo)).Should().NotContain("    UsrDescription 'Description':", because: "the output must not state this");
	}

	private static readonly Guid SchemaUId = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");

	// What the service lists for the stored rows while the switch is on: every non-default column with its rows, every
	// schema column with no stored row with none; a default-only column is omitted. Nothing while it is off.
	private static ColumnRights[] ServiceView(bool? on, ColumnRights[] stored) {
		if (on != true) {
			return [];
		}
		HashSet<Guid?> withRows = stored.Select(column => column.ColumnUId).ToHashSet();
		return stored.Where(column => !ColumnRightsDefaults.IsDefaultOnly(column.Rows))
			.Concat(new[] { DescriptionUId, AmountUId, NameUId }.Where(uId => !withRows.Contains(uId))
				.Select(uId => new ColumnRights(uId, null, [])))
			.ToArray();
	}

	// An object whose schema UId was read, so its stored rows are read from storage; the service lists them as it does.
	private void ReturnsWithStorage(bool? on, params ColumnRights[] stored) =>
		ReturnsWithStorage(on, ServiceView(on, stored), stored);

	private void ReturnsWithStorage(bool? on, ColumnRights[] listed, ColumnRights[] stored) {
		_rightsReader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsInfo(true,
			"UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: on, Columns: listed));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns(stored);
	}

	private List<string> Lines(string method) =>
		ReceivedCallLog.CallsTo(_logger, method).Select(call => (string)call[0]).ToList();

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
		List<string> infos = Lines(nameof(ILogger.WriteInfo));
		infos.Should().Contain("    UsrAmount:", because: "a restricted column is listed by its code");
		infos.Should().Contain($"      [0] Role ({Role}): denied", because: "with its rows");
		infos.Should().Contain($"  {GetColumnRightsCommand.DefaultOnly} (1): Name.", because: "a default column is summarised");
		infos.Should().Contain($"  {GetColumnRightsCommand.NoRows} (1): UsrDescription.",
			because: "a schema column with no stored row is reported as locked");
		infos.Should().NotContain("    Name:", because: "a default column is never shown as a restriction");
		GetColumnRightsCommand.NoRows.Should().Contain("every user follows the object's operation permissions")
			.And.NotContain("system administrators",
				because: "per Creatio's documentation users with no row in a column follow the object's operation permissions");
	}

	[Test]
	[Description("The stored rows are read with their count verified, so a read the server cut short is never shown as complete.")]
	public void Execute_ShouldCountTheStoredRows_WhenReadingStorage() {
		// Arrange
		ReturnsWithStorage(true, Column(AmountUId, null, Row(Role, "Role", 0, ColumnRightLevel.Denied)));

		// Act
		Run();

		// Assert
		ReceivedCallLog.CallsTo(_storage, nameof(IColumnRightsStorageReader.ReadStoredColumns))
			.Select(call => (bool)call[2]).Should().Equal(new[] { true }, because: "the read counts its rows first");
	}

	[Test]
	[Description("While ON, storage that disagrees with what the service lists is not shown as the truth: a warning names the disagreement and the service's view is shown.")]
	public void Execute_ShouldShowTheServiceView_WhenStorageDisagreesWhileOn() {
		// Arrange
		ReturnsWithStorage(true,
			[Column(AmountUId, "Amount", Row(Role, "Role", 0, ColumnRightLevel.Denied)), Column(DescriptionUId, "Description")],
			[Column(NameUId, null, Row(Role, "Role", 0, ColumnRightLevel.Read))]);

		// Act
		int exitCode = Run();

		// Assert
		exitCode.Should().Be(0, because: "the service read still answers");
		Lines(nameof(ILogger.WriteWarning)).Should().ContainSingle(line => line.Contains("disagree with what the service lists")
			&& line.Contains(NameUId.ToString()), because: "the warning names the disagreeing column");
		List<string> infos = Lines(nameof(ILogger.WriteInfo));
		infos.Should().Contain("    UsrAmount 'Amount':", because: "the service's view is shown");
		infos.Should().NotContain("    Name:", because: "the disagreeing storage is not shown as the truth");
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
		List<string> infos = Lines(nameof(ILogger.WriteInfo));
		infos.Should().Contain($"  {GetColumnRightsCommand.SwitchOff}", because: "OFF is said first");
		infos.Should().Contain("  Rows stored while column permissions are OFF:", because: "stored rows are not applied");
		infos.Should().Contain($"      [0] Role ({Role}): denied", because: "the stored restriction is listed");
		infos.Should().Contain($"  {GetColumnRightsCommand.DefaultOnly} (1): Name.", because: "a default column is summarised");
		infos.Should().NotContain("    Name:", because: "a default column is never listed as a restriction");
	}

	[Test]
	[Description("--column naming a column with no stored row while ON reports it as one only system administrators can access, under a plain ON line — never the object-wide 'no rows other than the default' line.")]
	public void Execute_ShouldReportTheNamedColumnAsLocked_WhenItHasNoStoredRowWhileOn() {
		// Arrange
		ReturnsWithStorage(true, Column(AmountUId, null, Row(Role, "Role", 0, ColumnRightLevel.Denied)));

		// Act
		int exitCode = Run(column: "usrdescription");

		// Assert
		exitCode.Should().Be(0, because: "the column exists");
		List<string> infos = Lines(nameof(ILogger.WriteInfo));
		infos.Should().Contain($"  {GetColumnRightsCommand.NoRows} (1): UsrDescription.",
			because: "the named column has no stored row");
		infos.Should().NotContain("    UsrAmount:", because: "only the named column is shown");
		infos.Should().Contain("  Column permissions: ON.", because: "the switch line names no object-wide fact");
		infos.Should().NotContain($"  {GetColumnRightsCommand.OnWithoutRows}",
			because: "another column of the object has rows");
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
		Lines(nameof(ILogger.WriteWarning)).Should().ContainSingle(line => line.Contains("Access denied")
			&& line.Contains("not shown at all"), because: "the warning says what the service read leaves out");
		List<string> infos = Lines(nameof(ILogger.WriteInfo));
		infos.Should().Contain("    UsrAmount 'Amount':", because: "the service's restricted column is shown");
		infos.Should().Contain($"  {GetColumnRightsCommand.NoRows} (1): UsrDescription.",
			because: "a column the service lists with no rows is locked");
	}
}
