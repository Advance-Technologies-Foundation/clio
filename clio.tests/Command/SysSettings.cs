using System.Threading.Tasks;
using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Infrastructure;
using NSubstitute;
using NUnit.Framework;
using mockFs = System.IO.Abstractions;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
internal class SysSettingsCommandTests : BaseCommandTests<SysSettingsOptions>
{

	
	private static EnvironmentSettings EnvironmentSettings =>
    		new() {
    			Uri = "https://localhost",
    			Login = "Supervisor",
    			Password = "Supervisor",
    			IsNetCore = false,
    		};

	
	private readonly IContainer _container;
	private readonly mockFs.IFileSystem _fileSystem = TestFileSystem.MockExamplesFolder("deployments-manifest");
		
	public SysSettingsCommandTests(){
		BindingsModule bm = new(_fileSystem);
		_container = bm.Register(EnvironmentSettings);
	}

	[Test(Description = "Describe your test, or ask copilot to describe it for you")]
	public async Task GetSysSettingByCode_Prints_CorrectValue(){
		//Arrange
		
		SysSettingsOptions options = new() {
			IsGet = true,
			Code = "whatever",
		};

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IDataProvider dataProvider = _container.Resolve<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.Resolve<IWorkingDirectoriesProvider>(); 
        IFileSystem filesystem = _container.Resolve<IFileSystem>(); 
		
		
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		string mockValue = "this is sys setting value";
		sysSettingsManager.GetSysSettingValueByCode(options.Code).Returns(mockValue);
			
		SysSettingsCommand sut = new (
			applicationClient, EnvironmentSettings, dataProvider,workingDirectoriesProvider, filesystem, sysSettingsManager);
			
		ILogger logger = Substitute.For<ILogger>();
		sut.Logger = logger;
		
		//Act
		var actual = sut.Execute(options);

		//Assert
		string expectedLogMessage = $"SysSetting {options.Code} : {mockValue}";
		logger.Received(1).WriteInfo(expectedLogMessage);
	}

}