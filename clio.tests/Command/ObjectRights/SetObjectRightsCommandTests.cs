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
	private IObjectRightsReader _rightsReader;
	private IConnectedObjectsResolver _connectedObjects;
	private IInteractiveConsole _console;
	private IGranteeLookup _granteeLookup;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetObjectRightsCommand>();
	}

	public override void TearDown() {
		_rightsWriter.ClearReceivedCalls();
		_rightsReader.ClearReceivedCalls();
		_connectedObjects.ClearReceivedCalls();
		_console.ClearReceivedCalls();
		_granteeLookup.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_rightsWriter = Substitute.For<IObjectRightsWriter>();
		_rightsReader = Substitute.For<IObjectRightsReader>();
		_rightsReader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(callInfo => new ObjectRightsInfo(true, (string)callInfo[0], null, false, Array.Empty<RoleOperationRights>()));
		_connectedObjects = Substitute.For<IConnectedObjectsResolver>();
		_console = Substitute.For<IInteractiveConsole>();
		_granteeLookup = Substitute.For<IGranteeLookup>();
		_granteeLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns("All external users");
		_logger = Substitute.For<ILogger>();
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(ObjectRightsOutcome.Changed));
		// Default: no fan-out — the resolver returns just the root object.
		_connectedObjects.Resolve(Arg.Any<string>(), Arg.Any<bool>())
			.Returns(callInfo => Resolution((string)callInfo[0]));
		containerBuilder.AddTransient(_ => _rightsWriter);
		containerBuilder.AddTransient(_ => _rightsReader);
		containerBuilder.AddTransient(_ => _connectedObjects);
		containerBuilder.AddTransient(_ => _console);
		containerBuilder.AddTransient(_ => _granteeLookup);
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
		_connectedObjects.Resolve("UsrPortalSpike", true)
			.Returns(Resolution("UsrPortalSpike", "UsrPSCategory"));
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
		_console.IsInteractive.Returns(false);
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
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(ObjectRightsOutcome.Failed, "boom"));
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
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.RefusedLastRowRemoval));
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
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.ChangedAndDisabled));
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

	private static ConnectedObjectsResolution Resolution(params string[] objects) =>
		new(objects, Array.Empty<string>());

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
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(ObjectRightsOutcome.NotFound));
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
		_connectedObjects.Resolve("UsrPortalSpike", true)
			.Returns(Resolution("UsrPortalSpike", "UsrGone"));
		_rightsWriter.SetObjectRights("UsrGone", Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(ObjectRightsOutcome.NotFound));
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
		_connectedObjects.Resolve("UsrPortalSpike", true)
			.Returns(Resolution("UsrPortalSpike", "UsrPSCategory"));
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
		_connectedObjects.Resolve("UsrPortalSpike", true)
			.Returns(Resolution("UsrPortalSpike", "UsrPSCategory"));
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
	[Description("An interactive run that the operator declines writes nothing and returns 0 (cancelled, not failed).")]
	public void Execute_ShouldCancel_WhenInteractivePromptDeclined() {
		// Arrange
		_console.IsInteractive.Returns(true);
		_console.Prompt(Arg.Any<string>()).Returns(false);
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Confirm = false };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a declined prompt is a cancellation");
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("An --operations value made only of separators is rejected instead of writing an empty row.")]
	public void Execute_ShouldReject_WhenOperationsHasOnlySeparators() {
		// Arrange
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, Operations = ", ,", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an empty operation set would enable operation permissions while granting nothing");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("no operation given")));
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("A failed connected-object enumeration with --include-connected writes nothing and fails, instead of granting the root alone and reporting success.")]
	public void Execute_ShouldFailWithoutWriting_WhenConnectedEnumerationFails() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike", true)
			.Returns(new ConnectedObjectsResolution(new[] { "UsrPortalSpike" }, Array.Empty<string>(), "schema read failed"));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the requested fan-out could not be resolved");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("schema read failed") && m.Contains("Nothing was changed")));
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("A security/system lookup excluded by the resolver is named in a warning and never written.")]
	public void Execute_ShouldWarnAndSkip_WhenConnectedObjectExcluded() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike", true)
			.Returns(new ConnectedObjectsResolution(new[] { "UsrPortalSpike", "UsrPSCategory" }, new[] { "SysAdminUnit" }));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the root and the ordinary lookup were granted");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("SysAdminUnit") && m.Contains("security/system object")));
		_rightsWriter.DidNotReceive().SetObjectRights("SysAdminUnit", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("--revoke --include-connected without --connected-operations changes the root only: the read-only grant default must not strip READ from shared lookups.")]
	public void Execute_ShouldLeaveConnectedUntouched_WhenRevokeWithoutConnectedOperations() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "Contact"));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "delete", Revoke = true,
			IncludeConnected = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the root revoke succeeded");
		_connectedObjects.Received(1).Resolve("UsrOrder", false);
		_rightsWriter.DidNotReceive().SetObjectRights("Contact", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("leaves the connected objects untouched")));
	}

	[Test]
	[Description("--revoke --include-connected with explicit --connected-operations revokes exactly those on the lookups, and never asks to turn a connected object's operation permissions off.")]
	public void Execute_ShouldRevokeExplicitOpsOnConnected_WithoutDisablingThem() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "Contact"));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Revoke = true,
			IncludeConnected = true, ConnectedOperations = "read", DisableOperationPermissions = true, Confirm = true
		};

		// Act
		_command.Execute(options);

		// Assert
		_rightsWriter.Received(1).SetObjectRights("UsrOrder", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Is(true), Arg.Is(true), Arg.Any<CreatioRequestOptions>());
		_rightsWriter.Received(1).SetObjectRights("Contact", Arg.Any<Guid>(),
			Arg.Is<IReadOnlyCollection<ObjectOperation>>(ops => IsExactly(ops, ObjectOperation.Read)),
			Arg.Is(true), Arg.Is(false), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("When the root change fails, the connected objects are not attempted, so no half-applied fan-out is left behind.")]
	public void Execute_ShouldStopFanOut_WhenRootFails() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike", true).Returns(Resolution("UsrPortalSpike", "UsrPSCategory"));
		_rightsWriter.SetObjectRights("UsrPortalSpike", Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(ObjectRightsOutcome.Failed, "boom"));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the root change failed");
		_rightsWriter.DidNotReceive().SetObjectRights("UsrPSCategory", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("were not attempted")));
	}

	[Test]
	[Description("A failure on one connected object is named, the remaining objects are still attempted, and the run exits 1.")]
	public void Execute_ShouldNameFailedConnectedObject_AndContinue() {
		// Arrange
		_connectedObjects.Resolve("UsrPortalSpike", true).Returns(Resolution("UsrPortalSpike", "UsrA", "UsrB"));
		_rightsWriter.SetObjectRights("UsrA", Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(ObjectRightsOutcome.Failed, "HTTP 500"));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrPortalSpike", Grantee = Grantee, IncludeConnected = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "one object failed");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrA") && m.Contains("HTTP 500")));
		_rightsWriter.Received(1).SetObjectRights("UsrB", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	// ---- Review round 5 ----

	[Test]
	[Description("A revoke on a ROOT object that is not administered fails with exit code 1 and says the object stays open to every internal user.")]
	public void Execute_ShouldFail_WhenRevokingOnNotAdministeredRoot() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.RevokeOnNotAdministered));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOpen", Grantee = Grantee, Revoke = true, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the restriction the caller asked for did not happen");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrOpen") && m.Contains("not administered")
			&& m.Contains("cannot restrict")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("no change") || m.Contains("revoked")));
	}

	[Test]
	[Description("A revoke that meets a non-administered CONNECTED object only warns; the root revoke still counts.")]
	public void Execute_ShouldWarn_WhenRevokingOnNotAdministeredConnected() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsWriter.SetObjectRights("UsrStatus", Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.RevokeOnNotAdministered));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Revoke = true, IncludeConnected = true,
			ConnectedOperations = "read", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the root revoke succeeded; the lookup was open to internal users anyway");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("UsrStatus") && m.Contains("cannot restrict")));
	}

	[Test]
	[Description("A refused last-row revoke on a CONNECTED object fails and says a fan-out never turns a lookup off, instead of suggesting the disable flag.")]
	public void Execute_ShouldExplain_WhenConnectedLastRowRefused() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsWriter.SetObjectRights("UsrStatus", Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.RefusedLastRowRemoval));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Revoke = true, IncludeConnected = true,
			ConnectedOperations = "read", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the connected revoke did not happen");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrStatus")
			&& m.Contains("never turned off by a fan-out") && !m.Contains("re-run with --disable-operation-permissions")));
	}

	[Test]
	[Description("An exception thrown by the writer is caught and reported with exit code 1.")]
	public void Execute_ShouldReturnError_WhenWriterThrows() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unexpected failure is not a success");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("boom")));
	}

	[Test]
	[Description("An interactive run that the operator approves applies the change.")]
	public void Execute_ShouldApply_WhenInteractivePromptApproved() {
		// Arrange
		_console.IsInteractive.Returns(true);
		_console.Prompt(Arg.Any<string>()).Returns(true);
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Confirm = false };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the operator approved the change");
		_rightsWriter.Received(1).SetObjectRights("UsrOrder", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("The confirmation text for a grant names every target object, both operation sets, and the enable-narrowing note.")]
	public void Execute_ShouldNameTargetsAndNarrowing_InGrantConfirmation() {
		// Arrange
		_console.IsInteractive.Returns(false);
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus", "UsrPartner"));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, IncludeConnected = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a non-interactive run without --confirm is refused");
		_logger.Received().WriteError(Arg.Is<string>(m =>
			m.Contains("Grant object operations [read/create/edit]") && m.Contains("'UsrOrder'")
			&& m.Contains("[read] on 2 connected object(s) (UsrStatus, UsrPartner)")
			&& m.Contains("has them turned ON")));
	}

	[Test]
	[Description("The confirmation text for a revoke with the disable opt-in names the widening to ALL internal users for the root.")]
	public void Execute_ShouldNameWidening_InDisableConfirmation() {
		// Arrange
		_console.IsInteractive.Returns(false);
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Revoke = true, DisableOperationPermissions = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a non-interactive run without --confirm is refused");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("Revoke object operations")
			&& m.Contains("available to ALL internal users") && m.Contains("connected objects are never turned off")));
	}

	[Test]
	[Description("An invalid --connected-operations value names the option in the error.")]
	public void Execute_ShouldNameOption_WhenConnectedOperationsInvalid() {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, IncludeConnected = true, ConnectedOperations = "readd", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unknown connected operation is a usage error");
		_logger.Received().WriteError("Error: --connected-operations: unknown operation 'readd'. Use read,create,edit,delete.");
	}

	// ---- Final review ----

	private static readonly Guid EmployeesId = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private void WriterReturnsEnabled(params RoleOperationRights[] rolesAfter) =>
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.ChangedAndEnabled, RolesAfterEnable: rolesAfter));

	[Test]
	[Description("After turning operation permissions on, the result names the roles that actually hold rights, read back from the server.")]
	public void Execute_ShouldNameRolesAfterEnable_WhenOthersKeptAccess() {
		// Arrange
		WriterReturnsEnabled(
			new RoleOperationRights(EmployeesId, "All employees", true, true, true, true),
			new RoleOperationRights(Guid.Parse(Grantee), "All external users", true, false, false, false));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "internal users kept access through All employees");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("turned ON")
			&& m.Contains("roles with rights now: All employees (read/create/edit/delete)")));
	}

	[Test]
	[Description("When the read-back after enabling shows only the grantee, the run fails loudly: every other internal user lost access.")]
	public void Execute_ShouldFail_WhenOnlyGranteeHoldsRightsAfterEnable() {
		// Arrange
		WriterReturnsEnabled(new RoleOperationRights(Guid.Parse(Grantee), "All external users", true, false, false, false));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the grant cut every other internal user off the object");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("UsrOrder") && m.Contains("LOST access")));
	}

	[Test]
	[Description("A failed read-back after enabling is reported as a warning, not as a verified outcome.")]
	public void Execute_ShouldWarn_WhenReadBackAfterEnableFails() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.ChangedAndEnabled, ReadBackError: "timeout"));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the grant itself was saved");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("reading it back failed") && m.Contains("timeout")));
	}

	[Test]
	[Description("A grantee id that does not exist in SysAdminUnit fails before anything is written.")]
	public void Execute_ShouldFail_WhenGranteeDoesNotExist() {
		// Arrange
		_granteeLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns((string)null);
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "granting to a principal nobody holds would only cut others off");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("was not found in SysAdminUnit")));
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("The confirmation text names the grantee by name as well as by id.")]
	public void Execute_ShouldNameGrantee_InConfirmation() {
		// Arrange
		_console.IsInteractive.Returns(false);
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a non-interactive run without --confirm is refused");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains($"'All external users' ({Grantee})")));
	}

	[Test]
	[Description("A security/system ROOT object may only be granted read without --allow-security-object.")]
	public void Execute_ShouldRefuse_WhenGrantingBeyondReadOnSecurityRoot() {
		// Arrange
		SetObjectRightsOptions options = new() { EntitySchemaName = "SysUserInRole", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "create/edit on a security object is a privilege-escalation path");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("security/system object") && m.Contains("--allow-security-object")));
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[TestCase("read", false)]
	[TestCase("read,create,edit", true)]
	[Description("A security/system root accepts read, and accepts more with the explicit opt-in.")]
	public void Execute_ShouldAllowSecurityRoot_WhenReadOnlyOrOptedIn(string operations, bool allow) {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "SysUserInRole", Grantee = Grantee, Operations = operations,
			AllowSecurityObject = allow, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "read-only, or an explicit opt-in, is allowed");
		_rightsWriter.Received(1).SetObjectRights("SysUserInRole", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A plain revoke on a security/system root is allowed without the opt-in: it only narrows access.")]
	public void Execute_ShouldAllowRevoke_OnSecurityRoot() {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "SysUserInRole", Grantee = Grantee, Operations = "read,create,edit", Revoke = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a revoke that cannot turn permissions off never widens access");
		_rightsWriter.Received(1).SetObjectRights("SysUserInRole", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), true, false, Arg.Any<CreatioRequestOptions>());
	}

	// ---- Preview / confirmation code (M1) ----

	private string CapturePreviewCode(SetObjectRightsOptions options) {
		string code = null;
		_logger.When(l => l.WriteInfo(Arg.Is<string>(m => m.StartsWith("confirmation-code: "))))
			.Do(call => code = ((string)call[0]).Substring("confirmation-code: ".Length));
		_command.Execute(options);
		return code;
	}

	[Test]
	[Description("A preview writes nothing and lists every target with its current state, including that operation permissions will be turned ON, plus a confirmation code.")]
	public void Execute_ShouldListTargetsAndWriteNothing_WhenPreview() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOrder", null, true, new[] {
				new RoleOperationRights(Guid.Parse(Grantee), "All external users", true, false, false, false),
				new RoleOperationRights(Guid.Parse(Grantee), "All external users", false, false, true, false),
				new RoleOperationRights(EmployeesId, "All employees", true, true, true, true)
			}));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", IncludeConnected = true, Preview = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a preview is not a failure");
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.StartsWith("PREVIEW — nothing was changed.")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("UsrOrder (root): grant [read]")
			&& m.Contains("grantee holds read/edit")
			&& m.Contains("other roles with rights: All employees (read/create/edit/delete)")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("UsrStatus (connected): grant [read]")
			&& m.Contains("they will be turned ON")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.StartsWith("confirmation-code: ")));
	}

	[Test]
	[Description("A confirmed call with the code from an unchanged preview applies the change without prompting.")]
	public void Execute_ShouldApply_WhenCodeMatchesPreview() {
		// Arrange
		SetObjectRightsOptions preview = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Preview = true };
		string code = CapturePreviewCode(preview);
		_console.IsInteractive.Returns(false);
		SetObjectRightsOptions confirmed = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", ConfirmationCode = code
		};

		// Act
		int exitCode = _command.Execute(confirmed);

		// Assert
		code.Should().NotBeNullOrWhiteSpace(because: "the preview must print a code");
		exitCode.Should().Be(0, because: "the targets are exactly what the preview showed");
		_rightsWriter.Received(1).SetObjectRights("UsrOrder", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A code from a preview whose target set has since changed (a new lookup) is refused and nothing is written.")]
	public void Execute_ShouldRefuse_WhenTargetsChangedSincePreview() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder"));
		SetObjectRightsOptions preview = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", IncludeConnected = true, Preview = true
		};
		string code = CapturePreviewCode(preview);
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrNewLookup"));
		SetObjectRightsOptions confirmed = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", IncludeConnected = true, ConfirmationCode = code
		};

		// Act
		int exitCode = _command.Execute(confirmed);

		// Assert
		exitCode.Should().Be(1, because: "the user approved a different set of targets");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("confirmation code does not match")));
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("A code from a preview of different arguments (another operation set) is refused.")]
	public void Execute_ShouldRefuse_WhenArgumentsDifferFromPreview() {
		// Arrange
		string code = CapturePreviewCode(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Preview = true
		});
		SetObjectRightsOptions confirmed = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read,create,edit", ConfirmationCode = code
		};

		// Act
		int exitCode = _command.Execute(confirmed);

		// Assert
		exitCode.Should().Be(1, because: "the preview approved read only");
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	private void RootRightsAre(bool administered, params RoleOperationRights[] roles) =>
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOrder", null, administered, roles));

	[Test]
	[Description("A code from a preview is refused when the object's operation permissions were turned on and the grantee got a row in between.")]
	public void Execute_ShouldRefuse_WhenRightsChangedSincePreview() {
		// Arrange
		RootRightsAre(false);
		string code = CapturePreviewCode(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Preview = true
		});
		RootRightsAre(true, new RoleOperationRights(Guid.Parse(Grantee), "All external users", true, false, false, false));

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", ConfirmationCode = code
		});

		// Assert
		exitCode.Should().Be(1, because: "the user approved turning permissions ON, which is no longer what happens");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("confirmation code does not match")));
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("A code is refused when ANOTHER role's row changed since the preview: that row decides whether a revoke with the disable opt-in narrows the object or opens it to every internal user.")]
	public void Execute_ShouldRefuse_WhenOtherRoleRowChangedSincePreview() {
		// Arrange
		RoleOperationRights granteeRow = new(Guid.Parse(Grantee), "All external users", true, false, false, false);
		RootRightsAre(true, granteeRow, new RoleOperationRights(EmployeesId, "All employees", true, true, true, true));
		SetObjectRightsOptions preview = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Revoke = true,
			DisableOperationPermissions = true, Preview = true
		};
		string code = CapturePreviewCode(preview);
		RootRightsAre(true, granteeRow);

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Revoke = true,
			DisableOperationPermissions = true, ConfirmationCode = code
		});

		// Assert
		exitCode.Should().Be(1, because: "the revoke would now turn the object OFF, which the preview did not show");
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[TestCase(true, false, TestName = "Execute_ShouldRefuse_WhenDisableFlagAddedSincePreview")]
	[TestCase(false, true, TestName = "Execute_ShouldRefuse_WhenAllowSecurityFlagAddedSincePreview")]
	[Description("A code from a preview is refused when a flag that changes the outcome was added only to the confirmed call.")]
	public void Execute_ShouldRefuse_WhenFlagAddedSincePreview(bool disable, bool allowSecurity) {
		// Arrange
		string code = CapturePreviewCode(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Revoke = disable, Preview = true
		});

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Revoke = disable,
			DisableOperationPermissions = disable, AllowSecurityObject = allowSecurity, ConfirmationCode = code
		});

		// Assert
		exitCode.Should().Be(1, because: "the user approved the call without that flag");
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("The code does not depend on the order the operations were typed in.")]
	public void Execute_ShouldApply_WhenOperationsReordered() {
		// Arrange
		string code = CapturePreviewCode(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read,edit", Preview = true
		});

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "edit,read", ConfirmationCode = code
		});

		// Assert
		exitCode.Should().Be(0, because: "the same operations in another order are the same change");
	}

	[TestCase(true, TestName = "Execute_ShouldFailPreview_WhenRootUnreadable")]
	[TestCase(false, TestName = "Execute_ShouldFailPreview_WhenRootNotFound")]
	[Description("A preview of a root that could not be read, or does not exist, fails and issues no confirmation code.")]
	public void Execute_ShouldFailPreview_WhenRootCannotBeDescribed(bool readError) {
		// Arrange
		_rightsReader.GetObjectRights("UsrOrder", Arg.Any<CreatioRequestOptions>())
			.Returns(readError
				? new ObjectRightsInfo(false, "UsrOrder", null, false, Array.Empty<RoleOperationRights>(), "HTTP 500")
				: new ObjectRightsInfo(false, "UsrOrder", null, false, Array.Empty<RoleOperationRights>()));

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Preview = true
		});

		// Assert
		exitCode.Should().Be(1, because: "there is nothing the user could approve");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("no confirmation code was issued")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.StartsWith("confirmation-code: ")));
	}

	[Test]
	[Description("A root that WAS written but lost other users' access still gets its lookups: stopping there would leave the root granted and its lookups not.")]
	public void Execute_ShouldStillFanOut_WhenRootWrittenButOthersLostAccess() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsWriter.SetObjectRights("UsrOrder", Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
				Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.ChangedAndEnabled, RolesAfterEnable: new[] {
				new RoleOperationRights(Guid.Parse(Grantee), "All external users", true, false, false, false)
			}));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", IncludeConnected = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "internal users lost access to the root");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("LOST access") && m.Contains("already saved")));
		_rightsWriter.Received(1).SetObjectRights("UsrStatus", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("root object was not changed")));
	}

	[Test]
	[Description("Granting All employees itself is not reported as 'others lost access' when its row is the only one after enabling.")]
	public void Execute_ShouldSucceed_WhenGranteeIsAllEmployees() {
		// Arrange
		WriterReturnsEnabled(new RoleOperationRights(EmployeesId, "All employees", true, true, true, false));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = EmployeesId.ToString(), Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "every internal user keeps access through All employees");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("roles with rights now: All employees (read/create/edit)")));
	}

	[Test]
	[Description("A read-back after enabling that shows no role with rights at all fails with its own message.")]
	public void Execute_ShouldFail_WhenReadBackShowsNoRoleWithRights() {
		// Arrange
		WriterReturnsEnabled(new RoleOperationRights(EmployeesId, "All employees", false, false, false, false));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "nobody can reach the object");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("NO role with rights")));
	}

	[TestCase(false, 1, TestName = "Execute_ShouldRefuseDisable_OnSecurityRootWithoutOptIn")]
	[TestCase(true, 0, TestName = "Execute_ShouldAllowDisable_OnSecurityRootWithOptIn")]
	[Description("A revoke that may turn a security/system root's operation permissions OFF needs --allow-security-object: turning it off opens the table to every internal user.")]
	public void Execute_ShouldGateDisable_OnSecurityRoot(bool allow, int expectedExitCode) {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "SysUserInRole", Grantee = Grantee, Operations = "read", Revoke = true,
			DisableOperationPermissions = true, AllowSecurityObject = allow, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(expectedExitCode, because: "only the explicit opt-in may open a security table");
		_rightsWriter.Received(allow ? 1 : 0).SetObjectRights("SysUserInRole", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("The last-grant refusal on a security/system root does not suggest --disable-operation-permissions.")]
	public void Execute_ShouldNotHintDisable_WhenLastGrantRefusedOnSecurityRoot() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.RefusedLastRowRemoval));
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "SysUserInRole", Grantee = Grantee, Operations = "read", Revoke = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the revoke did not happen");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("LAST effective grant")
			&& !m.Contains("--disable-operation-permissions")));
	}

	[Test]
	[Description("A grantee lookup that fails at the service fails the run before anything is read or written.")]
	public void Execute_ShouldFail_WhenGranteeLookupFails() {
		// Arrange
		_granteeLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("HTTP 500"));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Preview = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unchecked grantee must not be written");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("could not check grantee") && m.Contains("HTTP 500")));
		_rightsReader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("An object already in the requested state is reported as no change, and the run succeeds.")]
	public void Execute_ShouldReportNoChange_WhenAlreadyInRequestedState() {
		// Arrange
		_rightsWriter.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsChange(ObjectRightsOutcome.NoChange));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "nothing to change is not a failure");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("UsrOrder: already in the requested state")));
	}

	// ---- Review round 6 ----

	[TestCase("SysEntitySchemaOperationRight ", TestName = "Execute_ShouldGateSecurityRoot_WhenNameHasTrailingSpace")]
	[TestCase(" SysAdminUnit", TestName = "Execute_ShouldGateSecurityRoot_WhenNameHasLeadingSpace")]
	[Description("A padded name is trimmed before the security/system gate, so SQL Server's trailing-space comparison cannot smuggle a rights table past it.")]
	public void Execute_ShouldGateSecurityRoot_WhenNamePadded(string name) {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = name, Grantee = Grantee, Operations = "read,create", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the trimmed name is a security/system object");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("security/system object")));
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[TestCase("Usr Order")]
	[TestCase("UsrOrder;")]
	[TestCase("1UsrOrder")]
	[Description("A name that is not a schema identifier is refused before anything is resolved, read or written.")]
	public void Execute_ShouldRefuse_WhenNameIsNotAnIdentifier(string name) {
		// Arrange
		SetObjectRightsOptions options = new() { EntitySchemaName = name, Grantee = Grantee, Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "only a plain schema identifier can name an object");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("is not a schema name")));
		_connectedObjects.DidNotReceiveWithAnyArgs().Resolve(default, default);
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[TestCase(true, null, TestName = "Execute_ShouldRefuse_WhenPreviewWithConfirm")]
	[TestCase(true, "abc", TestName = "Execute_ShouldRefuse_WhenPreviewWithCode")]
	[TestCase(false, "abc", TestName = "Execute_ShouldRefuse_WhenConfirmWithCode")]
	[Description("--preview, --confirm and --confirmation-code are mutually exclusive: a combination is refused rather than silently picking one.")]
	public void Execute_ShouldRefuse_WhenConfirmationFlagsCombined(bool preview, string code) {
		// Arrange
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Preview = preview, Confirm = !preview || code is null,
			ConfirmationCode = code
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the call does not say which of the modes it means");
		_rightsReader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("An interactive run that carries a matching code applies without prompting, and the code is matched case- and whitespace-insensitively.")]
	public void Execute_ShouldApplyWithoutPrompt_WhenInteractiveRunCarriesCode() {
		// Arrange
		string code = CapturePreviewCode(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Preview = true
		});
		_console.IsInteractive.Returns(true);

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read",
			ConfirmationCode = "  " + code.ToUpperInvariant() + " "
		});

		// Assert
		exitCode.Should().Be(0, because: "the code is the confirmation");
		_console.DidNotReceiveWithAnyArgs().Prompt(default);
		_rightsWriter.Received(1).SetObjectRights("UsrOrder", Arg.Any<Guid>(),
			Arg.Any<IReadOnlyCollection<ObjectOperation>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("After enabling, another role holding rights does not count: internal users keep access only through All employees.")]
	public void Execute_ShouldFail_WhenAllEmployeesHoldsNoReadAfterEnable() {
		// Arrange
		WriterReturnsEnabled(
			new RoleOperationRights(Guid.Parse("11111111-2222-3333-4444-555555555555"), "Old portal role", true, false, false, false),
			new RoleOperationRights(Guid.Parse(Grantee), "All external users", true, false, false, false));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a stale role's row says nothing about every other internal user");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("All employees holds no read") && m.Contains("already saved")));
	}

	[Test]
	[Description("The preview of a grant on an object that is not administered names the existing rows that become effective and says All employees is added.")]
	public void Execute_ShouldNameRevivedRowsAndAllEmployees_WhenPreviewEnables() {
		// Arrange
		RootRightsAre(false, new RoleOperationRights(Guid.Parse("11111111-2222-3333-4444-555555555555"),
			"Old portal role", true, false, false, false));

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", Preview = true
		});

		// Assert
		exitCode.Should().Be(0, because: "a preview is not a failure");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("they will be turned ON")
			&& m.Contains("an All employees row with read/create/edit/delete is added")
			&& m.Contains("existing rows that become effective: Old portal role (read)")));
	}

	[TestCase(true, TestName = "Execute_ShouldPreview_WhenConnectedTargetUnreadable")]
	[TestCase(false, TestName = "Execute_ShouldPreview_WhenConnectedTargetNotFound")]
	[Description("A connected target that cannot be read or does not exist is shown in the preview; only the root failing blocks the code.")]
	public void Execute_ShouldPreview_WhenConnectedTargetCannotBeDescribed(bool readError) {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(readError, "UsrStatus", null, false, Array.Empty<RoleOperationRights>(),
				readError ? "HTTP 500" : null));

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", IncludeConnected = true, Preview = true
		});

		// Assert
		exitCode.Should().Be(0, because: "the root was described, so the user can approve the call");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("UsrStatus (connected)")
			&& m.Contains(readError ? "could not read its rights" : "not found")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.StartsWith("confirmation-code: ")));
	}

	[Test]
	[Description("A connected target that was unreadable at preview and readable at confirm changes the code: the user never saw its state.")]
	public void Execute_ShouldRefuse_WhenConnectedTargetBecameReadableSincePreview() {
		// Arrange
		_connectedObjects.Resolve("UsrOrder", true).Returns(Resolution("UsrOrder", "UsrStatus"));
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrStatus", null, false, Array.Empty<RoleOperationRights>(), "HTTP 500"));
		string code = CapturePreviewCode(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", IncludeConnected = true, Preview = true
		});
		_rightsReader.GetObjectRights("UsrStatus", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrStatus", null, false, Array.Empty<RoleOperationRights>()));

		// Act
		int exitCode = _command.Execute(new SetObjectRightsOptions {
			EntitySchemaName = "UsrOrder", Grantee = Grantee, Operations = "read", IncludeConnected = true,
			ConfirmationCode = code
		});

		// Assert
		exitCode.Should().Be(1, because: "the target's state is not what the preview showed");
		_rightsWriter.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}
}
