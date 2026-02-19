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
		_sut = Container.GetRequiredService<IWindowsFeatureManager>();
	}
	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton<IWorkingDirectoriesProvider>(_workingDirectoriesProvider);
		//containerBuilder.RegisterInstance<ConsoleProgressbar>();
	}

	[Test]
	public void BuildPrintString_AllignsItems(){
		
		var actual = _sut.GetActionMaxLength(["x","xx","xxx"]);
		actual.Should().Be(3);
	}
	

}