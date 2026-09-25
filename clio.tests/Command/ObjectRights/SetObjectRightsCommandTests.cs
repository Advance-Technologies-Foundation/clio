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
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(true, true));
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
			Arg.Is(false),
			Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
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
		// The disable flag is pinned to false: a plain revoke must NEVER ask the writer to turn operation
		// permissions off (that would widen access to every internal user as a side effect).
		_rightsWriter.Received(1).SetObjectRights("UsrPortalSpike", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Is(true), Arg.Is(false),
			Arg.Any<CreatioRequestOptions>());
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
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Is(false), Arg.Any<bool>(),
			Arg.Any<CreatioRequestOptions>());
		_rightsWriter.Received(1).SetObjectRights("UsrPSCategory", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Is(false), Arg.Any<bool>(),
			Arg.Any<CreatioRequestOptions>());
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
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
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
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns exit code 1 and logs the error when the writer reports a failure.")]
	public void Execute_ShouldReturnError_WhenWriterFails() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(true, false, "boom"));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a writer failure returns exit code 1");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("boom")));
	}

	[Test]
	[Description("Reports the refused last-row revoke as an error naming the access widening it avoided, and returns exit code 1 because nothing was applied.")]
	public void Execute_ShouldReportWidening_WhenWriterRefusesLastRowRemoval() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
				Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(true, false, RefusedLastRowRemoval: true));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Revoke = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the revoke the operator asked for did not happen");
		_logger.Received().WriteError(Arg.Is<string>(m =>
			m.Contains("ALL internal users") && m.Contains("--disable-operation-permissions")));
	}

	[Test]
	[Description("Passes the opt-in through to the writer and names the resulting access widening in the per-object result line.")]
	public void Execute_ShouldReportDisabledPermissions_WhenOptInGiven() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
				Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(true, true, OperationPermissionsDisabled: true));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Revoke = true,
			DisableOperationPermissions = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the caller asked for the widening, so applying it is a success");
		_rightsWriter.Received(1).SetObjectRights("UsrPortalSpike", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Is(true), Arg.Is(true),
			Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("available to ALL internal users")));
	}

	private static bool IsExactly(IReadOnlyCollection<ObjectOperation> actual, params ObjectOperation[] expected) =>
		actual.Count == expected.Length && expected.All(actual.Contains);

	[TestCase(null)]
	[TestCase("   ")]
	[Description("Pins the least-privilege root default: no --operations yields exactly read/create/edit (never delete), and never asks to disable operation permissions.")]
	public void Execute_ShouldGrantReadCreateEditOnly_WhenOperationsOmitted(string operations) {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Operations = operations, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a default grant succeeds");
		_rightsWriter.Received(1).SetObjectRights("UsrPortalSpike", Arg.Any<Guid>(),
			Arg.Is<IReadOnlyCollection<ObjectOperation>>(ops =>
				IsExactly(ops, ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit)),
			Arg.Is(false), Arg.Is(false), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Parses operation aliases and de-duplicates them: append,write,read,read -> create, edit, read.")]
	public void Execute_ShouldParseAliasesAndDeduplicate_WhenOperationsRepeated() {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Operations = "append,write,read,read", Confirm = true
		};

		// Act
		_command.Execute(options);

		// Assert
		_rightsWriter.Received(1).SetObjectRights("UsrPortalSpike", Arg.Any<Guid>(),
			Arg.Is<IReadOnlyCollection<ObjectOperation>>(ops =>
				IsExactly(ops, ObjectOperation.Create, ObjectOperation.Edit, ObjectOperation.Read)),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A root object that is not found fails with exit code 1 — nothing was written, so it must not report success.")]
	public void Execute_ShouldFail_WhenRootSchemaNotFound() {
		// Arrange
		_rightsWriter.SetObjectRights("UsrOrders", Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(false, false));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrders", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing root object means the requested change did not happen");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrOrders") && m.Contains("not found")));
	}

	[Test]
	[Description("A connected lookup that is not found only warns: the root change still applied, so the run succeeds.")]
	public void Execute_ShouldOnlyWarn_WhenConnectedSchemaNotFound() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike", true, Arg.Any<string>())
			.Returns(new[] { "UsrPortalSpike", "UsrGone" });
		_rightsWriter.SetObjectRights("UsrGone", Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(false, false));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "only a connected lookup was missing; the named root object was changed");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("UsrGone") && m.Contains("skipped")));
	}

	[Test]
	[Description("Connected lookup objects get READ only by default, while the root keeps read/create/edit — create/edit are never fanned out to shared lookups implicitly.")]
	public void Execute_ShouldGrantReadOnlyToConnected_WhenConnectedOperationsOmitted() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike", true, Arg.Any<string>())
			.Returns(new[] { "UsrPortalSpike", "UsrPSCategory" });
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true, Confirm = true
		};

		// Act
		_command.Execute(options);

		// Assert
		_rightsWriter.Received(1).SetObjectRights("UsrPortalSpike", Arg.Any<Guid>(),
			Arg.Is<IReadOnlyCollection<ObjectOperation>>(ops =>
				IsExactly(ops, ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit)),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
		_rightsWriter.Received(1).SetObjectRights("UsrPSCategory", Arg.Any<Guid>(),
			Arg.Is<IReadOnlyCollection<ObjectOperation>>(ops => IsExactly(ops, ObjectOperation.Read)),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("--connected-operations widens the connected lookups explicitly when the caller asks for it.")]
	public void Execute_ShouldApplyConnectedOperations_WhenExplicitlyWidened() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike", true, Arg.Any<string>())
			.Returns(new[] { "UsrPortalSpike", "UsrPSCategory" });
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true,
			ConnectedOperations = "read,edit", Confirm = true
		};

		// Act
		_command.Execute(options);

		// Assert
		_rightsWriter.Received(1).SetObjectRights("UsrPSCategory", Arg.Any<Guid>(),
			Arg.Is<IReadOnlyCollection<ObjectOperation>>(ops => IsExactly(ops, ObjectOperation.Read, ObjectOperation.Edit)),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("An unknown --connected-operations token is rejected before anything is written.")]
	public void Execute_ShouldReject_WhenConnectedOperationsInvalid() {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true,
			ConnectedOperations = "readd", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an invalid operation list is a usage error");
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("Reports the access NARROWING when a grant turns operation permissions ON for an object that did not use them.")]
	public void Execute_ShouldReportEnablement_WhenGrantTurnsOperationPermissionsOn() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(true, true, OperationPermissionsEnabled: true));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the grant was applied");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("turned ON")));
	}
}
