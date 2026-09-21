using System;
using System.Collections.Generic;
using System.Linq;
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
public class SetObjectRightsCommandTests : BaseCommandTests<SetObjectRightsOptions> {

	private const string Grantee = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";

	private SetObjectRightsCommand _command;
	private IObjectRightsWriter _rightsWriter;
	private IConnectedObjectsResolver _connectedObjects;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetObjectRightsCommand>();
	}

	public override void TearDown() {
		_rightsWriter.ClearReceivedCalls();
		_connectedObjects.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_rightsWriter = Substitute.For<IObjectRightsWriter>();
		_connectedObjects = Substitute.For<IConnectedObjectsResolver>();
		_logger = Substitute.For<ILogger>();
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(true, true));
		// Default: no fan-out — the resolver returns just the root object.
		_connectedObjects.Resolve(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>())
			.Returns(callInfo => new[] { (string)callInfo[0] });
		containerBuilder.AddTransient(_ => _rightsWriter);
		containerBuilder.AddTransient(_ => _connectedObjects);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Grants the parsed operations for the grantee on the root object and returns 0 when --confirm is set.")]
	public void Execute_ShouldGrantParsedOperations_WhenConfirmSet() {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Operations = "read,edit", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful grant returns exit code 0");
		_rightsWriter.Received(1).SetObjectRights(
			"UsrPortalSpike",
			Arg.Is<Guid>(g => g == Guid.Parse(Grantee)),
			Arg.Is<IReadOnlyCollection<ObjectOperation>>(ops =>
				ops.Count == 2 && ops.Contains(ObjectOperation.Read) && ops.Contains(ObjectOperation.Edit)),
			false,
			Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Passes revoke through to the writer when --revoke is set.")]
	public void Execute_ShouldRevoke_WhenRevokeSet() {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Operations = "delete", Revoke = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful revoke returns exit code 0");
		_rightsWriter.Received(1).SetObjectRights("UsrPortalSpike", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), true, Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Applies the change to every object the resolver returns when --include-connected is set.")]
	public void Execute_ShouldFanOutToConnected_WhenIncludeConnectedSet() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike", true, Arg.Any<string>())
			.Returns(new[] { "UsrPortalSpike", "UsrPSCategory" });
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "granting root and its connected objects succeeds");
		_rightsWriter.Received(1).SetObjectRights("UsrPortalSpike", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), false, Arg.Any<CreatioRequestOptions>());
		_rightsWriter.Received(1).SetObjectRights("UsrPSCategory", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), false, Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns a friendly error and calls nothing when --entity-schema-name is empty.")]
	public void Execute_ShouldReturnError_WhenEntitySchemaNameMissing() {
		// Arrange
		SetObjectRightsOptions options = new() { EntitySchemaName = "  ", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing entity schema name is an input error");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("entity-schema-name")));
		_rightsWriter.DidNotReceive().SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns a friendly error when --grantee is not a GUID.")]
	public void Execute_ShouldReturnError_WhenGranteeInvalid() {
		// Arrange
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Grantee = "All external users", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "grantee must be a SysAdminUnit id");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("grantee")));
	}

	[Test]
	[Description("Returns a friendly error when an operation token is unknown.")]
	public void Execute_ShouldReturnError_WhenOperationUnknown() {
		// Arrange
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Operations = "read,frobnicate", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unknown operation is an input error");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("frobnicate")));
	}

	[Test]
	[Description("Refuses to apply the destructive change in a non-interactive run when --confirm is absent.")]
	public void Execute_ShouldRefuse_WhenNonInteractiveAndConfirmAbsent() {
		// Arrange
		Assume.That(Console.IsInputRedirected, Is.True, "the confirm-gate refuse path requires redirected stdin");
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Confirm = false };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a destructive change without --confirm is refused in a non-interactive run");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("--confirm")));
		_rightsWriter.DidNotReceive().SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns exit code 1 and logs the error when the writer reports a failure.")]
	public void Execute_ShouldReturnError_WhenWriterFails() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(true, false, "boom"));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a writer failure returns exit code 1");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("boom")));
	}
}
