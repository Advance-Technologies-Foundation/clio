using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The <c>get-column-rights</c> MCP tool: its safety hints, argument binding and mapping, the refusal of unknown
/// argument names before any read, and the structured response of a run.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ColumnRightsToolTests {

	private const string Grantee = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";
	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid AmountUId = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid SchemaUId = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");

	private ILogger _logger;
	private IObjectRightsReader _reader;
	private IColumnRightsStorageReader _storage;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private IToolCommandResolver _resolver;
	private GetColumnRightsOptions _captured;

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		// BaseTool snapshots the tool logger's captured messages after the run and clears them; the substitute
		// captures what the command writes so the response carries it, as the real logger does.
		List<LogMessage> messages = new();
		_logger.LogMessages.Returns(_ => messages.ToList());
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => messages.Add(new InfoMessage((string)call[0])));
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => messages.Add(new WarningMessage((string)call[0])));
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => messages.Add(new ErrorMessage((string)call[0])));
		_logger.When(l => l.ClearMessages()).Do(_ => messages.Clear());
		_reader = Substitute.For<IObjectRightsReader>();
		_storage = Substitute.For<IColumnRightsStorageReader>();
		_storage.ReadStoredColumns(default, default, default).ReturnsForAnyArgs(Array.Empty<ColumnRights>());
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			new EntitySchemaPropertiesInfo("UsrHoliday", "Holiday", null, "(merged: all packages)", null, false, "Id",
				"Name", 1, 0, null, false, false, null, false, null, false, false, true, false, null, null,
				new[] { new EntitySchemaPropertyColumnInfo("UsrAmount", AmountUId, "own", "Amount", null, "Decimal",
					false, false, null) }));
		_resolver = Substitute.For<IToolCommandResolver>();
		_captured = null;
		_resolver.Resolve<GetColumnRightsCommand>(Arg.Do<EnvironmentOptions>(o => _captured = (GetColumnRightsOptions)o))
			.Returns(_ => Command());
	}

	private GetColumnRightsCommand Command() => new(_reader, _storage, _columnManager, _logger);

	private GetColumnRightsTool Tool() => new(Command(), _logger, _resolver);

	private static T Bind<T>(string json) =>
		JsonSerializer.Deserialize<T>(json, Clio.BindingsModule.CreateMcpSerializerOptions())!;

	[Test]
	[Description("Declares get-column-rights read-only, non-destructive, idempotent and closed-world under its canonical name.")]
	public void GetColumnRightsTool_ShouldDeclareReadOnlySafetyFlags_WhenInspectingAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GetColumnRightsTool)
			.GetMethod(nameof(GetColumnRightsTool.GetColumnRights))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be("get-column-rights", because: "the tool publishes under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeTrue(because: "reading column permissions changes nothing");
		attribute.Destructive.Should().BeFalse(because: "a read is never destructive");
		attribute.Idempotent.Should().BeTrue(because: "a read may be repeated");
		attribute.OpenWorld.Should().BeFalse(because: "the tool talks only to the registered environment");
	}

	[Test]
	[Description("Marks the single args wrapper as schema-required so an omitted args object fails with a structured error.")]
	public void GetColumnRightsTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] required = typeof(GetColumnRightsTool)
			.GetMethod(nameof(GetColumnRightsTool.GetColumnRights))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		required.Should().NotBeEmpty(because: "the args wrapper must be schema-required");
	}

	[Test]
	[Description("Binds every get-column-rights argument from kebab-case JSON and maps it onto the command options, with the read limits of get-object-rights.")]
	public void GetColumnRightsArgs_ShouldBindAndMapEveryArgument_WhenDeserializedWithMcpOptions() {
		// Arrange
		GetColumnRightsArgs args = Bind<GetColumnRightsArgs>(
			$$"""{"environment-name":"dev","entity-schema-name":"UsrHoliday","column":"UsrAmount","grantee":"{{Grantee}}"}""");

		// Act
		GetColumnRightsOptions options = GetColumnRightsTool.BuildOptions(args);

		// Assert
		args.ExtensionData.Should().BeNullOrEmpty(because: "every key is a known argument");
		options.Environment.Should().Be("dev", because: "environment-name maps to the environment");
		options.EntitySchemaName.Should().Be("UsrHoliday", because: "entity-schema-name maps through");
		options.Column.Should().Be("UsrAmount", because: "column maps to --column");
		options.Grantee.Should().Be(Grantee, because: "grantee maps to --grantee");
		options.TimeOut.Should().Be(GetObjectRightsTool.McpReadTimeoutMilliseconds,
			because: "each request gets at most 30 s, as on get-object-rights");
		options.MaxAttempts.Should().Be(1, because: "a hang is not retried inside the read deadline");
		options.ReadBudget.Should().Be(GetObjectRightsTool.McpReadBudget,
			because: "all requests share the 90 s limit of get-object-rights");
	}

	[TestCase("column-name")]
	[TestCase("grantee-id")]
	[TestCase("include-connected")]
	[Description("A misspelled or foreign argument is refused before any read, so the read never silently answers for every column or every role.")]
	public void GetColumnRights_ShouldRefuseBeforeAnyRead_WhenAnArgumentIsUnknown(string unknown) {
		// Arrange
		GetColumnRightsArgs args = Bind<GetColumnRightsArgs>(
			$$"""{"environment-name":"dev","entity-schema-name":"UsrHoliday","{{unknown}}":"x"}""");

		// Act
		ObjectRightsToolResponse response = Tool().GetColumnRights(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown argument name must be refused, not silently dropped");
		response.Error.Should().Contain(unknown, because: "the refusal names the offending key");
		_resolver.DidNotReceive().Resolve<GetColumnRightsCommand>(Arg.Any<EnvironmentOptions>());
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}

	[Test]
	[Description("A successful read runs the command resolved for the call's environment and returns its output.")]
	public void GetColumnRights_ShouldReturnTheOutput_WhenTheReadSucceeds() {
		// Arrange
		ColumnRights[] columns = { new(AmountUId, "Amount",
			new[] { new ColumnRightRow(Employees, "All employees", 0, ColumnRightLevel.Denied) }) };
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsInfo(true,
			"UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: true, Columns: columns));
		_storage.ReadStoredColumns(SchemaUId, Arg.Any<CreatioRequestOptions>(), Arg.Any<bool>()).Returns(columns);

		// Act
		ObjectRightsToolResponse response = Tool().GetColumnRights(new GetColumnRightsArgs("dev", "UsrHoliday"));

		// Assert
		response.Success.Should().BeTrue(because: "the read completed");
		_captured.Should().NotBeNull(because: "the command is resolved for the call's environment");
		_captured!.Environment.Should().Be("dev", because: "the environment travels with the call");
		response.Output.Should().Contain("UsrAmount 'Amount':", because: "the column is named by its code and caption");
		response.Output.Should().Contain($"[0] All employees ({Employees}): denied",
			because: "each row carries its position, grantee and level");
	}

	[Test]
	[Description("A failed service read is a failed call (success=false), never a read that says column permissions are OFF.")]
	public void GetColumnRights_ShouldFail_WhenTheReadFails() {
		// Arrange
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>())
			.Returns(ObjectRightsInfo.ReadFailed("UsrHoliday", "Access denied"));

		// Act
		ObjectRightsToolResponse response = Tool().GetColumnRights(new GetColumnRightsArgs("dev", "UsrHoliday"));

		// Assert
		response.Success.Should().BeFalse(because: "a read that did not happen is a failure");
		response.Error.Should().Contain("Access denied", because: "the failure carries the service's reason");
		response.Error.Should().NotContain("OFF", because: "a failed read is never reported as OFF");
	}
}

