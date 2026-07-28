using System;
using System.Collections.Generic;
using Clio.Command.RecordRights;
using Clio.Common;
using Clio.Common.RecordRights;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.RecordRights;

[TestFixture]
[Property("Module", "Command")]
public class SetRecordRightsCommandTests : BaseCommandTests<SetRecordRightsOptions> {

	private const string GranteeGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
	private static readonly string EmptyGuid = Guid.Empty.ToString();

	private SetRecordRightsCommand _command;
	private ICreatioRightsClient _rightsClient;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetRecordRightsCommand>();
	}

	public override void TearDown() {
		_rightsClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_rightsClient = Substitute.For<ICreatioRightsClient>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _rightsClient);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Sends a single isNew grant row (never reads current rights) with the grantee GUID and Guid.Empty Id/type when --confirm is set.")]
	public void Execute_ShouldSendSingleGrantRow_WhenConfirmSet() {
		// Arrange
		SetRecordRightsOptions options = new() {
			Entity = "Contact", RecordId = "rec-1", Grantee = GranteeGuid,
			Operation = "edit", Level = "delegated", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful apply returns exit code 0");
		_rightsClient.DidNotReceive().GetRecordRights(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CreatioRequestOptions>());
		_rightsClient.Received(1).ApplyChanges(
			Arg.Is<ApplyChangesRecordRef>(r => r.EntitySchemaName == "Contact" && r.PrimaryColumnValue == "rec-1"),
			Arg.Is<IReadOnlyList<RecordRightRow>>(rows =>
				rows.Count == 1
				&& rows[0].Operation == 1
				&& rows[0].RightLevel == 2
				&& rows[0].IsNew
				&& !rows[0].IsDeleted
				&& rows[0].SysAdminUnit.Value == GranteeGuid
				&& rows[0].SysAdminUnitType == EmptyGuid
				&& !string.IsNullOrEmpty(rows[0].Id) && rows[0].Id != EmptyGuid),
			Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Sends a single isDeleted revoke row (never reads current rights) with the grantee GUID when --confirm is set.")]
	public void Execute_ShouldSendSingleRevokeRow_WhenRevokeAndConfirmSet() {
		// Arrange
		SetRecordRightsOptions options = new() {
			Entity = "Contact", RecordId = "rec-1", Grantee = GranteeGuid,
			Operation = "read", Revoke = true, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful revoke returns exit code 0");
		_rightsClient.DidNotReceive().GetRecordRights(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CreatioRequestOptions>());
		_rightsClient.Received(1).ApplyChanges(
			Arg.Any<ApplyChangesRecordRef>(),
			Arg.Is<IReadOnlyList<RecordRightRow>>(rows =>
				rows.Count == 1
				&& rows[0].Operation == 0
				&& !rows[0].IsNew
				&& rows[0].IsDeleted
				&& rows[0].SysAdminUnit.Value == GranteeGuid
				&& rows[0].SysAdminUnitType == EmptyGuid),
			Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns a friendly error and applies nothing when --grantee is not a GUID.")]
	public void Execute_ShouldReturnError_WhenGranteeIsNotGuid() {
		// Arrange
		SetRecordRightsOptions options = new() {
			Entity = "Contact", RecordId = "rec-1", Grantee = "All employees",
			Operation = "read", Level = "granted", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a non-GUID grantee is an input error");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("GUID")));
		_rightsClient.DidNotReceive().ApplyChanges(Arg.Any<ApplyChangesRecordRef>(),
			Arg.Any<IReadOnlyList<RecordRightRow>>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns a friendly error and applies nothing when the operation name is unknown.")]
	public void Execute_ShouldReturnError_WhenOperationUnknown() {
		// Arrange
		SetRecordRightsOptions options = new() {
			Entity = "Contact", RecordId = "rec-1", Grantee = GranteeGuid,
			Operation = "share", Level = "granted", Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unknown operation name is an input error");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("share")));
		_rightsClient.DidNotReceive().ApplyChanges(Arg.Any<ApplyChangesRecordRef>(),
			Arg.Any<IReadOnlyList<RecordRightRow>>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Refuses to apply the destructive change in a non-interactive run when --confirm is absent.")]
	public void Execute_ShouldRefuse_WhenNonInteractiveAndConfirmAbsent() {
		// Arrange
		// dotnet test runs with stdin redirected, so the command takes the non-interactive refuse branch.
		Assume.That(Console.IsInputRedirected, Is.True, "the confirm-gate refuse path requires redirected stdin");
		SetRecordRightsOptions options = new() {
			Entity = "Contact", RecordId = "rec-1", Grantee = GranteeGuid,
			Operation = "read", Level = "granted", Confirm = false
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a destructive apply without --confirm is refused in a non-interactive run");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("--confirm")));
		_rightsClient.DidNotReceive().ApplyChanges(Arg.Any<ApplyChangesRecordRef>(),
			Arg.Any<IReadOnlyList<RecordRightRow>>(), Arg.Any<CreatioRequestOptions>());
	}
}
