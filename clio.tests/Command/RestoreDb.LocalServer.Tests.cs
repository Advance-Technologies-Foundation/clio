using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
[Category("UnitTests")]
[TestFixture]
public class RestoreDbLocalServerTests : BaseCommandTests<RestoreDbCommandOptions>
{
	private ILogger _logger;
	private IFileSystem _fileSystem;
	private IDbClientFactory _dbClientFactory;
	private ISettingsRepository _settingsRepository;
	private ICreatioInstallerService _creatioInstallerService;
	private IDbConnectionTester _dbConnectionTester;
	private IBackupFileDetector _backupFileDetector;
	private IPostgresToolsPathDetector _postgresToolsPathDetector;
	private IProcessExecutor _processExecutor;

	[SetUp]
	public override void Setup() {
		base.Setup();
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<IFileSystem>();
		_dbClientFactory = Substitute.For<IDbClientFactory>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_creatioInstallerService = Substitute.For<ICreatioInstallerService>();
		_dbConnectionTester = Substitute.For<IDbConnectionTester>();
		_backupFileDetector = Substitute.For<IBackupFileDetector>();
		_postgresToolsPathDetector = Substitute.For<IPostgresToolsPathDetector>();
		_processExecutor = Substitute.For<IProcessExecutor>();
	}