/// <summary>
/// The <c>set-column-rights</c> MCP tool: its safety hints, argument binding and mapping (no package argument), the
/// refusal of unknown argument names before any read or write, and a preview that writes nothing.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SetColumnRightsToolTests {

	private const string Grantee = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";
	private static readonly Guid AmountUId = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid SchemaUId = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");

	private ILogger _logger;
	private IObjectRightsReader _reader;
	private IColumnRightsStorageReader _storage;
	private IColumnRightsWriter _writer;
	private IGranteeLookup _granteeLookup;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private IToolCommandResolver _resolver;
	private SetColumnRightsOptions _captured;

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		List<LogMessage> messages = new();
		_logger.LogMessages.Returns(_ => messages.ToList());
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => messages.Add(new InfoMessage((string)call[0])));
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => messages.Add(new WarningMessage((string)call[0])));
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => messages.Add(new ErrorMessage((string)call[0])));
		_logger.When(l => l.ClearMessages()).Do(_ => messages.Clear());
		_reader = Substitute.For<IObjectRightsReader>();
		_storage = Substitute.For<IColumnRightsStorageReader>();
		_storage.ReadStoredColumns(default, default, default).ReturnsForAnyArgs(Array.Empty<ColumnRights>());
		_reader.GetObjectRights("UsrHoliday", Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsInfo(true,
			"UsrHoliday", "Holiday", false, Array.Empty<RoleOperationRights>(), SchemaUId: SchemaUId,
			AdministratedByColumns: true, Columns: Array.Empty<ColumnRights>()));
		_writer = Substitute.For<IColumnRightsWriter>();
		_granteeLookup = Substitute.For<IGranteeLookup>();
		_granteeLookup.ResolveGranteeName(Guid.Parse(Grantee), Arg.Any<CreatioRequestOptions>()).Returns("Role");
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			new EntitySchemaPropertiesInfo("UsrHoliday", "Holiday", null, "(merged: all packages)", null, false, "Id",
				"Name", 1, 0, null, false, false, null, false, null, false, false, true, false, null, null,
				new[] { new EntitySchemaPropertyColumnInfo("UsrAmount", AmountUId, "own", "Amount", null, "Decimal",
					false, false, null) }));
		_resolver = Substitute.For<IToolCommandResolver>();
		_captured = null;
		_resolver.Resolve<SetColumnRightsCommand>(Arg.Do<EnvironmentOptions>(o => _captured = (SetColumnRightsOptions)o))
			.Returns(_ => Command());
	}

	private SetColumnRightsCommand Command() => new(_reader, _storage, _writer, new ColumnRightsPlanner(),
		new ColumnRightsReadBackVerifier(), Substitute.For<IColumnRightsWriteLease>(), _granteeLookup, _columnManager,
		Substitute.For<IInteractiveConsole>(), _logger);

	private SetColumnRightsTool Tool() => new(Command(), _logger, _resolver);

	private static T Bind<T>(string json) =>
		JsonSerializer.Deserialize<T>(json, Clio.BindingsModule.CreateMcpSerializerOptions())!;

	[Test]
	[Description("Declares set-column-rights destructive, not read-only, idempotent and closed-world under its canonical name: a grant can take access away, and a repeated identical call finds its change in place.")]
	public void SetColumnRightsTool_ShouldDeclareDestructiveSafetyFlags_WhenInspectingAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SetColumnRightsTool)
			.GetMethod(nameof(SetColumnRightsTool.SetColumnRights))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be("set-column-rights", because: "the tool publishes under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "the tool writes column permissions");
		attribute.Destructive.Should().BeTrue(because: "a deny or a removal takes access away");
		attribute.Idempotent.Should().BeTrue(because: "repeating the same call changes nothing further");
		attribute.OpenWorld.Should().BeFalse(because: "the tool talks only to the registered environment");
	}

	[Test]
	[Description("The set-column-rights description tells a host approving an enable that every column with no rows gets the default row and is named, and that calls for one object must not run in parallel.")]
	public void SetColumnRightsTool_ShouldStateEnableDefaultsAndNoParallelCalls_WhenInspectingDescription() {
		// Arrange & Act
		string description = ((System.ComponentModel.DescriptionAttribute)typeof(SetColumnRightsTool)
			.GetMethod(nameof(SetColumnRightsTool.SetColumnRights))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Single()).Description;

		// Assert
		description.Should().Contain("gives EVERY column with no stored row the default row (All employees: edit), naming those columns",
			because: "a host approving an enable must see that columns with no rows get the default");
		description.Should().Contain("must NOT run in parallel", because: "overlapping calls on one object are refused");
	}

	[Test]
	[Description("Marks the single args wrapper as schema-required so an omitted args object fails with a structured error.")]
	public void SetColumnRightsTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] required = typeof(SetColumnRightsTool)
			.GetMethod(nameof(SetColumnRightsTool.SetColumnRights))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		required.Should().NotBeEmpty(because: "the args wrapper must be schema-required");
	}

	[Test]
	[Description("Binds every set-column-rights argument from kebab-case JSON and maps it onto a confirmed command call with the request limits of set-object-rights.")]
	public void SetColumnRightsArgs_ShouldBindAndMapEveryArgument_WhenDeserializedWithMcpOptions() {
		// Arrange
		SetColumnRightsArgs args = Bind<SetColumnRightsArgs>(
			$$"""{"environment-name":"dev","entity-schema-name":"UsrHoliday","column":"UsrAmount","grantee":"{{Grantee}}","access":"read","position":0,"remove-row":false,"enable-column-permissions":true,"disable-column-permissions":false}""");

		// Act
		SetColumnRightsOptions options = SetColumnRightsTool.BuildOptions(args);

		// Assert
		args.ExtensionData.Should().BeNullOrEmpty(because: "every key is a known argument");
		options.Environment.Should().Be("dev", because: "environment-name maps to the environment");
		options.Column.Should().Be("UsrAmount", because: "column maps to --column");
		options.Grantee.Should().Be(Grantee, because: "grantee maps to --grantee");
		options.Access.Should().Be("read", because: "access maps to --access");
		options.Position.Should().Be(0, because: "position maps to --position");
		options.EnableColumnPermissions.Should().BeTrue(because: "the enable flag maps through");
		options.Confirm.Should().BeTrue(because: "the host's approval of the call is the confirmation");
		options.TimeOut.Should().Be(SetObjectRightsTool.McpRequestTimeoutMilliseconds,
			because: "each request gets the set-object-rights limit");
		options.MaxAttempts.Should().Be(1, because: "a write is not retried");
		options.CallBudget.Should().Be(SetObjectRightsTool.McpCallBudget, because: "all requests share one limit");
		SetColumnRightsTool.ValidArguments.Should().NotContain("package",
			because: "column rights are not bound to a package: the delivery path is not established");
	}

	[TestCase("package")]
	[TestCase("remove_row")]
	[TestCase("operations")]
	[Description("An unknown argument — package included, which this tool does not have — is refused before any read or write, so a typo never turns into a different change.")]
	public void SetColumnRights_ShouldRefuseBeforeAnyReadOrWrite_WhenAnArgumentIsUnknown(string unknown) {
		// Arrange
		SetColumnRightsArgs args = Bind<SetColumnRightsArgs>(
			$$"""{"environment-name":"dev","entity-schema-name":"UsrHoliday","column":"UsrAmount","grantee":"{{Grantee}}","access":"read","{{unknown}}":"x"}""");

		// Act
		ObjectRightsToolResponse response = Tool().SetColumnRights(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown argument name must be refused, not silently dropped");
		response.Error.Should().Contain(unknown, because: "the refusal names the offending key");
		_resolver.DidNotReceive().Resolve<SetColumnRightsCommand>(Arg.Any<EnvironmentOptions>());
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
	}

	[Test]
	[Description("preview=true is a dry run: never confirmed, nothing saved, and the plan comes back in the output.")]
	public void SetColumnRights_ShouldWriteNothing_WhenPreviewIsTrue() {
		// Arrange
		SetColumnRightsArgs args = new("dev", "UsrHoliday", "UsrAmount", Grantee, "denied", Preview: true);

		// Act
		ObjectRightsToolResponse response = Tool().SetColumnRights(args);

		// Assert
		response.Success.Should().BeTrue(because: "a preview of a valid change succeeds");
		_captured!.Confirm.Should().BeFalse(because: "a preview is never confirmed");
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default, default);
		response.Output.Should().Contain("PREVIEW", because: "the output says nothing was changed");
	}
}
