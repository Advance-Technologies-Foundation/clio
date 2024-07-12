using System.Threading.Tasks;
using Autofac;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
internal class UninstallCreatioCommandTests : BaseCommandTests<UninstallCreatioCommandOptions>
{

	private UninstallCreatioCommand _sut; 
	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
	}

	public override void Setup(){
		base.Setup();
		_sut = _container.Resolve<UninstallCreatioCommand>();
	}

	[Test(Description = "Command should execute without errors")]
	public async Task Execute_ShouldReturn_WhenCalled(){

		//Arrange
		var options = new UninstallCreatioCommandOptions();

		//Act
		int exitCode  = _sut.Execute(options);

		//Assert
		exitCode.Should().Be(0);
	}

}