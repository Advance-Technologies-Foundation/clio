using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Common;
using Clio.Requests;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
public class AppCmdTests
{
	
	[Test]
	public void AppCmd_One(){
		
		//Well this is a hack, but it works
		AppCmd.Appcmd = args => args switch{
			@"list sites /xml" =>
				"""
				 <appcmd>
				    <SITE SITE.NAME="Work_3778" SITE.ID="1" bindings="http/*:40090:kkrylovn.tscrm.com" state="Stopped" />
				    <SITE SITE.NAME="MarketplaceGPT" SITE.ID="2" bindings="http/*:40001:kkrylovn.tscrm.com" state="Stopped" />
				    <SITE SITE.NAME="studio-812" SITE.ID="3" bindings="http/*:40010:kkrylovn.tscrm.com" state="Started" />
				</appcmd>
				""",
			@"list VDIR Work_3778/ /text:physicalPath" => """D:\Projects\inetpub\wwwroot\Work_3778""",
			@"list VDIR MarketplaceGPT/ /text:physicalPath" => """D:\Projects\inetpub\wwwroot\MarketplaceGPT""",
			@"list VDIR studio-812/ /text:physicalPath" => """D:\Projects\inetpub\wwwroot\studio-812""",
			var _ => ""
		};

		//Mock empty FS
		IDictionary<string, MockFileData> _mockFileData = new Dictionary<string, MockFileData>();
		MockFileSystem mockFs = new MockFileSystem(_mockFileData);
		
		//Add Directories
		mockFs.Directory.CreateDirectory(@"D:\Projects\inetpub\wwwroot\Work_3778");
		mockFs.Directory.CreateDirectory(@"D:\Projects\inetpub\wwwroot\Work_3778\Terrasoft.WebApp"); //Makes site NetFramework
		StreamWriter sw1 = mockFs.File.CreateText(Path.Join(@"D:\Projects\inetpub\wwwroot\Work_3778","ConnectionStrings.config"));
		
		sw1.Write("""
				<?xml version="1.0" encoding="utf-8"?>
				<connectionStrings>
				  <add name="redis" connectionString="host=localhost;db=90;port=6379" />
				  <add name="defPackagesWorkingCopyPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\TerrasoftPackages" />
				  <add name="tempDirectoryPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\" />
				  <add name="sourceControlAuthPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\Svn" />
				  <add name="elasticsearchCredentials" connectionString="User=gs-es; Password=DEQpJMfKqUVTWg9wYVgi;" />
				  <add name="influx" connectionString="url=http://10.0.7.161:30359; user=; password=; batchIntervalMs=5000" />
				  <add name="messageBroker" connectionString="amqp://guest:guest@localhost/BPMonlineSolution" />
				  <add name="db" connectionString="Data Source=localhost;Initial Catalog=WorkBuild.8.1.1;User Id=SA;Password=$Zarelon01$Zarelon01;MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100" />
				  <add name="schedulerDb" connectionString="Data Source=localhost;Initial Catalog=WorkBuild.8.1.1;User Id=SA;Password=$Zarelon01$Zarelon01;MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100" />
				</connectionStrings>
				""");
		sw1.Flush();
		
		
		mockFs.Directory.CreateDirectory(@"D:\Projects\inetpub\wwwroot\MarketplaceGPT");
		mockFs.Directory.CreateDirectory(@"D:\Projects\inetpub\wwwroot\MarketplaceGPT\Terrasoft.Configuration"); //Makes site .NetCore
		StreamWriter sw2 = mockFs.File.CreateText(Path.Join(@"D:\Projects\inetpub\wwwroot\MarketplaceGPT","ConnectionStrings.config"));
		sw2.Write("""
				<?xml version="1.0" encoding="utf-8"?>
				<connectionStrings>
				  <add name="db" connectionString="Server=127.0.0.1;Port=5432;Database=MarketplaceGPT;User ID=postgres;password=root;Timeout=500; CommandTimeout=400;MaxPoolSize=1024;" />
				  <add name="dbPostgreSql" connectionString="Server=127.0.0.1;Port=5432;Database=MarketplaceGPT;User ID=postgres;password=root;Timeout=500; CommandTimeout=400;MaxPoolSize=1024;" />
				  <add name="redis" connectionString="host=127.0.0.1;db=1;port=6379" />
				  <add name="dbMssqlCore" connectionString="Data Source=tscore-ms-01\mssql2008; Initial Catalog=BPMonlineCore; Persist Security Info=True; MultipleActiveResultSets=True; Integrated Security=SSPI; Pooling = true; Max Pool Size = 100; Async = true" />
				  <add name="dbMssqlUnitTest" connectionString="Data Source=TSAppHost-02; Initial Catalog=BPMonlineUnitTest; Persist Security Info=True; MultipleActiveResultSets=True; User ID=UnitTest; Password=UnitTest; Async = true" />
				  <add name="tempDirectoryPath" connectionString="%TEMP%/%USER%/%APPLICATION%" />
				  <add name="consumerInfoServiceUri" connectionString="http://sso.bpmonline.com:4566/ConsumerInfoService.svc" />
				  <add name="consumerInfoServiceAccessInfoPageUri" connectionString="http://sso.bpmonline.com:4566/AccessInfoPage.aspx" />
				  <add name="logstashConfigFolderPath" connectionString="%TEMP%\%APPLICATION%\LogstashConfig" />
				  <add name="elasticsearchCredentials" connectionString="User=gs-es; Password=DEQpJMfKqUVTWg9wYVgi;" />
				  <add name="influx" connectionString="url=http://10.0.7.161:30359; user=; password=; batchIntervalMs=5000" />
				  <add name="clientPerformanceLoggerServiceUri" connectionString="http://tsbuild-k8s-m1:30001/" />
				  <add name="messageBroker" connectionString="amqp://guest:guest@localhost/BPMonlineSolution" />
				</connectionStrings>
				""");
		sw2.Flush();
		
		mockFs.Directory.CreateDirectory(@"D:\Projects\inetpub\wwwroot\studio-812");
		mockFs.Directory.CreateDirectory(@"D:\Projects\inetpub\wwwroot\studio-812\Terrasoft.WebApp\Terrasoft.Configuration");
		StreamWriter sw3 = mockFs.File.CreateText(Path.Join(@"D:\Projects\inetpub\wwwroot\studio-812","ConnectionStrings.config"));
		sw3.Write("""
				<?xml version="1.0" encoding="utf-8"?>
				<connectionStrings>
				  <add name="redis" connectionString="host=localhost;db=90;port=6379" />
				  <add name="defPackagesWorkingCopyPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\TerrasoftPackages" />
				  <add name="tempDirectoryPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\" />
				  <add name="sourceControlAuthPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\Svn" />
				  <add name="elasticsearchCredentials" connectionString="User=gs-es; Password=DEQpJMfKqUVTWg9wYVgi;" />
				  <add name="influx" connectionString="url=http://10.0.7.161:30359; user=; password=; batchIntervalMs=5000" />
				  <add name="messageBroker" connectionString="amqp://guest:guest@localhost/BPMonlineSolution" />
				  <add name="db" connectionString="Data Source=localhost;Initial Catalog=studio-812;User Id=SA;Password=$Zarelon01$Zarelon01;MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100" />
				  <add name="schedulerDb" connectionString="Data Source=localhost;Initial Catalog=WorkBuild.8.1.1;User Id=SA;Password=$Zarelon01$Zarelon01;MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100" />
				</connectionStrings>
				""");
		sw3.Flush();
		
		//Create FileSystem
		IFileSystem fs = new FileSystem(mockFs);
		
		//Create System under test
		AppCmd appCmd = new (fs);
		
		//Act
		IEnumerable<AppCmd.ScannedSite> sites = appCmd.GetAllSites();

		List<AppCmd.ScannedSite> scannedSites = sites.ToList();
		scannedSites.Should().NotBeNull();
		
		scannedSites[0].Name.Should().Be("Work_3778");
		scannedSites[0].SiteType.Should().Be(IISScannerHandler.SiteType.NetFramework);
		scannedSites[0].Urls.Should().Contain(new Uri("http://kkrylovn.tscrm.com:40090"));
		scannedSites[0].DbType.Should().Be(CreatioDBType.MSSQL);
		
		scannedSites[1].Name.Should().Be("MarketplaceGPT");
		scannedSites[1].SiteType.Should().Be(IISScannerHandler.SiteType.Core);
		scannedSites[1].Urls.Should().Contain(new Uri("http://kkrylovn.tscrm.com:40001"));
		scannedSites[1].DbType.Should().Be(CreatioDBType.PostgreSQL);
		
		scannedSites[2].Name.Should().Be("studio-812");
		scannedSites[2].SiteType.Should().Be(IISScannerHandler.SiteType.NetFramework);
		scannedSites[2].Urls.Should().Contain(new Uri("http://kkrylovn.tscrm.com:40010"));
		scannedSites[2].DbType.Should().Be(CreatioDBType.MSSQL);
	}

}