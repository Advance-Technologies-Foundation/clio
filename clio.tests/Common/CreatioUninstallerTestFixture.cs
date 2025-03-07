using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Autofac;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.K8;
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

	#region Constants: Private

	private const string ConnectionStringsFileName = "ConnectionStrings.config";
	private const string EnvironmentName = "work";
	private const string InstalledCreatioPath = @"C:\inetpub\wwwroot\work";

	#endregion

	#region Fields: Private

	private readonly ISettingsRepository _settingsRepositoryMock = Substitute.For<ISettingsRepository>();
	private ICreatioUninstaller _sut;
	private readonly IMediator _mediatorMock = Substitute.For<IMediator>();
	private readonly ILogger _loggerMock = Substitute.For<ILogger>();
	private readonly Ik8Commands _k8CommandsMock = Substitute.For<Ik8Commands>();
	private readonly IMssql _mssqlMock = Substitute.For<IMssql>();
	private readonly IPostgres _postgresMock = Substitute.For<IPostgres>();

	#endregion

	#region Properties: Private

	private Action<IEnumerable<IISScannerHandler.UnregisteredSite>> MockMediator =>
		allSitesMock => {
			_mediatorMock.When(i =>
					i.Send(Arg.Any<AllUnregisteredSitesRequest>()))
				.Do(i => {
						AllUnregisteredSitesRequest allUnregisteredSitesRequest = i[0] as AllUnregisteredSitesRequest;
						allUnregisteredSitesRequest?.Callback.Invoke(allSitesMock);
					}
				);
		};

	#endregion

	#region Methods: Private

	private void MockNoSitesFound(){
		IEnumerable<IISScannerHandler.UnregisteredSite> allSitesMock = [];
		MockMediator(allSitesMock);
	}

	private void MockStartedSite(string url = "", string siteName = EnvironmentName){
		IEnumerable<IISScannerHandler.UnregisteredSite> allSitesMock = [
			new IISScannerHandler.UnregisteredSite(
				new IISScannerHandler.SiteBinding(siteName, "Started", "", InstalledCreatioPath),
				[
					string.IsNullOrWhiteSpace(url) ?
						new Uri(EnvironmentSettings.Uri) : new Uri(url)
				],
				IISScannerHandler.SiteType.NetFramework)
		];
		MockMediator(allSitesMock);
	}

	#endregion

	#region Methods: Protected

	private readonly k8Commands.ConnectionStringParams _cnpMs = new (0, 0, 0, 0, "", "");
	private readonly k8Commands.ConnectionStringParams _cnpPg = new (0, 0, 0, 0, "", "");
	
	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_settingsRepositoryMock).As<ISettingsRepository>();
		containerBuilder.RegisterInstance(_mediatorMock).As<IMediator>();
		containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
		containerBuilder.RegisterInstance(_k8CommandsMock).As<Ik8Commands>();
		containerBuilder.RegisterInstance(_mssqlMock).As<IMssql>();
		containerBuilder.RegisterInstance(_postgresMock).As<IPostgres>();
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
		
		_k8CommandsMock.GetMssqlConnectionString().Returns(_cnpMs);
		_k8CommandsMock.GetPostgresConnectionString().Returns(_cnpPg);
		
		_sut = Container.Resolve<ICreatioUninstaller>();
		FileSystem.AddDirectory(InstalledCreatioPath);
	}

	#endregion

	[Test]
	public void UninstallByEnvironmentName_Exits_WhenNoSiteFoundByUrl(){
		//Arrange
		MockStartedSite("https://google.ca","fake");

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		_loggerMock.Received(1).WriteWarning($"Could not find IIS by environment name: {EnvironmentName}");
	}

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
	public void UninstallByEnvironmentName_FindsDirPath(){
		//Arrange
		MockStartedSite();

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		_loggerMock.Received(1).WriteInfo($"Uninstalling Creatio from directory: {InstalledCreatioPath}");
	}

	[TestCase("ConnectionStrings_PG")]
	[TestCase("ConnectionStrings_MS")]
	public void UninstallByPath_DropsDb(string fileName){
		//Arrange
		MockStartedSite();
		_loggerMock.ClearReceivedCalls();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		string csContent = File.ReadAllText($"Examples/CreatioInstalledDir/{fileName}.config");
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		string dbType = fileName == "ConnectionStrings_PG" ? "PostgreSql" : "MsSql";
		const string  dbNameInFile = "dbname";
		
		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		_loggerMock.Received(1).WriteInfo($"Found db: dbname, Server: {dbType}");

		if (fileName == "ConnectionStrings_PG") {
			_k8CommandsMock.Received(1).GetPostgresConnectionString();
			_postgresMock.Received(1).Init("127.0.0.1", _cnpPg.DbPort, _cnpPg.DbUsername, _cnpPg.DbPassword);
			_postgresMock.Received(1).DropDb(dbNameInFile);
		}
		if (fileName == "ConnectionStrings_MS") {
			_k8CommandsMock.Received(1).GetMssqlConnectionString();
			_mssqlMock.Received(1).Init("127.0.0.1", _cnpMs.DbPort, _cnpMs.DbUsername, _cnpMs.DbPassword);
			_mssqlMock.Received(1).DropDb(dbNameInFile);
		}
	}

	[TestCase("ConnectionStrings_PG")]
	[TestCase("ConnectionStrings_MS")]
	public void UninstallByPath_Returns_When_ConnectionString_Invalid(string fileName){
		//Arrange
		_loggerMock.ClearReceivedCalls();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		string csContent = File.ReadAllText($"Examples/CreatioInstalledDir/{fileName}.config");
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		string dbType = fileName == "ConnectionStrings_PG" ? "PostgreSql" : "MsSql";

		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		_loggerMock.Received(1).WriteInfo($"Found db: dbname, Server: {dbType}");
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

}