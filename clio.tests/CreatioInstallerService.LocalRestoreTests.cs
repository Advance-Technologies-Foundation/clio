using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Reflection;
using System.Threading.Tasks;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.db;
using Clio.UserEnvironment;
using Clio.Tests.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
internal class CreatioInstallerServiceLocalRestoreTests : BaseClioModuleTests
{
	private static readonly string UnzippedDirectory = OperatingSystem.IsWindows() ? @"C:\unzipped" : "/unzipped";
	private static readonly string BackupFilePath = Path.Combine(UnzippedDirectory, "db", "sample.backup");

	private CreatioInstallerService _sut;
	private ILogger _logger;
	private ISettingsRepository _settingsRepository;
	private IDbConnectionTester _dbConnectionTester;
	private IBackupFileDetector _backupFileDetector;
	private IPostgresToolsPathDetector _postgresToolsPathDetector;
	private IDbClientFactory _dbClientFactory;
	private IProcessExecutor _processExecutor;

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData> {
			[BackupFilePath] = new("backup")
		});
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_dbConnectionTester = Substitute.For<IDbConnectionTester>();
		_backupFileDetector = Substitute.For<IBackupFileDetector>();
		_postgresToolsPathDetector = Substitute.For<IPostgresToolsPathDetector>();
		_dbClientFactory = Substitute.For<IDbClientFactory>();
		_processExecutor = Substitute.For<IProcessExecutor>();

		_settingsRepository.GetIISClioRootPath().Returns(OperatingSystem.IsWindows() ? @"C:\inetpub\wwwroot\clio" : "/tmp/clio-iis");
		_settingsRepository.GetCreatioProductsFolder().Returns(OperatingSystem.IsWindows() ? @"C:\CreatioProductBuild" : "/tmp/creatio-products");
		_settingsRepository.GetRemoteArtefactServerPath().Returns(OperatingSystem.IsWindows() ? @"C:\builds" : "/tmp/builds");

		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_dbConnectionTester);
		containerBuilder.AddSingleton(_backupFileDetector);
		containerBuilder.AddSingleton(_postgresToolsPathDetector);
		containerBuilder.AddSingleton(_dbClientFactory);
		containerBuilder.AddSingleton(_processExecutor);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<CreatioInstallerService>();
	}

	[Test]
	[Description("Should restore PostgreSQL for deploy-creatio local mode by using local pg_restore against the configured host port")]
	public void RestoreToLocalDb_WhenPostgresServerConfigured_UsesLocalPgRestore() {
		// Arrange
		LocalDbServerConfiguration config = CreateDockerPostgresConfig();
		Postgres postgres = Substitute.For<Postgres>();

		_settingsRepository.GetLocalDbServer("docker-postgres").Returns(config);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType(BackupFilePath).Returns(BackupFileType.PostgresBackup);
		_postgresToolsPathDetector.GetPgRestorePath(null).Returns("/usr/bin/pg_restore");
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 }));
		postgres.FindTemplateBySourceFile("app").Returns((string)null);
		postgres.CreateDb(Arg.Any<string>()).Returns(true);
		postgres.SetDatabaseAsTemplate(Arg.Any<string>()).Returns(true);
		postgres.SetDatabaseComment(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		postgres.CheckDbExists("restored-db").Returns(false);
		postgres.CreateDbFromTemplate(Arg.Any<string>(), "restored-db").Returns(true);
		_dbClientFactory.CreatePostgres("localhost", 5433, "postgres", "postgres").Returns(postgres);

		MethodInfo method = GetRestoreToLocalDbMethod();

		// Act
		int result = (int)method.Invoke(_sut, new object[] { UnzippedDirectory, "restored-db", "docker-postgres", false, "app.zip" })!;

		// Assert
		result.Should().Be(0, because: "deploy-creatio local PostgreSQL restore should complete through local pg_restore");
		_processExecutor.Received(1).ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o => o.Arguments.Contains("-v --no-owner --no-privileges") && o.Arguments.Contains(BackupFilePath)));
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Running local pg_restore against localhost:5433")));
	}

	[Test]
	[Description("Should reuse existing PostgreSQL template for deploy-creatio local mode without running pg_restore again")]
	public void RestoreToLocalDb_WhenTemplateAlreadyExists_SkipsPgRestore() {
		// Arrange
		LocalDbServerConfiguration config = CreateDockerPostgresConfig();
		Postgres postgres = Substitute.For<Postgres>();

		_settingsRepository.GetLocalDbServer("docker-postgres").Returns(config);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		_backupFileDetector.DetectBackupType(BackupFilePath).Returns(BackupFileType.PostgresBackup);
		_postgresToolsPathDetector.GetPgRestorePath(null).Returns("/usr/bin/pg_restore");
		postgres.FindTemplateBySourceFile("app").Returns("template_existing");
		postgres.CheckDbExists("restored-db").Returns(false);
		postgres.CreateDbFromTemplate("template_existing", "restored-db").Returns(true);
		_dbClientFactory.CreatePostgres("localhost", 5433, "postgres", "postgres").Returns(postgres);

		MethodInfo method = GetRestoreToLocalDbMethod();

		// Act
		int result = (int)method.Invoke(_sut, new object[] { UnzippedDirectory, "restored-db", "docker-postgres", false, "app.zip" })!;

		// Assert
		result.Should().Be(0, because: "existing PostgreSQL templates should be reused instead of re-running pg_restore");
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>());
		postgres.DidNotReceive().CreateDb(Arg.Any<string>());
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Found existing template 'template_existing'")));
	}

	[Test]
	[Description("Should show Docker host-port troubleshooting hint when deploy-creatio local PostgreSQL connection test fails")]
	public void RestoreToLocalDb_WhenPostgresConnectionFails_ShowsDockerHint() {
		// Arrange
		LocalDbServerConfiguration config = CreateDockerPostgresConfig();

		_settingsRepository.GetLocalDbServer("docker-postgres").Returns(config);
		_dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult {
			Success = false,
			ErrorMessage = "Connection refused",
			Suggestion = "Verify PostgreSQL host and port"
		});

		MethodInfo method = GetRestoreToLocalDbMethod();

		// Act
		int result = (int)method.Invoke(_sut, new object[] { UnzippedDirectory, "restored-db", "docker-postgres", false, "app.zip" })!;

		// Assert
		result.Should().Be(1, because: "deploy-creatio must stop before restore when PostgreSQL connectivity validation fails");
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>());
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("docker ps") && s.Contains("published host port")));
	}

	private static LocalDbServerConfiguration CreateDockerPostgresConfig() {
		return new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5433,
			Username = "postgres",
			Password = "postgres"
		};
	}

	private static MethodInfo GetRestoreToLocalDbMethod() {
		return typeof(CreatioInstallerService)
			.GetMethod("RestoreToLocalDb", BindingFlags.Instance | BindingFlags.NonPublic)!;
	}
}



