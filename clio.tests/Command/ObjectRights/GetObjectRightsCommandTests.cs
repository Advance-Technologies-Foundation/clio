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
		_connectedObjects.Resolve(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>())
			.Returns(callInfo => new[] { (string)callInfo[0] });
		containerBuilder.AddTransient(_ => _rightsReader);
		containerBuilder.AddTransient(_ => _connectedObjects);
		containerBuilder.AddTransient(_ => _logger);
	}

	private static ObjectRightsInfo Administered(string name, params RoleOperationRights[] roles) =>
		new(true, name, name, true, roles);

	private static RoleOperationRights Ext(bool read, bool create, bool edit, bool del) =>
		new(ExternalUsers, "All external users", read, create, edit, del);

	[Test]
	[Description("With a grantee filter, reports no missing objects when the grantee has read/create/edit on the root and every connected object.")]
	public void Execute_ShouldReportNoMissing_WhenGranteeGrantedEverywhere() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike2", true, Arg.Any<string>())
			.Returns(new[] { "UsrPortalSpike2", "UsrPSCategory" });
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
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("already has read/create/edit")));
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("lacks")));
	}

	[Test]
	[Description("With a grantee filter, lists a connected object where the grantee has no grant.")]
	public void Execute_ShouldListMissing_WhenGranteeLacksOnConnected() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike2", true, Arg.Any<string>())
			.Returns(new[] { "UsrPortalSpike2", "UsrPSCategory" });
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
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("lacks") && m.Contains("UsrPSCategory")));
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
	[Description("Skips an object with a warning when reading its rights fails, without aborting.")]
	public void Execute_ShouldSkip_WhenReadError() {
		// Arrange
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPortalSpike2", null, false, Array.Empty<RoleOperationRights>(),
				ReadError: "Request Error"));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike2" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a per-object read failure does not abort the read");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("could not read object rights")));
	}
}
