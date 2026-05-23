using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.db;
using Clio.UserEnvironment;
using ConsoleTables;
using FluentAssertions;
using FluentValidation.Results;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class RestoreDbToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises the stable consolidated restore-db MCP tool name and the supported mode discriminator values.")]
	public void RestoreDb_Should_Advertise_Stable_Contract() {
		// Arrange / Act
		string toolName = RestoreDbTool.RestoreDbToolName;
		string[] modes = [
			RestoreDbTool.ModeEnvironment,
			RestoreDbTool.ModeDbCredentials,
			RestoreDbTool.ModeLocalServer,
		];

		// Assert
		toolName.Should().Be("restore-db",
			because: "the MCP contract identifier must stay stable after consolidation");
		modes.Should().BeEquivalentTo(["environment", "db-credentials", "local-server"],
			because: "the three supported mode discriminator values must remain stable");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the consolidated restore-db MCP entrypoint as destructive because every mode mutates a target database.")]
	[Ignore("ENG-90312 Phase 2: tool folded into clio-run; safety flags now reflected on clio-run itself. Polymorphic registry validated by Z7 schema-discovery test.")]
	public void RestoreDb_Should_Expose_Destructive_Metadata() {
		// Arrange
		McpServerToolAttribute attribute = GetToolAttribute(nameof(RestoreDbTool.Restore));

		// Assert
		attribute.Destructive.Should().BeTrue(
			because: "every restore-db MCP entrypoint can replace a database and must be advertised as destructive");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment mode through the environment-aware command resolver and forwards the requested overrides.")]
	public void RestoreDb_EnvironmentMode_Should_Resolve_Command_And_Map_Arguments() {
		// Arrange
		TestLogger logger = new();
		DbOperationLogContextAccessor dbOperationLogContextAccessor = new();
		IDbOperationLogSessionFactory dbOperationLogSessionFactory =
			new DbOperationLogSessionFactory(logger, dbOperationLogContextAccessor);
		FakeRestoreDbCommand defaultCommand = new(logger, exitCode: 5, dbOperationLogSessionFactory);
		FakeRestoreDbCommand resolvedCommand = new(logger, exitCode: 5, dbOperationLogSessionFactory);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestoreDbCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		RestoreDbTool tool = new(defaultCommand, logger, commandResolver, dbOperationLogContextAccessor);

		// Act
		CommandExecutionResult result = tool.Restore(new RestoreDbRunArgs(
			Mode: RestoreDbTool.ModeEnvironment,
			EnvironmentName: "sandbox",
			BackupPath: @"C:\backups\db.backup",
			DbName: "sandbox_db",
			Force: true,
			AsTemplate: true,
			DisableResetPassword: false));

		// Assert
		result.ExitCode.Should().Be(5,
			because: "the MCP wrapper should return the real restore-db command result");
		commandResolver.Received(1).Resolve<RestoreDbCommand>(Arg.Is<RestoreDbCommandOptions>(options =>
			options.Environment == "sandbox" &&
			options.BackupPath == @"C:\backups\db.backup" &&
			options.DbName == "sandbox_db" &&
			options.Force &&
			options.AsTemplate &&
			!options.DisableResetPassword));
		defaultCommand.ReceivedOptions.Should().BeNull(
			because: "environment-mode MCP execution should use the resolved command instance");
		resolvedCommand.ReceivedOptions.Should().NotBeNull(
			because: "the resolved restore-db command should receive the mapped MCP options");
		resolvedCommand.ReceivedOptions!.Environment.Should().Be("sandbox",
			because: "environment-name should map directly to RestoreDbCommandOptions.Environment");
		resolvedCommand.ReceivedOptions.BackupPath.Should().Be(@"C:\backups\db.backup",
			because: "backup-path should map directly to RestoreDbCommandOptions.BackupPath");
		resolvedCommand.ReceivedOptions.DbName.Should().Be("sandbox_db",
			because: "db-name should map directly to RestoreDbCommandOptions.DbName");
		resolvedCommand.ReceivedOptions.Force.Should().BeTrue(
			because: "force should be preserved for legacy environment-based restore flows");
		resolvedCommand.ReceivedOptions.AsTemplate.Should().BeTrue(
			because: "as-template should map directly to the restore-db command options");
		resolvedCommand.ReceivedOptions.DisableResetPassword.Should().BeFalse(
			because: "disable-reset-password should map directly to the restore-db command options");
		result.LogFilePath.Should().NotBeNullOrWhiteSpace(
			because: "the MCP result should surface the generated database-operation log path");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the db-credentials mode into the legacy direct database restore options and returns the log artifact path.")]
	public void RestoreDb_DbCredentialsMode_Should_Map_Explicit_Credentials_Arguments() {
		// Arrange
		TestLogger logger = new();
		DbOperationLogContextAccessor dbOperationLogContextAccessor = new();
		IDbOperationLogSessionFactory dbOperationLogSessionFactory =
			new DbOperationLogSessionFactory(logger, dbOperationLogContextAccessor);
		FakeRestoreDbCommand command = new(logger, exitCode: 0, dbOperationLogSessionFactory);
		RestoreDbTool tool = new(command, logger, Substitute.For<IToolCommandResolver>(), dbOperationLogContextAccessor);

		// Act
		CommandExecutionResult result = tool.Restore(new RestoreDbRunArgs(
			Mode: RestoreDbTool.ModeDbCredentials,
			DbServerUri: "mssql://localhost:1433",
			DbUser: "sa",
			DbPassword: "Password1!",
			DbWorkingFolder: @"C:\sql-share",
			BackupPath: @"C:\backups\db.bak",
			DbName: "sandbox_db",
			Force: true,
			AsTemplate: true,
			DisableResetPassword: false));

		// Assert
		command.ReceivedOptions.Should().NotBeNull(
			because: "the direct credentials mode should invoke the injected restore-db command");
		command.ReceivedOptions!.DbServerUri.Should().Be("mssql://localhost:1433",
			because: "db-server-uri should map into the legacy direct restore options");
		command.ReceivedOptions.DbUser.Should().Be("sa",
			because: "db-user should map into the legacy direct restore options");
		command.ReceivedOptions.DbPassword.Should().Be("Password1!",
			because: "db-password should map into the legacy direct restore options");
		command.ReceivedOptions.DbWorknigFolder.Should().Be(@"C:\sql-share",
			because: "db-working-folder should map into the legacy direct restore options");
		command.ReceivedOptions.BackUpFilePath.Should().Be(@"C:\backups\db.bak",
			because: "backup-path should map into the legacy BackUpFilePath option");
		command.ReceivedOptions.DbName.Should().Be("sandbox_db",
			because: "db-name should map into the legacy direct restore options");
		command.ReceivedOptions.Force.Should().BeTrue(
			because: "force should be preserved for legacy overwrite behavior");
		command.ReceivedOptions.AsTemplate.Should().BeTrue(
			because: "as-template should map into the legacy direct restore options");
		command.ReceivedOptions.DisableResetPassword.Should().BeFalse(
			because: "disable-reset-password should map into the legacy direct restore options");
		result.LogFilePath.Should().NotBeNullOrWhiteSpace(
			because: "the MCP result should surface the generated database-operation log path");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the local-server mode into the current local restore options and preserves the drop-if-exists flag.")]
	public void RestoreDb_LocalServerMode_Should_Map_Local_Server_Arguments() {
		// Arrange
		TestLogger logger = new();
		DbOperationLogContextAccessor dbOperationLogContextAccessor = new();
		IDbOperationLogSessionFactory dbOperationLogSessionFactory =
			new DbOperationLogSessionFactory(logger, dbOperationLogContextAccessor);
		FakeRestoreDbCommand command = new(logger, exitCode: 0, dbOperationLogSessionFactory);
		RestoreDbTool tool = new(command, logger, Substitute.For<IToolCommandResolver>(), dbOperationLogContextAccessor);

		// Act
		CommandExecutionResult result = tool.Restore(new RestoreDbRunArgs(
			Mode: RestoreDbTool.ModeLocalServer,
			DbServerName: "local-sql",
			BackupPath: @"C:\backups\db.bak",
			DbName: "sandbox_db",
			DropIfExists: true,
			AsTemplate: true,
			DisableResetPassword: false));

		// Assert
		command.ReceivedOptions.Should().NotBeNull(
			because: "the local-server mode should execute the restore-db command with mapped options");
		command.ReceivedOptions!.DbServerName.Should().Be("local-sql",
			because: "db-server-name should map directly into RestoreDbCommandOptions.DbServerName");
		command.ReceivedOptions.BackupPath.Should().Be(@"C:\backups\db.bak",
			because: "backup-path should map directly into RestoreDbCommandOptions.BackupPath");
		command.ReceivedOptions.DbName.Should().Be("sandbox_db",
			because: "db-name should map directly into RestoreDbCommandOptions.DbName");
		command.ReceivedOptions.DropIfExists.Should().BeTrue(
			because: "drop-if-exists should be preserved for the local restore flow");
		command.ReceivedOptions.AsTemplate.Should().BeTrue(
			because: "as-template should be preserved for the local restore flow");
		command.ReceivedOptions.DisableResetPassword.Should().BeFalse(
			because: "disable-reset-password should be preserved for the local restore flow");
		result.LogFilePath.Should().NotBeNullOrWhiteSpace(
			because: "the MCP result should surface the generated database-operation log path");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an unknown mode value with a clear error listing allowed modes.")]
	public void RestoreDb_Should_Reject_Invalid_Mode() {
		// Arrange
		TestLogger logger = new();
		DbOperationLogContextAccessor dbOperationLogContextAccessor = new();
		IDbOperationLogSessionFactory dbOperationLogSessionFactory =
			new DbOperationLogSessionFactory(logger, dbOperationLogContextAccessor);
		FakeRestoreDbCommand command = new(logger, exitCode: 0, dbOperationLogSessionFactory);
		RestoreDbTool tool = new(command, logger, Substitute.For<IToolCommandResolver>(), dbOperationLogContextAccessor);

		// Act
		CommandExecutionResult result = tool.Restore(new RestoreDbRunArgs(Mode: "bogus"));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an unknown mode discriminator should be rejected before the command runs");
		command.ReceivedOptions.Should().BeNull(
			because: "the underlying command must not run when the mode discriminator is invalid");
	}

	[Test]
	[Category("Unit")]
	[Description("Restore-db MCP prompts mention the log-file-path artifact so agents know where to look for engine-level restore diagnostics.")]
	public void RestoreDbPrompt_Should_Mention_Log_File_Path() {
		// Arrange / Act
		string environmentPrompt = RestoreDbPrompt.RestoreByEnvironmentPrompt("sandbox");
		string credentialsPrompt = RestoreDbPrompt.RestoreByCredentialsPrompt(
			"mssql://localhost:1433",
			@"C:\backups\db.bak",
			"sandbox_db");
		string localPrompt = RestoreDbPrompt.RestoreToLocalServerPrompt(
			"local-sql",
			@"C:\backups\db.bak",
			"sandbox_db");

		// Assert
		environmentPrompt.Should().Contain("log-file-path",
			because: "the environment restore prompt should tell agents where detailed restore diagnostics are returned");
		environmentPrompt.Should().Contain("disable-reset-password",
			because: "the environment restore prompt should mention how to skip the password-reset script when needed");
		environmentPrompt.Should().Contain("as-template",
			because: "the environment restore prompt should explain template-only PostgreSQL execution");
		credentialsPrompt.Should().Contain("log-file-path",
			because: "the credentials restore prompt should tell agents where detailed restore diagnostics are returned");
		credentialsPrompt.Should().Contain("disable-reset-password",
			because: "the credentials restore prompt should mention how to skip the password-reset script when needed");
		credentialsPrompt.Should().Contain("as-template",
			because: "the credentials restore prompt should explain template-only PostgreSQL execution");
		localPrompt.Should().Contain("log-file-path",
			because: "the local-server restore prompt should tell agents where detailed restore diagnostics are returned");
		localPrompt.Should().Contain("disable-reset-password",
			because: "the local-server restore prompt should mention how to skip the password-reset script when needed");
		localPrompt.Should().Contain("as-template",
			because: "the local-server restore prompt should explain template-only PostgreSQL execution");
	}

	private static McpServerToolAttribute GetToolAttribute(string methodName) {
		return (McpServerToolAttribute)typeof(RestoreDbTool)
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
	}

	private sealed class FakeRestoreDbCommand : RestoreDbCommand {
		private readonly TestLogger _logger;
		private readonly int _exitCode;
		private readonly IDbOperationLogSessionFactory _dbOperationLogSessionFactory;

		public FakeRestoreDbCommand(
			TestLogger logger,
			int exitCode,
			IDbOperationLogSessionFactory dbOperationLogSessionFactory)
			: base(
				logger,
				Substitute.For<IFileSystem>(),
				new System.IO.Abstractions.FileSystem(),
				Substitute.For<IDbClientFactory>(),
				Substitute.For<ISettingsRepository>(),
				Substitute.For<ICreatioInstallerService>(),
				Substitute.For<IDbConnectionTester>(),
				Substitute.For<IBackupFileDetector>(),
				Substitute.For<IPostgresToolsPathDetector>(),
				Substitute.For<IProcessExecutor>(),
				dbOperationLogSessionFactory,
				new DbOperationLogContextAccessor()) {
			_logger = logger;
			_exitCode = exitCode;
			_dbOperationLogSessionFactory = dbOperationLogSessionFactory;
		}

		public RestoreDbCommandOptions? ReceivedOptions { get; private set; }

		public override int Execute(RestoreDbCommandOptions options) {
			using IDbOperationLogSession session = _dbOperationLogSessionFactory.BeginSession("restore-db-test");
			ReceivedOptions = options;
			_logger.WriteInfo("real restore-db command path");
			_logger.WriteInfo($"Database operation log: {session.LogFilePath}");
			return _exitCode;
		}
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
