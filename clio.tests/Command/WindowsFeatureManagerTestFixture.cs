using Autofac;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

public class WindowsFeatureManagerTestFixture : BaseClioModuleTests
{

	IWindowsFeatureManager _sut;
	IWorkingDirectoriesProvider _workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
	
	public override void Setup(){
		base.Setup();
		_sut = Container.Resolve<IWindowsFeatureManager>();
	}
	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_workingDirectoriesProvider).As<IWorkingDirectoriesProvider>();
		//containerBuilder.RegisterInstance<ConsoleProgressbar>();
	}

	[Test]
	public void BuildPrintString_AllignsItems(){
		
		var actual = _sut.GetActionMaxLength(["x","xx","xxx"]);
		actual.Should().Be(3);
	}
	

}