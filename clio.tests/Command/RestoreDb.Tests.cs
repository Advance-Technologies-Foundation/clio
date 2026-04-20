using System;
using System.IO;
using System.IO.Compression;
using Clio.Command;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.db;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("Unit")]
[TestFixture]
[Property("Module", "Command")]
public class RestoreDbTests : BaseCommandTests<RestoreDbCommandOptions>
{
	[Test]
	[Description("Restores an MSSQL database by using the configured environment and legacy direct restore flow.")]
	public void Execute_WhenUsingLegacyEnvironmentRestore_RestoresMssqlDatabase() {
		// Arrange
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
		mssql.RestoreDatabase(newDbName, "BPMonline812Studio.bak", Arg.Any<Action<string>>())
			.Returns(new DatabaseRestoreResult(true, Array.Empty<string>()));
		mssql.CheckDbExists(existingDbName).Returns(true);
		mssql.CheckDbExists(Arg.Is<string>(dbName => dbName != existingDbName)).Returns(false);

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
		RestoreDbCommand sut = new(
			logger,
			fileSystem,
			new System.IO.Abstractions.FileSystem(),
			dbClientFactory,
			settingsRepository,
			Substitute.For<ICreatioInstallerService>(),
			Substitute.For<IDbConnectionTester>(),
			Substitute.For<IBackupFileDetector>(),
			Substitute.For<IPostgresToolsPathDetector>(),
			Substitute.For<IProcessExecutor>());

		StringReader input = new(newDbName);
		Console.SetIn(input);

		// Act
		int actual = sut.Execute(options);

		// Assert
		actual.Should().Be(0, because: "the existing legacy MSSQL restore mode must remain backward compatible");
	}

	[Test]
	[Description("Restores a PostgreSQL ZIP backup without dbServerName by extracting the archive and delegating to the PostgreSQL restore flow.")]
	public void Execute_WhenZipBackupProvidedWithoutDbServerName_RestoresFromExtractedBackup() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IDbClientFactory dbClientFactory = Substitute.For<IDbClientFactory>();
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ICreatioInstallerService creatioInstallerService = Substitute.For<ICreatioInstallerService>();
		string tempRoot = Path.Combine(Path.GetTempPath(), $"restore-db-zip-{Guid.NewGuid():N}");
		string packageRoot = Path.Combine(tempRoot, "package");
		string dbDirectory = Path.Combine(packageRoot, "db");
		Directory.CreateDirectory(dbDirectory);
		string backupFilePath = Path.Combine(dbDirectory, "database.backup");
		File.WriteAllText(backupFilePath, "backup");
		string zipPath = Path.Combine(tempRoot, "package.zip");
		ZipFile.CreateFromDirectory(packageRoot, zipPath);
		fileSystem
			.When(fs => fs.CreateDirectory(Arg.Any<string>()))
			.Do(callInfo => Directory.CreateDirectory(callInfo.Arg<string>()));
		creatioInstallerService.DoPgWork(Arg.Any<string>(), "restored_db", "package").Returns(0);

		RestoreDbCommandOptions options = new() {
			BackupPath = zipPath,
			DbName = "restored_db"
		};
		RestoreDbCommand sut = new(
			logger,
			fileSystem,
			new System.IO.Abstractions.FileSystem(),
			dbClientFactory,
			settingsRepository,
			creatioInstallerService,
			Substitute.For<IDbConnectionTester>(),
			Substitute.For<IBackupFileDetector>(),
			Substitute.For<IPostgresToolsPathDetector>(),
			Substitute.For<IProcessExecutor>());

		try {
			// Act
			int actual = sut.Execute(options);

			// Assert
			actual.Should().Be(0, because: "ZIP-based PostgreSQL restores should work without requiring dbServerName");
			creatioInstallerService.Received(1).DoPgWork(Arg.Any<string>(), "restored_db", "package");
			creatioInstallerService.Received(1).TryDisableForcedPasswordReset(
				Arg.Is<PfInstallerOptions>(o => o.SiteName == "restored_db" && o.ZipFile == zipPath &&
					o.DisableResetPassword),
				InstallerHelper.DatabaseType.Postgres);
			settingsRepository.DidNotReceive().GetEnvironment(Arg.Any<EnvironmentOptions>());
		}
		finally {
			if (Directory.Exists(tempRoot)) {
				Directory.Delete(tempRoot, true);
			}
		}
	}

	[Test]
	[Description("Creates only a PostgreSQL template from a ZIP backup without requiring dbName or dbServerName.")]
	public void Execute_WhenAsTemplateIsSetForZipBackup_CreatesTemplateOnly() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IDbClientFactory dbClientFactory = Substitute.For<IDbClientFactory>();
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ICreatioInstallerService creatioInstallerService = Substitute.For<ICreatioInstallerService>();
		string tempRoot = Path.Combine(Path.GetTempPath(), $"restore-db-template-{Guid.NewGuid():N}");
		string packageRoot = Path.Combine(tempRoot, "package");
		string dbDirectory = Path.Combine(packageRoot, "db");
		Directory.CreateDirectory(dbDirectory);
		string backupFilePath = Path.Combine(dbDirectory, "database.backup");
		File.WriteAllText(backupFilePath, "backup");
		string zipPath = Path.Combine(tempRoot, "template-source.zip");
		ZipFile.CreateFromDirectory(packageRoot, zipPath);
		fileSystem
			.When(fs => fs.CreateDirectory(Arg.Any<string>()))
			.Do(callInfo => Directory.CreateDirectory(callInfo.Arg<string>()));
		creatioInstallerService.EnsurePgTemplateAndGetName(Arg.Any<string>(), "template-source", true)
			.Returns("template_actual");

		RestoreDbCommandOptions options = new() {
			BackupPath = zipPath,
			AsTemplate = true,
			DropIfExists = true
		};
		RestoreDbCommand sut = new(
			logger,
			fileSystem,
			new System.IO.Abstractions.FileSystem(),
			dbClientFactory,
			settingsRepository,
			creatioInstallerService,
			Substitute.For<IDbConnectionTester>(),
			Substitute.For<IBackupFileDetector>(),
			Substitute.For<IPostgresToolsPathDetector>(),
			Substitute.For<IProcessExecutor>());

		try {
			// Act
			int actual = sut.Execute(options);

			// Assert
			actual.Should().Be(0, because: "--as-template should allow ZIP-based PostgreSQL template creation without dbName");
			creatioInstallerService.Received(1).EnsurePgTemplateAndGetName(Arg.Any<string>(), "template-source", true);
			creatioInstallerService.Received(1).TryDisableForcedPasswordReset(
				Arg.Is<PfInstallerOptions>(o => o.SiteName == "template_actual" && o.ZipFile == zipPath &&
					o.DisableResetPassword),
				InstallerHelper.DatabaseType.Postgres);
			creatioInstallerService.DidNotReceive().DoPgWork(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
			settingsRepository.DidNotReceive().GetEnvironment(Arg.Any<EnvironmentOptions>());
		}
		finally {
			if (Directory.Exists(tempRoot)) {
				Directory.Delete(tempRoot, true);
			}
		}
	}
}
