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

	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private static RoleOperationRights RoleRow(bool read, bool create, bool edit, bool del) =>
		new(Role, "Sales managers", read, create, edit, del);

	private static ObjectRightsInfo NotAdministered(string name) =>
		new(true, name, name, false, Array.Empty<RoleOperationRights>());

	[Test]
	[Description("Without a grantee filter, reports every role's operations on the object.")]
	public void Execute_ShouldReportAllRoles_WhenNoGrantee() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder",
				RoleRow(true, true, true, false),
				new RoleOperationRights(Employees, "All employees", true, true, true, true)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("Sales managers") && m.Contains("read/create/edit")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("All employees") && m.Contains("read/create/edit/delete")));
	}

	[Test]
	[Description("With a grantee filter, reports exactly the operations that role holds on each object — facts only, no coverage verdict.")]
	public void Execute_ShouldReportGranteeOperationsPerObject_WithoutVerdict() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", RoleRow(true, true, true, false)));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrStatus", RoleRow(true, false, false, false)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Role.ToString(), IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("UsrOrder") && m.Contains("read/create/edit")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("UsrStatus") && m.EndsWith(": read.")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("can read") || m.Contains("cannot read")));
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("can read") || m.Contains("cannot read")));
	}

	[Test]
	[Description("With a grantee filter, an administered object where the role has no row is reported as having no operations granted.")]
	public void Execute_ShouldReportNoGrant_WhenGranteeHasNoRow() {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", new RoleOperationRights(Employees, "All employees", true, true, true, true)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Role.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("UsrOrder") && m.Contains("NO object operations granted")));
	}

	[TestCase(null)]
	[TestCase("11111111-2222-3333-4444-555555555555")]
	[Description("A non-administered object is reported with the general platform fact: open to internal users, reachable by external users only through an explicit grant — with or without a grantee.")]
	public void Execute_ShouldReportNotAdministeredFact(string grantee) {
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
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus"));
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
		_connectedObjects.Resolve("UsrOrder", true)
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
		_connectedObjects.Resolve("UsrOrder", true)
			.Returns(new ConnectedObjectsResolution(new[] { "UsrOrder" }, new[] { "SysAdminUnit" }));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(Administered("UsrOrder", RoleRow(true, true, true, false)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", IncludeConnected = true };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("SysAdminUnit") && m.Contains("security/system object")));
	}
}
