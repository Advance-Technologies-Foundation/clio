using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
internal class UninstallCreatioCommandTests : BaseCommandTests<UninstallCreatioCommandOptions>
{

	ICreatioUninstaller _creatioUninstaller = Substitute.For<ICreatioUninstaller>();
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
}