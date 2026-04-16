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
using ConsoleTables;
using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RestoreDbLogArtifactTests {
	[Test]
	[Description("restore-db always creates a temp database-operation log artifact and reports its path even when validation fails before restore starts.")]
	public void Execute_WhenBackupMissing_ReportsLogPath_And_CreatesArtifact() {
		// Arrange
		TestLogger logger = new();
		IDbOperationLogContextAccessor contextAccessor = new DbOperationLogContextAccessor();
		IDbOperationLogSessionFactory sessionFactory = new DbOperationLogSessionFactory(logger, contextAccessor);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IDbClientFactory dbClientFactory = Substitute.For<IDbClientFactory>();
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IDbConnectionTester dbConnectionTester = Substitute.For<IDbConnectionTester>();
		IBackupFileDetector backupFileDetector = Substitute.For<IBackupFileDetector>();
		IPostgresToolsPathDetector postgresToolsPathDetector = Substitute.For<IPostgresToolsPathDetector>();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();

		settingsRepository.GetLocalDbServer("local-pg").Returns(new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		});
		fileSystem.ExistsFile("missing.backup").Returns(false);

		RestoreDbCommand sut = new(
			logger,
			fileSystem,
			dbClientFactory,
			settingsRepository,
			Substitute.For<ICreatioInstallerService>(),
			dbConnectionTester,
			backupFileDetector,
			postgresToolsPathDetector,
			processExecutor,
			sessionFactory,
			contextAccessor);

		RestoreDbCommandOptions options = new() {
			DbServerName = "local-pg",
			BackupPath = "missing.backup",
			DbName = "sampledb"
		};

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "restore-db should fail when the requested backup file does not exist");
		string logFilePath = logger.GetLatestArtifactPath();
		logFilePath.Should().NotBeNullOrWhiteSpace(
			because: "restore-db should always report the generated temp database-operation log path");
		File.Exists(logFilePath).Should().BeTrue(
			because: "the temp database-operation log artifact should be created at command start");
		File.ReadAllText(logFilePath).Should().Contain("[DB-OPERATION] restore-db",
			because: "the artifact should identify which command invocation produced the temp database-operation log");
	}

	[Test]
	[Description("restore-db writes raw pg_restore output into the database-operation log artifact even when debug mode is off.")]
	public void Execute_WhenPostgresRestoreEmitsNativeOutput_WritesItToArtifact() {
		// Arrange
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		try {
			TestLogger logger = new();
			IDbOperationLogContextAccessor contextAccessor = new DbOperationLogContextAccessor();
			IDbOperationLogSessionFactory sessionFactory = new DbOperationLogSessionFactory(logger, contextAccessor);
			IFileSystem fileSystem = Substitute.For<IFileSystem>();
			IDbClientFactory dbClientFactory = Substitute.For<IDbClientFactory>();
			ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
			IDbConnectionTester dbConnectionTester = Substitute.For<IDbConnectionTester>();
			IBackupFileDetector backupFileDetector = Substitute.For<IBackupFileDetector>();
			IPostgresToolsPathDetector postgresToolsPathDetector = Substitute.For<IPostgresToolsPathDetector>();
			IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
			Postgres postgres = Substitute.For<Postgres>();

			LocalDbServerConfiguration config = new() {
				DbType = "postgres",
				Hostname = "localhost",
				Port = 5432,
				Username = "postgres",
				Password = "postgres"
			};

			settingsRepository.GetLocalDbServer("local-pg").Returns(config);
			fileSystem.ExistsFile("backup.backup").Returns(true);
			dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
			backupFileDetector.DetectBackupType("backup.backup").Returns(BackupFileType.PostgresBackup);
			postgresToolsPathDetector.GetPgRestorePath(null).Returns("/usr/bin/pg_restore");
			postgres.CheckDbExists("sampledb").Returns(false);
			postgres.CreateDb("sampledb").Returns(true);
			dbClientFactory.CreatePostgres("localhost", 5432, "postgres", "postgres").Returns(postgres);
			processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
				.Returns(callInfo => {
					ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
					executionOptions.OnOutput?.Invoke("pg_restore: processing item 42", ProcessOutputStream.StdOut);
					return Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 });
				});

			RestoreDbCommand sut = new(
				logger,
				fileSystem,
				dbClientFactory,
				settingsRepository,
				Substitute.For<ICreatioInstallerService>(),
				dbConnectionTester,
				backupFileDetector,
				postgresToolsPathDetector,
				processExecutor,
				sessionFactory,
				contextAccessor);

			RestoreDbCommandOptions options = new() {
				DbServerName = "local-pg",
				BackupPath = "backup.backup",
				DbName = "sampledb"
			};

			// Act
			int result = sut.Execute(options);

			// Assert
			result.Should().Be(0, because: "the PostgreSQL restore should succeed when pg_restore exits successfully");
			string logFilePath = logger.GetLatestArtifactPath();
			File.ReadAllText(logFilePath).Should().Contain("pg_restore: processing item 42",
				because: "native pg_restore output should be preserved in the temp artifact even outside debug mode");
		}
		finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("restore-db writes SQL Server restore progress messages into the database-operation log artifact.")]
	public void Execute_WhenMssqlRestoreEmitsNativeOutput_WritesItToArtifact() {
		// Arrange
		TestLogger logger = new();
		IDbOperationLogContextAccessor contextAccessor = new DbOperationLogContextAccessor();
		IDbOperationLogSessionFactory sessionFactory = new DbOperationLogSessionFactory(logger, contextAccessor);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IDbClientFactory dbClientFactory = Substitute.For<IDbClientFactory>();
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IDbConnectionTester dbConnectionTester = Substitute.For<IDbConnectionTester>();
		IBackupFileDetector backupFileDetector = Substitute.For<IBackupFileDetector>();
		IPostgresToolsPathDetector postgresToolsPathDetector = Substitute.For<IPostgresToolsPathDetector>();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		IMssql mssql = Substitute.For<IMssql>();

		LocalDbServerConfiguration config = new() {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa",
			Password = "Password1!"
		};

		settingsRepository.GetLocalDbServer("local-sql").Returns(config);
		fileSystem.ExistsFile("backup.bak").Returns(true);
		dbConnectionTester.TestConnection(config).Returns(new ConnectionTestResult { Success = true });
		backupFileDetector.DetectBackupType("backup.bak").Returns(BackupFileType.MssqlBackup);
		mssql.CheckDbExists("sampledb").Returns(false);
		mssql.GetDataPath().Returns(@"C:\SqlData");
		mssql.RestoreDatabase("sampledb", "backup.bak", Arg.Any<Action<string>>())
			.Returns(callInfo => {
				Action<string> onMessage = callInfo.ArgAt<Action<string>>(2);
				onMessage("10 percent processed.");
				return new DatabaseRestoreResult(true, Array.Empty<string>());
			});
		dbClientFactory.CreateMssql("localhost", 1433, "sa", "Password1!", false).Returns(mssql);

		RestoreDbCommand sut = new(
			logger,
			fileSystem,
			dbClientFactory,
			settingsRepository,
			Substitute.For<ICreatioInstallerService>(),
			dbConnectionTester,
			backupFileDetector,
			postgresToolsPathDetector,
			processExecutor,
			sessionFactory,
			contextAccessor);

		RestoreDbCommandOptions options = new() {
			DbServerName = "local-sql",
			BackupPath = "backup.bak",
			DbName = "sampledb"
		};

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "the SQL Server restore should succeed when the mocked engine restore succeeds");
		string logFilePath = logger.GetLatestArtifactPath();
		File.ReadAllText(logFilePath).Should().Contain("10 percent processed.",
			because: "SQL Server restore progress should be written to the temp database-operation artifact");
	}

	private sealed class TestLogger : ILogger {
		private readonly Dictionary<Guid, string> _scopedSinks = [];

		List<LogMessage> ILogger.LogMessages => LogMessages;
		bool ILogger.PreserveMessages { get; set; }
		internal List<LogMessage> LogMessages { get; } = [];

		public void ClearMessages() => LogMessages.Clear();

		public IDisposable BeginScopedFileSink(string logFilePath) {
			string fullPath = Path.GetFullPath(logFilePath);
			string? directory = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrWhiteSpace(directory)) {
				Directory.CreateDirectory(directory);
			}

			Guid sinkId = Guid.NewGuid();
			_scopedSinks[sinkId] = fullPath;
			return new ScopedSink(_scopedSinks, sinkId);
		}

		public void Start(string logFilePath = "") { }
		public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
		public void StartWithStream() { }
		public void Stop() { }
		public void Write(string value) => WriteLine(value);
		public void WriteLine() => WriteLine(string.Empty);
		public void WriteLine(string value) => Append(new UndecoratedMessage(value), value);
		public void WriteWarning(string value) => Append(new WarningMessage(value), value);
		public void WriteError(string value) => Append(new ErrorMessage(value), value);
		public void WriteInfo(string value) => Append(new InfoMessage(value), value);
		public void WriteDebug(string value) => Append(new DebugMessage(value), value);
		public void PrintTable(ConsoleTable table) { }
		public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) { }

		public string GetLatestArtifactPath() {
			return LogMessages
				.OfType<InfoMessage>()
				.Select(message => message.Value?.ToString())
				.Last(value => value != null && value.StartsWith("Database operation log: ", StringComparison.Ordinal))
				.Replace("Database operation log: ", string.Empty, StringComparison.Ordinal);
		}

		private void Append(LogMessage message, string value) {
			LogMessages.Add(message);
			foreach ((_, string sinkPath) in _scopedSinks.ToArray()) {
				using StreamWriter writer = CreateSharedAppendWriter(sinkPath);
				writer.WriteLine(value);
			}
		}

		private sealed class ScopedSink(
			IDictionary<Guid, string> sinks,
			Guid sinkId) : IDisposable {
			public void Dispose() {
				sinks.Remove(sinkId);
			}
		}

		private static StreamWriter CreateSharedAppendWriter(string filePath) {
			FileStream stream = new(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
			return new StreamWriter(stream) {
				AutoFlush = true
			};
		}
	}
}
