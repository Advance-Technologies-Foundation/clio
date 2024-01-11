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
	[Ignore("need fix")]
	[Test]
	public void Test(){
		//Arrange
		var logger = Substitute.For<ILogger>();
		var fileSystem = Substitute.For<IFileSystem>();
		var dbClientFactory = Substitute.For<IDbClientFactory>();
		var mssql = Substitute.For<IMssql>();
		var settingsRepository = Substitute.For<ISettingsRepository>();
		string existingDbName = "db-that-exists";
		string newDbName = "new-db-name";
		mssql.CreateDb(newDbName, Arg.Any<string>())
			.Returns(true);
		mssql.CheckDbExists(existingDbName).Returns(true);
		mssql.CheckDbExists(Arg.Is<string>(s=> s!= existingDbName)).Returns(false);
		
		int port = 1433;
		string username = "SA";
		string password = "SA_PASSWORD";
		string host = "127.0.0.1";
		dbClientFactory.CreateMssql(host, port, username, password)
			.Returns(mssql);
		
		RestoreDbCommandOptions options = new() {
			Uri = $"mssql://{username}:{password}@{host}:{port}",
			BackUpFilePath = @"D:\Projects\CreatioProductBuild\8.1.2.2482_Studio_Softkey_MSSQL_ENU\db\BPMonline812Studio.bak",
			DbWorknigFolder = @"\\wsl.localhost\rancher-desktop\mnt\clio-infrastructure\mssql\data",
			DbName = existingDbName
		};
		
		RestoreDbCommand sut = new (logger, fileSystem, dbClientFactory, settingsRepository);
		
		var input = new StringReader(newDbName);
		Console.SetIn(input);
		
		//Act
		var actual = sut.Execute(options);

		//Assert
		actual.Should().Be(0);
	}
}