	[Test]
	[Description("Should fail when database server configuration doesn't exist in appsettings.json")]
	public void Execute_WhenConfigurationNotFound_ReturnsError() {
		// Arrange
		_settingsRepository.GetLocalDbServer("non-existent").Returns((LocalDbServerConfiguration)null);
		_settingsRepository.GetLocalDbServerNames().Returns(new[] { "my-mssql", "my-postgres" });

		var options = new RestoreDbCommandOptions {
			DbServerName = "non-existent",
			BackupPath = "backup.bak",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because configuration does not exist");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("not found or is disabled")));
	}

	[Test]
	[Description("Should fail when requested local database server configuration is disabled")]
	public void Execute_WhenConfigurationIsDisabled_ReturnsError() {
		// Arrange
		_settingsRepository.GetLocalDbServer("my-disabled-db").Returns((LocalDbServerConfiguration)null);
		_settingsRepository.GetLocalDbServerNames().Returns(new[] { "my-enabled-db" });

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-disabled-db",
			BackupPath = "backup.bak",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "disabled local DB server configuration must not be used for restore");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("not found or is disabled")));
	}

	[Test]
	[Description("Should fail when backup path is not provided")]
	public void Execute_WhenBackupPathMissing_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because backup path is required");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("BackupPath is required")));
	}

	[Test]
	[Description("Should fail when backup file does not exist")]
	public void Execute_WhenBackupFileNotFound_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.bak").Returns(false);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.bak",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because backup file does not exist");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Backup file not found")));
	}

	[Test]
	[Description("Should fail when database name is not provided")]
	public void Execute_WhenDbNameMissing_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.bak").Returns(true);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.bak"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because database name is required");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("DbName is required")));
	}

	[Test]
	[Description("Should test connection before attempting restore")]
	public void Execute_TestsConnectionBeforeRestore() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.bak").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.bak").Returns(BackupFileType.MssqlBackup);

		var mssql = Substitute.For<IMssql>();
		mssql.CheckDbExists("testdb").Returns(false);
		mssql.CreateDb("testdb", "backup.bak").Returns(true);
		_dbClientFactory.CreateMssql("localhost", 1433, "sa", "password").Returns(mssql);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.bak",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		_dbConnectionTester.Received(1).TestConnection(config);
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Testing connection")));
	}

	[Test]
	[Description("Should fail when connection test fails")]
	public void Execute_WhenConnectionFails_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.bak").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult {
			Success = false,
			ErrorMessage = "Connection refused",
			Suggestion = "Check if server is running"
		});

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.bak",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because connection test failed");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Connection test failed")));
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("Check if server is running")));
	}

	[Test]
	[Description("Should detect backup file type automatically")]
	public void Execute_DetectsBackupFileType() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		};

		_settingsRepository.GetLocalDbServer("my-postgres").Returns(config);
		_fileSystem.ExistsFile("backup.backup").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.backup").Returns(BackupFileType.PostgresBackup);
		_postgresToolsPathDetector.GetPgRestorePath(null).Returns("/usr/bin/pg_restore");

		var postgres = new Postgres();
		postgres.Init("localhost", 5432, "postgres", "postgres");
		_dbClientFactory.CreatePostgres("localhost", 5432, "postgres", "postgres").Returns(postgres);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-postgres",
			BackupPath = "backup.backup",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		sut.Execute(options);

		// Assert
		_backupFileDetector.Received(1).DetectBackupType("backup.backup");
	}

	[Test]
	[Description("Should fail when backup type cannot be determined")]
	public void Execute_WhenBackupTypeUnknown_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.unknown").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.unknown").Returns(BackupFileType.Unknown);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.unknown",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because backup type is unknown");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Cannot determine backup file type")));
	}

	[Test]
	[Description("Should fail when backup type doesn't match database type")]
	public void Execute_WhenBackupTypeMismatch_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.backup").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.backup").Returns(BackupFileType.PostgresBackup);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.backup",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because PostgreSQL backup cannot be restored to MSSQL server");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("not compatible")));
	}

	[Test]
	[Description("Should successfully restore MSSQL backup to local server")]
	public void Execute_RestoresMssqlBackupSuccessfully() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.bak").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.bak").Returns(BackupFileType.MssqlBackup);

		var mssql = Substitute.For<IMssql>();
		mssql.CheckDbExists("testdb").Returns(false);
		mssql.CreateDb("testdb", "backup.bak").Returns(true);
		_dbClientFactory.CreateMssql("localhost", 1433, "sa", "password").Returns(mssql);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.bak",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0, "because restore should succeed");
		mssql.Received(1).CreateDb("testdb", "backup.bak");
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Successfully restored")));
	}

	[Test]
	[Description("Should drop existing database before restore")]
	public void Execute_DropsExistingDatabaseBeforeRestore() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.bak").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.bak").Returns(BackupFileType.MssqlBackup);

		var mssql = Substitute.For<IMssql>();
		mssql.CheckDbExists("testdb").Returns(true);
		mssql.CreateDb("testdb", "backup.bak").Returns(true);
		_dbClientFactory.CreateMssql("localhost", 1433, "sa", "password").Returns(mssql);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.bak",
			DbName = "testdb",
			DropIfExists = true
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0, "because restore should succeed");
		mssql.Received(1).DropDb("testdb");
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("already exists")));
	}

	[Test]
	[Description("Should fail when database exists and drop-if-exists flag is not set")]
	public void Execute_WhenDatabaseExistsAndDropIfExistsNotSet_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "password"
		};

		_settingsRepository.GetLocalDbServer("my-mssql").Returns(config);
		_fileSystem.ExistsFile("backup.bak").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.bak").Returns(BackupFileType.MssqlBackup);

		var mssql = Substitute.For<IMssql>();
		mssql.CheckDbExists("testdb").Returns(true);
		_dbClientFactory.CreateMssql("localhost", 1433, "sa", "password").Returns(mssql);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-mssql",
			BackupPath = "backup.bak",
			DbName = "testdb",
			DropIfExists = false
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because database already exists and drop-if-exists flag is not set");
		mssql.DidNotReceive().DropDb(Arg.Any<string>());
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("already exists") && s.Contains("--drop-if-exists")));
	}

	[Test]
	[Description("Should drop existing PostgreSQL database with drop-if-exists flag")]
	public void Execute_DropsExistingPostgresDatabase_WithDropIfExistsFlag() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		};

		_settingsRepository.GetLocalDbServer("my-postgres").Returns(config);
		_fileSystem.ExistsFile("backup.backup").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.backup").Returns(BackupFileType.PostgresBackup);
		_postgresToolsPathDetector.GetPgRestorePath(null).Returns("/usr/bin/pg_restore");

		var postgres = Substitute.For<Postgres>();
		postgres.CheckDbExists("testdb").Returns(true);
		postgres.CreateDb("testdb").Returns(true);
		postgres.DropDb("testdb").Returns(true);
		_dbClientFactory.CreatePostgres("localhost", 5432, "postgres", "postgres").Returns(postgres);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-postgres",
			BackupPath = "backup.backup",
			DbName = "testdb",
			DropIfExists = true
		};

		var sut = CreateSut();

		// Act
		sut.Execute(options);

		// Assert
		postgres.Received(1).DropDb("testdb");
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("Dropping existing database")));
	}

	[Test]
	[Description("Should fail when PostgreSQL database exists and drop-if-exists flag is not set")]
	public void Execute_WhenPostgresDatabaseExistsAndDropIfExistsNotSet_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		};

		_settingsRepository.GetLocalDbServer("my-postgres").Returns(config);
		_fileSystem.ExistsFile("backup.backup").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.backup").Returns(BackupFileType.PostgresBackup);
		_postgresToolsPathDetector.GetPgRestorePath(null).Returns("/usr/bin/pg_restore");

		var postgres = Substitute.For<Postgres>();
		postgres.CheckDbExists("testdb").Returns(true);
		_dbClientFactory.CreatePostgres("localhost", 5432, "postgres", "postgres").Returns(postgres);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-postgres",
			BackupPath = "backup.backup",
			DbName = "testdb",
			DropIfExists = false
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because database already exists and drop-if-exists flag is not set");
		postgres.DidNotReceive().DropDb(Arg.Any<string>());
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("already exists") && s.Contains("--drop-if-exists")));
	}

	[Test]
	[Description("Should fail when pg_restore is not available for PostgreSQL restore")]
	public void Execute_WhenPgRestoreNotAvailable_ReturnsError() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		};

		_settingsRepository.GetLocalDbServer("my-postgres").Returns(config);
		_fileSystem.ExistsFile("backup.backup").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.backup").Returns(BackupFileType.PostgresBackup);
		_postgresToolsPathDetector.GetPgRestorePath(null).Returns((string)null);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-postgres",
			BackupPath = "backup.backup",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, "because pg_restore is not available");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("pg_restore not found")));
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("https://www.postgresql.org/download/")));
	}

	[Test]
	[Description("Should use explicit pgToolsPath when provided")]
	public void Execute_UsesExplicitPgToolsPath() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres",
			PgToolsPath = "/custom/path/to/bin"
		};

		_settingsRepository.GetLocalDbServer("my-postgres").Returns(config);
		_fileSystem.ExistsFile("backup.backup").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.backup").Returns(BackupFileType.PostgresBackup);
		_postgresToolsPathDetector.GetPgRestorePath("/custom/path/to/bin").Returns("/custom/path/to/bin/pg_restore");

		var postgres = new Postgres();
		postgres.Init("localhost", 5432, "postgres", "postgres");
		_dbClientFactory.CreatePostgres("localhost", 5432, "postgres", "postgres").Returns(postgres);

		var options = new RestoreDbCommandOptions {
			DbServerName = "my-postgres",
			BackupPath = "backup.backup",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		sut.Execute(options);

		// Assert
		_postgresToolsPathDetector.Received(1).GetPgRestorePath("/custom/path/to/bin");
	}

	[Test]
	[Description("Should maintain backward compatibility when dbServerName is not specified")]
	public void Execute_BackwardCompatibility_WorksWithoutDbServerName() {
		// Arrange
		var mssql = Substitute.For<IMssql>();
		mssql.CreateDb("testdb", "backup.bak").Returns(true);
		mssql.CheckDbExists("testdb").Returns(false);

		_dbClientFactory.CreateMssql("localhost", 1433, "sa", "password").Returns(mssql);
		_fileSystem.ExistsFile("backup.bak").Returns(true);

		_settingsRepository.GetEnvironment(Arg.Any<RestoreDbCommandOptions>()).Returns(new EnvironmentSettings {
			DbServer = new DbServer {
				Uri = new System.Uri("mssql://sa:password@localhost:1433")
			},
			DbName = "testdb",
			BackupFilePath = "backup.bak"
		});

		var options = new RestoreDbCommandOptions {
			// No DbServerName specified - should use old behavior
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0, "because backward compatibility should work");
	}

	[Test]
	[Description("Should show Docker port mapping hint when PostgreSQL local connection test fails")]
	public void Execute_WhenPostgresConnectionFails_ShowsDockerHint() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5433,
			Username = "postgres",
			Password = "postgres"
		};

		_settingsRepository.GetLocalDbServer("docker-postgres").Returns(config);
		_fileSystem.ExistsFile("backup.backup").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult {
			Success = false,
			ErrorMessage = "Connection refused",
			Suggestion = "Check if PostgreSQL is reachable"
		});

		var options = new RestoreDbCommandOptions {
			DbServerName = "docker-postgres",
			BackupPath = "backup.backup",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "restore must stop when PostgreSQL connectivity validation fails");
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("docker ps") && s.Contains("published host port")));
	}

	[Test]
	[Description("Should restore PostgreSQL to a configured local server by using local pg_restore and not the Kubernetes restore service")]
	public void Execute_WhenPostgresLocalServerConfigured_UsesLocalPgRestoreInsteadOfKubernetesRestore() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5433,
			Username = "postgres",
			Password = "postgres"
		};

		_settingsRepository.GetLocalDbServer("docker-postgres").Returns(config);
		_fileSystem.ExistsFile("backup.backup").Returns(true);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType("backup.backup").Returns(BackupFileType.PostgresBackup);
		_postgresToolsPathDetector.GetPgRestorePath(null).Returns("/usr/bin/pg_restore");
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 }));

		var postgres = Substitute.For<Postgres>();
		postgres.CheckDbExists("testdb").Returns(false);
		postgres.CreateDb("testdb").Returns(true);
		_dbClientFactory.CreatePostgres("localhost", 5433, "postgres", "postgres").Returns(postgres);

		var options = new RestoreDbCommandOptions {
			DbServerName = "docker-postgres",
			BackupPath = "backup.backup",
			DbName = "testdb"
		};

		var sut = CreateSut();

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "configured local PostgreSQL restore should run through pg_restore successfully");
		_processExecutor.Received(1).ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o => o.Arguments.Contains("-v --no-owner --no-privileges") && o.Arguments.Contains("\"backup.backup\"")));
		_creatioInstallerService.DidNotReceive().DoPgWork(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Running local pg_restore against localhost:5433")));
	}

	[Test]
	[Description("Should extract PostgreSQL backup from zip and restore it locally without using Kubernetes restore service")]
	public void Execute_WhenPostgresBackupIsInZip_ExtractsAndUsesLocalPgRestore() {
		// Arrange
		var config = new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5433,
			Username = "postgres",
			Password = "postgres"
		};
		string tempRoot = Path.Combine(Path.GetTempPath(), $"clio-restore-db-test-{Guid.NewGuid():N}");
		string packageRoot = Path.Combine(tempRoot, "package");
		string dbDirectory = Path.Combine(packageRoot, "db");
		Directory.CreateDirectory(dbDirectory);
		string backupFilePath = Path.Combine(dbDirectory, "sample.backup");
		File.WriteAllText(backupFilePath, "backup");
		string zipPath = Path.Combine(tempRoot, "sample.zip");
		System.IO.Compression.ZipFile.CreateFromDirectory(packageRoot, zipPath);

		try {
			_settingsRepository.GetLocalDbServer("docker-postgres").Returns(config);
			_fileSystem.ExistsFile(zipPath).Returns(true);
			_fileSystem.When(x => x.CreateDirectory(Arg.Any<string>())).Do(callInfo => Directory.CreateDirectory(callInfo.Arg<string>()));
			_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
			_backupFileDetector.DetectBackupType(Arg.Any<string>()).Returns(BackupFileType.PostgresBackup);
			_postgresToolsPathDetector.GetPgRestorePath(null).Returns("/usr/bin/pg_restore");
			_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
				.Returns(Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 }));

			var postgres = Substitute.For<Postgres>();
			postgres.CheckDbExists("testdb").Returns(false);
			postgres.CreateDb("testdb").Returns(true);
			_dbClientFactory.CreatePostgres("localhost", 5433, "postgres", "postgres").Returns(postgres);

			var options = new RestoreDbCommandOptions {
				DbServerName = "docker-postgres",
				BackupPath = zipPath,
				DbName = "testdb"
			};

			var sut = CreateSut();

			// Act
			int result = sut.Execute(options);

			// Assert
			result.Should().Be(0, because: "zip-based PostgreSQL backups should be extracted locally and restored via pg_restore");
			_processExecutor.Received(1).ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
				o.Arguments.Contains("-v --no-owner --no-privileges") &&
				o.Arguments.Contains(".backup\"")));
			_creatioInstallerService.DidNotReceive().DoPgWork(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		} finally {
			if (Directory.Exists(tempRoot)) {
				Directory.Delete(tempRoot, true);
			}
		}
	}
	private RestoreDbCommand CreateSut() {
		return new RestoreDbCommand(
			_logger,
			_fileSystem,
			_dbClientFactory,
			_settingsRepository,
			_creatioInstallerService,
			_dbConnectionTester,
			_backupFileDetector,
			_postgresToolsPathDetector,
			_processExecutor
		);
	}
}






