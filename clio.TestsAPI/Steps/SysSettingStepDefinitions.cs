using System.Diagnostics;
using System.Reflection;
using ATF.Repository;
using ATF.Repository.Providers;
using CreatioModel;
using FluentAssertions;
using NUnit.Framework;

namespace clio.ApiTest.Steps;

[Binding]
public class SysSettingStepDefinitions
{

	private readonly AppSettings _appSettings;
	private readonly IDataProvider _dataProvider;

	private IAppDataContext DataContext =>AppDataContextFactory.GetAppDataContext(_dataProvider);
	public SysSettingStepDefinitions(AppSettings appSettings, IDataProvider dataProvider){
		_appSettings = appSettings;
		_dataProvider = dataProvider;
	}
	private string _whenOutput = string.Empty;
	private string _thenOutput = string.Empty;
	
	[When(@"command is run with ""(.*)"" ""(.*)""")]
	[Then(@"command is run with ""(.*)"" ""(.*)""")]
	public void WhenCommandIsRunWith(string commandName, string clioArgs){
		
		if(clioArgs.Contains("--GET")) {
			_thenOutput = RunClioCommand(commandName, clioArgs);
		}else {
			_whenOutput = RunClioCommand(commandName, clioArgs);
		}
	}
	
	
	private string RunClioCommand(string commandName, string clioArgs){
		
		// D:\Projects\clio-proj\clio\clio.TestsAPI\bin\Debug\net8.0\clio.TestsAPI.dll
		
		string mainLocation = Assembly.GetExecutingAssembly().Location;
		string mainLocationDirPath = Path.GetDirectoryName(mainLocation);
		string clioDevPath = Path.Combine(mainLocationDirPath,"..","..","..","..","clio","bin","Debug","net6.0", "clio.exe");
		
		var url = _appSettings.URL;
		var username = _appSettings.LOGIN;
		var password = _appSettings.PASSWORD;
		var isNetCore = _appSettings.IS_NETCORE;
		string envArgs = $"-u {url} -l {username} -p {password} -i {isNetCore}";
		
		
		ProcessStartInfo psi = new () {
			FileName = clioDevPath,
			Arguments = $"{commandName} {clioArgs} {envArgs}",
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		Process? process = Process.Start(psi);
		if(process is null) {
			Assert.Fail("Could not start clio-dev process");
		}
		process!.WaitForExit();
		return process.StandardOutput.ReadToEnd();
	}
	

	[Then(@"the output should be \[INF] - SysSetting ""(.*)"" : ""(.*)""")]
	public void ThenTheOutputShouldBeInfSysSettingClioText(string expectedName, string expectedValue){
		//[INF] - SysSetting ClioBooleanFour : "False"
		string actual = _thenOutput.Trim().Replace("\"","");
		string expected = $"[INF] - SysSettings {expectedName} : {expectedValue}";
		actual.Should().Be(expected,"values should match");
	}

	[Then(@"SysSetting exists in Creatio with ""(.*)"" ""(.*)""")]
	public void ThenSysSettingExistsInCreatioWith(string sysSettingName, string valueNameType){
		List<VwSysSetting> settings = DataContext.Models<VwSysSetting>()
			.Where(s=> s.Code == sysSettingName)
			.ToList();
		
		settings.Should().HaveCount(1);
		VwSysSetting actual = settings.First();
		actual.Name.Should().Be(sysSettingName);
		actual.Code.Should().Be(sysSettingName);
		actual.ValueTypeName.Should().Be(valueNameType);
		actual.IsCacheable.Should().BeTrue();
		actual.IsPersonal.Should().BeFalse();
	}

}