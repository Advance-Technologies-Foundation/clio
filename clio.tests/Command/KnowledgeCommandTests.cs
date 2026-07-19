using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class InstallKnowledgeCommandTests : BaseCommandTests<InstallKnowledgeOptions> {
	private IKnowledgeInstallationService _service = null!;
	private InstallKnowledgeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeInstallationService>();
		services.AddSingleton(_service);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<InstallKnowledgeCommand>();
	}

	[Test]
	[Description("Returns success when the installation service persists a verified knowledge package.")]
	public void Execute_ShouldReturnSuccess_WhenInstallCompletes() {
		// Arrange
		_service.Install().Returns(new KnowledgeInstallationResult(
			KnowledgeInstallationStatus.Installed, "installed", "1.0.0", "knowledge"));

		// Act
		int exitCode = _command.Execute(new InstallKnowledgeOptions());

		// Assert
		exitCode.Should().Be(0, because: "a completed verified installation is a successful CLI operation");
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class UpdateKnowledgeCommandTests : BaseCommandTests<UpdateKnowledgeOptions> {
	private IKnowledgeInstallationService _service = null!;
	private UpdateKnowledgeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeInstallationService>();
		services.AddSingleton(_service);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UpdateKnowledgeCommand>();
	}

	[Test]
	[Description("Returns failure when update discovery cannot prove or publish a newer package.")]
	public void Execute_ShouldReturnFailure_WhenUpdateIsUnavailable() {
		// Arrange
		_service.Update().Returns(new KnowledgeInstallationResult(
			KnowledgeInstallationStatus.Unavailable, "feed unavailable", "1.0.0", "knowledge"));

		// Act
		int exitCode = _command.Execute(new UpdateKnowledgeOptions());

		// Assert
		exitCode.Should().Be(1, because: "an unavailable update result must not be reported as successful");
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class InfoKnowledgeCommandTests : BaseCommandTests<InfoKnowledgeOptions> {
	private IKnowledgeInstallationService _service = null!;
	private InfoKnowledgeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeInstallationService>();
		services.AddSingleton(_service);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<InfoKnowledgeCommand>();
	}

	[Test]
	[Description("Uses offline mode to report the local cache without asking the package client for update availability.")]
	public void Execute_ShouldSkipUpdateCheck_WhenOfflineIsSet() {
		// Arrange
		_service.GetInfo(false).Returns(Info("knowledge"));

		// Act
		int exitCode = _command.Execute(new InfoKnowledgeOptions { Offline = true, Json = true });

		// Assert
		exitCode.Should().Be(0, because: "a resolved local cache can be inspected without network access");
		_service.Received(1).GetInfo(false);
	}

	private static KnowledgeInstallationInfo Info(string root) => new(
		"appsettings.json", root, true, true, "1.0.0", null, "content", "feed", "Clio.Knowledge",
		System.DateTimeOffset.UtcNow, KnowledgeUpdateAvailability.Unknown, null, null);
}

[TestFixture]
[Property("Module", "Command")]
public sealed class DeleteKnowledgeCommandTests : BaseCommandTests<DeleteKnowledgeOptions> {
	private IKnowledgeInstallationService _service = null!;
	private IInteractiveConsole _interactiveConsole = null!;
	private DeleteKnowledgeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeInstallationService>();
		_interactiveConsole = Substitute.For<IInteractiveConsole>();
		services.AddSingleton(_service);
		services.AddSingleton(_interactiveConsole);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DeleteKnowledgeCommand>();
	}

	[Test]
	[Description("Fails closed without deleting when an interactive user does not confirm knowledge removal.")]
	public void Execute_ShouldNotDelete_WhenConfirmationIsRefused() {
		// Arrange
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(false);

		// Act
		int exitCode = _command.Execute(new DeleteKnowledgeOptions());

		// Assert
		exitCode.Should().Be(1, because: "destructive cache removal requires explicit confirmation");
		_service.DidNotReceive().Delete(Arg.Any<bool>());
	}

	[Test]
	[Description("Uses force as explicit non-interactive confirmation and removes only managed knowledge artifacts.")]
	public void Execute_ShouldDeleteWithoutPrompt_WhenForceIsSet() {
		// Arrange
		_service.Delete(true).Returns(new KnowledgeInstallationResult(
			KnowledgeInstallationStatus.Deleted, "deleted", "1.0.0", "knowledge"));

		// Act
		int exitCode = _command.Execute(new DeleteKnowledgeOptions { Force = true });

		// Assert
		exitCode.Should().Be(0, because: "the force flag is explicit authorization for non-interactive deletion");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
		_service.Received(1).Delete(true);
	}
}
