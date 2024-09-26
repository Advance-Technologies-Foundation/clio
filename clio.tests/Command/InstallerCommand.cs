using System;
using System.IO;
using Autofac;
using Clio.Command.CreatioInstallCommand;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
internal class InstallerCommandTests : BaseCommandTests<PfInstallerOptions>
{

	
	ICreatioInstallerService _creatioInstallerServiceMock = Substitute.For<ICreatioInstallerService>();
	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_creatioInstallerServiceMock);
		
	}

	public override void Setup() {
		base.Setup();
	}

	[Test(Description = "Should return without waiting for user input")]
	public void Execute_ReturnsWithoutWaitingForInput_WhenSilent(){
		//Arrange
		var command = Container.Resolve<InstallerCommand>();
		PfInstallerOptions options = new () {
			IsSilent = true
		};
		_creatioInstallerServiceMock.Execute(Arg.Any<PfInstallerOptions>())
			.Returns(0);

		//Act
		var actual = command.Execute(options);
		
		//Assert
		actual.Should().Be(0);
	}
	
	[Test(Description = "Execute completes on Enter when not silent")]
	public void Execute_ReturnsAfterConsoleInput_WhenNotSilent(){
		//Arrange
		var command = Container.Resolve<InstallerCommand>();
		PfInstallerOptions options = new () {
			IsSilent = false
		};
		_creatioInstallerServiceMock.Execute(Arg.Any<PfInstallerOptions>())
			.Returns(0);

		//Act
		var stringReader = new StringReader("A");
		Console.SetIn(stringReader);
		
		var actual = command.Execute(options);
		//Assert
		actual.Should().Be(0);
		
	}
	
	[Test(Description = "Should return 0 when OK")]
	public void Execute_DoesNotOpenBrowser_WhenSilent(){
		//Arrange
		var command = Container.Resolve<InstallerCommand>();
		PfInstallerOptions options = new PfInstallerOptions() {
			IsSilent = true
		};
		_creatioInstallerServiceMock.Execute(Arg.Any<PfInstallerOptions>())
			.Returns(0);

		_creatioInstallerServiceMock.StartWebBrowser(Arg.Any<PfInstallerOptions>())
			.Returns(0);

		//Act
		var actual = command.Execute(options);
		
		//Assert
		actual.Should().Be(0);
		_creatioInstallerServiceMock.Received(0).StartWebBrowser(options);
	}
	
	[Test(Description = "Should open browser")]
	public void Execute_OpensBrowser_WhenNotSilent(){
		//Arrange
		var command = Container.Resolve<InstallerCommand>();
		PfInstallerOptions options = new PfInstallerOptions() {
			IsSilent = false
		};
		_creatioInstallerServiceMock.Execute(Arg.Any<PfInstallerOptions>())
			.Returns(0);

		_creatioInstallerServiceMock.StartWebBrowser(Arg.Any<PfInstallerOptions>())
			.Returns(0);

		var stringReader = new StringReader("A");
		Console.SetIn(stringReader);
		
		//Act
		var actual = command.Execute(options);
		//Assert
		actual.Should().Be(0);
		
		
		_creatioInstallerServiceMock.Received(1).StartWebBrowser(options);
	}

}