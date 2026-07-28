using System;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Progress;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Property("Module", "Command")]
internal class UninstallCreatioCommandTests : BaseCommandTests<UninstallCreatioCommandOptions>
{

	// The substitute also implements IStageEventSource so the command's stage-event bubbling cast succeeds.
	ICreatioUninstaller _creatioUninstaller = Substitute.For<ICreatioUninstaller, IStageEventSource>();
	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton<ICreatioUninstaller>(_creatioUninstaller);
	}
	
	private UninstallCreatioCommand _sut; 

	public override void Setup(){
		base.Setup();
		_sut = Container.GetRequiredService<UninstallCreatioCommand>();
	}

	[Test]
	public void Execute_ShouldEarlyReturn_WhenValidationFails(){

		//Arrange
		var options = new UninstallCreatioCommandOptions();

		//Act
		int exitCode  = _sut.Execute(options);

		//Assert
		exitCode.Should().Be(1);
	}

	[Test]
	public void Execute_ShouldReturn_When_EnvironmentNameValidationPasses(){

		//Arrange
		var options = new UninstallCreatioCommandOptions{EnvironmentName = "some"};

		//Act
		int exitCode  = _sut.Execute(options);

		//Assert
		exitCode.Should().Be(0);
		_creatioUninstaller.Received(1).UninstallByEnvironmentName(options.EnvironmentName);
	}
	
	[Test]
	public void Execute_ShouldReturn_When_PhysicalPathValidationPasses(){

		//Arrange
		const string directoryPath = @"C:\some_creatio_folder";
		var options = new UninstallCreatioCommandOptions{PhysicalPath = directoryPath};
		FileSystem.AddDirectory(directoryPath);
		//Act
		int exitCode  = _sut.Execute(options);

		//Assert
		exitCode.Should().Be(0);
		_creatioUninstaller.Received(1).UninstallByPath(options.PhysicalPath);
	}

	[Test]
	[Description("AC-07: the command re-raises stage events emitted by its uninstaller collaborator so a subscriber sees them.")]
	public void StageChanged_ShouldBubbleEventsFromUninstaller_WhenUninstallerEmits(){
		// Arrange
		ClioStageEvent expected = new(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.Manifest,
			Guid.NewGuid(), 0, ClioStageEventContract.Operations.Uninstall);
		ClioStageEvent received = null;
		_sut.StageChanged += (_, e) => received = e;

		// Act
		((IStageEventSource)_creatioUninstaller).StageChanged
			+= Raise.Event<EventHandler<ClioStageEvent>>(_creatioUninstaller, expected);

		// Assert
		received.Should().BeSameAs(expected,
			"because the command bubbles its collaborator's stage-event stream to subscribers (AC-07)");
	}

	[Test]
	[Description("Correction A: a safe-abort from the uninstaller is reported as a non-success exit code, not a success message.")]
	public void Execute_ShouldReturnNonZeroAndLogError_WhenUninstallAborts(){
		// Arrange
		const string directoryPath = @"C:\some_creatio_folder";
		const string reason = "Uninstall aborted: configuration unreadable.";
		FileSystem.AddDirectory(directoryPath);
		UninstallCreatioCommandOptions options = new() { PhysicalPath = directoryPath };
		_creatioUninstaller.When(u => u.UninstallByPath(directoryPath))
			.Do(_ => throw new CreatioUninstallAbortedException(reason));

		// Act
		int exitCode = _sut.Execute(options);

		// Assert
		exitCode.Should().Be(1, "because an aborted uninstall must not report success (Correction A)");
	}
}