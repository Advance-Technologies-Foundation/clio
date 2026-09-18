using System;
using Clio.Command.ObjectRights;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

[TestFixture]
[Property("Module", "Command")]
public class SetObjectRightsCommandTests : BaseCommandTests<SetObjectRightsOptions> {

	private SetObjectRightsCommand _command;
	private ISectionServiceClient _sectionServiceClient;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetObjectRightsCommand>();
	}

	public override void TearDown() {
		_sectionServiceClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_sectionServiceClient = Substitute.For<ISectionServiceClient>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _sectionServiceClient);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Calls the SectionService with only the root entity name and returns 0 when --confirm is set.")]
	public void Execute_ShouldCallService_WhenConfirmSet() {
		// Arrange
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful grant returns exit code 0");
		_sectionServiceClient.Received(1).SetConnectedEntitiesAdministratedByEntity(
			Arg.Is<string>(name => name == "UsrPortalSpike"),
			Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns a friendly error and calls nothing when --entity-schema-name is empty.")]
	public void Execute_ShouldReturnError_WhenEntitySchemaNameMissing() {
		// Arrange
		SetObjectRightsOptions options = new() { EntitySchemaName = "  ", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing entity schema name is an input error");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("entity-schema-name")));
		_sectionServiceClient.DidNotReceive().SetConnectedEntitiesAdministratedByEntity(
			Arg.Any<string>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Refuses to apply the destructive change in a non-interactive run when --confirm is absent.")]
	public void Execute_ShouldRefuse_WhenNonInteractiveAndConfirmAbsent() {
		// Arrange
		// dotnet test runs with stdin redirected, so the command takes the non-interactive refuse branch.
		Assume.That(Console.IsInputRedirected, Is.True, "the confirm-gate refuse path requires redirected stdin");
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Confirm = false };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a destructive change without --confirm is refused in a non-interactive run");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("--confirm")));
		_sectionServiceClient.DidNotReceive().SetConnectedEntitiesAdministratedByEntity(
			Arg.Any<string>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns exit code 1 and logs the error when the SectionService call fails.")]
	public void Execute_ShouldReturnError_WhenServiceThrows() {
		// Arrange
		_sectionServiceClient
			.When(client => client.SetConnectedEntitiesAdministratedByEntity(
				Arg.Any<string>(), Arg.Any<CreatioRequestOptions>()))
			.Do(_ => throw new InvalidOperationException("boom"));
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike", Confirm = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a service failure returns exit code 1");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("boom")));
	}
}
