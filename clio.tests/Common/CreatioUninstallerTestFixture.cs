using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Autofac;
using Clio.Common;
using Clio.Requests;
using Clio.Tests.Command;
using Clio.UserEnvironment;
using MediatR;
using NSubstitute;
using NUnit.Framework;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests.Common;

public class CreatioUninstallerTestFixture : BaseClioModuleTests
{

	const string ConnectionStringsFileName = "ConnectionStrings.config";
	#region Constants: Private
	private const string EnvironmentName = "work";
	private const string InstalledCreatioPath = @"C:\inetpub\wwwroot\work";

	#endregion

	#region Fields: Private

	private readonly ISettingsRepository _settingsRepositoryMock = Substitute.For<ISettingsRepository>();
	private ICreatioUninstaller _sut;
	private readonly IMediator _mediatorMock = Substitute.For<IMediator>();
	private readonly ILogger _loggerMock = Substitute.For<ILogger>();

	#endregion

	#region Methods: Private

	
	private Action<IEnumerable<IISScannerHandler.UnregisteredSite>> MockMediator =>allSitesMock=> {
		_mediatorMock.When(i =>
				i.Send(Arg.Any<AllSitesRequest>()))
			.Do(i => {
					AllSitesRequest allSitesRequest = i[0] as AllSitesRequest;
					allSitesRequest?.Callback.Invoke(allSitesMock);
				}
			);
	};
	private void MockNoSitesFound(){
		IEnumerable<IISScannerHandler.UnregisteredSite> allSitesMock = [];
		MockMediator(allSitesMock);
	}
	private void MockStartedSite(string url = ""){
		IEnumerable<IISScannerHandler.UnregisteredSite> allSitesMock = [
			new IISScannerHandler.UnregisteredSite(
				new IISScannerHandler.SiteBinding(EnvironmentName, "Started", "", InstalledCreatioPath),
				[ string.IsNullOrWhiteSpace(url) ? 
					new Uri(EnvironmentSettings.Uri) : new Uri(url)
				],
				IISScannerHandler.SiteType.NetFramework)
		];
		MockMediator(allSitesMock);
	}

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_settingsRepositoryMock).As<ISettingsRepository>();
		containerBuilder.RegisterInstance(_mediatorMock).As<IMediator>();
		containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
	}

	#endregion

	#region Methods: Public

	public override void Setup(){
		EnvironmentSettings = new EnvironmentSettings {
			Uri = "http://kkrylovn.tscrm.com:40090",
			Login = "",
			Password = ""
		};
		base.Setup();
		_settingsRepositoryMock.GetEnvironment(EnvironmentName).Returns(EnvironmentSettings);
		_sut = Container.Resolve<ICreatioUninstaller>();
		FileSystem.AddDirectory(InstalledCreatioPath);
	}

	#endregion

	[Test]
	public void UninstallByEnvironmentName_Exits_WhenNoSiteFoundInIIS(){
		//Arrange
		MockNoSitesFound();

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		_loggerMock.Received(1).WriteWarning("IIS does not have any sites. Nothing to uninstall.");
	}
	
	
	[Test]
	public void UninstallByEnvironmentName_Exits_WhenNoSiteFoundByUrl(){
		//Arrange
		MockStartedSite("https://google.ca");

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		_loggerMock.Received(1).WriteWarning($"Could not find IIS by environment name: {EnvironmentName}");
	}
	
	[Test]
	public void UninstallByEnvironmentName_FindsDirPath(){
		//Arrange
		MockStartedSite();

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		_loggerMock.Received(1).WriteInfo($"Uninstalling Creatio from directory: {InstalledCreatioPath}");
	}

	
	[Test]
	public void UninstallByPath_Returns_When_DirectoryDoesNotExist(){
		//Arrange
		_loggerMock.ClearReceivedCalls();
		const string creatioDirectoryPath = @"C:\random_dir";
		
		//Act
		_sut.UninstallByPath(creatioDirectoryPath);
		
		//Assert
		_loggerMock.Received(1).WriteWarning($"Directory {creatioDirectoryPath} does not exist.");
	}
	
	[Test]
	public void UninstallByPath_Returns_When_ConnectionString_NotExist(){
		
		//Arrange
		_loggerMock.ClearReceivedCalls();
		//MockStartedSite();
		
		//Act
		_sut.UninstallByPath(InstalledCreatioPath);
		
		//Assert
		_loggerMock.Received(1).WriteWarning($"ConnectionStrings file not found in: {InstalledCreatioPath}");
	}
	
	[TestCase("ConnectionStrings_PG")]
	[TestCase("ConnectionStrings_MS")]
	public void UninstallByPath_Returns_When_ConnectionString_Invalid(string fileName){
		//Arrange
		_loggerMock.ClearReceivedCalls();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		var csContent = File.ReadAllText($"Examples/CreatioInstalledDir/{fileName}.config");
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		string dbType = fileName == "ConnectionStrings_PG" ? "PostgreSql" : "MsSql";
		
		//Act
		_sut.UninstallByPath(InstalledCreatioPath);
		
		//Assert
		_loggerMock.Received(1).WriteInfo($"Found db: dbname, Server: {dbType}");
	}
	
}