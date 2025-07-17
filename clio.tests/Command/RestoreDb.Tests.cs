using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.ScenarioHandlers;
using Clio.UserEnvironment;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NUnit.Framework;
using OneOf;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
public class RestoreDbTests : BaseCommandTests<RestoreDbCommandOptions> 
{
	
	[Test]
	public void Test(){
		//Arrange
		ILogger logger = Substitute.For<ILogger>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IDbClientFactory dbClientFactory = Substitute.For<IDbClientFactory>();
		IMssql mssql = Substitute.For<IMssql>();
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		string backUpFilePath = Environment.OSVersion.Platform == PlatformID.Win32NT
			? @"D:\Projects\CreatioProductBuild\8.1.2.2482_Studio_Softkey_MSSQL_ENU\db\BPMonline812Studio.bak"
			: @"~/creatio/8.1.2.2482_Studio_Softkey_MSSQL_ENU/db/BPMonline812Studio.bak";
		const string existingDbName = "db-that-exists";
		const string newDbName = "new-db-name";
		mssql.CreateDb(newDbName, "BPMonline812Studio.bak").Returns(true);
		mssql.CheckDbExists(existingDbName).Returns(true);
		mssql.CheckDbExists(Arg.Is<string>(s=> s!= existingDbName)).Returns(false);
		
		const int port = 1433;
		const string username = "SA";
		const string password = "SA_PASSWORD";
		const string host = "127.0.0.1";
		dbClientFactory.CreateMssql(host, port, username, password).Returns(mssql);
		
		RestoreDbCommandOptions options = new() {
			Uri = $"mssql://{username}:{password}@{host}:{port}",
			BackUpFilePath = backUpFilePath,
			DbWorknigFolder = @"\\wsl.localhost\rancher-desktop\mnt\clio-infrastructure\mssql\data",
			DbName = newDbName
		};
		settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings {
			DbServer = new DbServer {
				Uri = new Uri(options.Uri)
			},
			DbName = options.DbName,
			BackupFilePath = options.BackUpFilePath
		});
		RestoreDbCommand sut = new (logger, fileSystem, dbClientFactory, settingsRepository);
		
		StringReader input = new (newDbName);
		Console.SetIn(input);
		
		//Act
		int actual = sut.Execute(options);

		//Assert
		actual.Should().Be(0);
	}
}