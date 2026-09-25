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

	private static readonly Guid ExternalUsers = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");
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
		_connectedObjects.Resolve(Arg.Any<string>(), Arg.Any<bool>())
			.Returns(callInfo => Resolution((string)callInfo[0]));
		containerBuilder.AddTransient(_ => _rightsReader);
		containerBuilder.AddTransient(_ => _connectedObjects);
		containerBuilder.AddTransient(_ => _logger);
	}

	private static ConnectedObjectsResolution Resolution(params string[] objects) =>
		new(objects, Array.Empty<string>());

	private static ObjectRightsInfo Administered(string name, params RoleOperationRights[] roles) =>
		new(true, name, name, true, roles);

	private static RoleOperationRights Ext(bool read, bool create, bool edit, bool del) =>
		new(ExternalUsers, "All external users", read, create, edit, del);

	[Test]
	[Description("With a grantee filter, reports no missing objects when the grantee can read the root and every connected object.")]
	public void Execute_ShouldReportNoMissing_WhenGranteeGrantedEverywhere() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike2", true)
			.Returns(Resolution("UsrPortalSpike2", "UsrPSCategory"));
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPortalSpike2", Ext(true, true, true, false)));
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPSCategory", Ext(true, true, true, false)));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike2", Grantee = ExternalUsers.ToString(), IncludeConnected = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("can read every listed object")));
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("cannot read")));
	}

	[Test]
	[Description("With a grantee filter, lists a connected object where the grantee has no grant.")]
	public void Execute_ShouldListMissing_WhenGranteeLacksOnConnected() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike2", true)
			.Returns(Resolution("UsrPortalSpike2", "UsrPSCategory"));
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPortalSpike2", Ext(true, true, true, false)));
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPSCategory", new RoleOperationRights(Employees, "All employees", true, true, true, true)));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike2", Grantee = ExternalUsers.ToString(), IncludeConnected = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a read that finds gaps still completes");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("cannot read") && m.Contains("UsrPSCategory")));
	}

	[Test]
	[Description("Without a grantee filter, reports every role's operations on the object.")]
	public void Execute_ShouldReportAllRoles_WhenNoGrantee() {
		// Arrange
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPortalSpike2",
				Ext(true, true, true, false),
				new RoleOperationRights(Employees, "All employees", true, true, true, true)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike2" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("All external users")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("All employees")));
	}

	[Test]
	[Description("Reports an object that is not administered by operation permissions as available to all.")]
	public void Execute_ShouldReportAvailable_WhenNotAdministratedByOperations() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOpen", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOpen", "Open", false, Array.Empty<RoleOperationRights>()));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOpen" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("available to all")));
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
	[Description("A read failure on the ROOT object fails the check with exit code 1: the named object was not verified.")]
	public void Execute_ShouldReturnError_WhenRootReadFails() {
		// Arrange
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPortalSpike2", null, false, Array.Empty<RoleOperationRights>(),
				ReadError: "Request Error"));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike2" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the object the caller named could not be read, so the check did not happen");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("could not read object rights")));
	}

	private GetObjectRightsOptions GranteeCheckWithConnected() {
		_connectedObjects.Resolve("UsrPortalSpike2", true)
			.Returns(Resolution("UsrPortalSpike2", "UsrPSCategory"));
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPortalSpike2", Ext(true, true, true, false)));
		return new GetObjectRightsOptions {
			EntitySchemaName = "UsrPortalSpike2", Grantee = ExternalUsers.ToString(), IncludeConnected = true
		};
	}

	[Test]
	[Description("With a grantee filter, a connected object that is NOT administered by operation permissions is listed as lacking access — never counted as covered — because external users are deny-by-default.")]
	public void Execute_ShouldListNonAdministeredObjectAsMissing_WhenGranteeFilterSet() {
		// Arrange
		GetObjectRightsOptions options = GranteeCheckWithConnected();
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPSCategory", "UsrPSCategory", false, Array.Empty<RoleOperationRights>()));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("can read every listed object")));
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("cannot read") && m.Contains("UsrPSCategory")));
	}

	[Test]
	[Description("With a grantee filter, a connected object whose rights could not be read never yields the unqualified all-clear line.")]
	public void Execute_ShouldNotPrintAllClear_WhenConnectedObjectReadFails() {
		// Arrange
		GetObjectRightsOptions options = GranteeCheckWithConnected();
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPSCategory", null, false, Array.Empty<RoleOperationRights>(),
				ReadError: "denied"));

		// Act
		_command.Execute(options);

		// Assert
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("can read every listed object")));
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("could not be read")));
	}

	[Test]
	[Description("With a grantee filter, a connected object that is not found never yields the unqualified all-clear line.")]
	public void Execute_ShouldNotPrintAllClear_WhenConnectedObjectNotFound() {
		// Arrange
		GetObjectRightsOptions options = GranteeCheckWithConnected();
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "UsrPSCategory", null, false, Array.Empty<RoleOperationRights>()));

		// Act
		_command.Execute(options);

		// Assert
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("can read every listed object")));
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("could not be read")));
	}

	[Test]
	[Description("A root object that is not found fails with exit code 1, the same answer set-object-rights gives.")]
	public void Execute_ShouldReturnError_WhenRootSchemaNotFound() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrders", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(false, "UsrOrders", null, false, Array.Empty<RoleOperationRights>()));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrders", Grantee = ExternalUsers.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing root object means nothing was checked");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrOrders") && m.Contains("not found")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("can read")));
	}

	[Test]
	[Description("When every read fails, no coverage sentence is printed at all and the check fails: nothing was verified.")]
	public void Execute_ShouldReturnError_WhenEveryReadFails() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike2", true).Returns(Resolution("UsrPortalSpike2", "UsrPSCategory"));
		_rightsReader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "x", null, false, Array.Empty<RoleOperationRights>(), ReadError: "denied"));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike2", Grantee = ExternalUsers.ToString(), IncludeConnected = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the root object could not be read");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("no object could be read")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("can read")));
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("can read the")));
	}

	[Test]
	[Description("After the default portal grant (root read/create/edit, connected READ only) the check reports no missing connected object, so it never pushes a caller to widen shared lookups to write access.")]
	public void Execute_ShouldNotListReadOnlyConnected_AfterDefaultConnectedGrant() {
		// Arrange
		GetObjectRightsOptions options = GranteeCheckWithConnected();
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPSCategory", Ext(true, false, false, false)));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("can read every listed object")));
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("cannot read")));
	}

	[Test]
	[Description("A grantee row without READ is listed as missing even when it holds other operations.")]
	public void Execute_ShouldListMissing_WhenGranteeRowLacksRead() {
		// Arrange
		GetObjectRightsOptions options = GranteeCheckWithConnected();
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPSCategory", Ext(false, true, true, false)));

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("cannot read") && m.Contains("UsrPSCategory")));
	}

	[Test]
	[Description("A failed connected-object enumeration is reported as unverified and never yields an all-clear for the root alone.")]
	public void Execute_ShouldNotPrintAllClear_WhenConnectedEnumerationFails() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike2", true)
			.Returns(new ConnectedObjectsResolution(new[] { "UsrPortalSpike2" }, Array.Empty<string>(), "schema read failed"));
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPortalSpike2", Ext(true, true, true, false)));
		GetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike2", Grantee = ExternalUsers.ToString(), IncludeConnected = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the root object itself was read");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("UNVERIFIED") && m.Contains("schema read failed")));
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("could not be enumerated")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("can read every listed object")));
	}

	[Test]
	[Description("A security/system lookup excluded from the fan-out is named in a warning.")]
	public void Execute_ShouldWarn_WhenConnectedObjectExcluded() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike2", true)
			.Returns(new ConnectedObjectsResolution(new[] { "UsrPortalSpike2" }, new[] { "SysAdminUnit" }));
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrPortalSpike2", Ext(true, true, true, false)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike2", IncludeConnected = true };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("SysAdminUnit") && m.Contains("security/system object")));
	}
}
