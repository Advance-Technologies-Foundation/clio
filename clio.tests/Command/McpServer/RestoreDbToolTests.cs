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
	[Description("Advertises the stable restore-db MCP tool names so unit and end-to-end coverage track the production contract identifiers.")]
	public void RestoreDb_Should_Advertise_Stable_Tool_Names() {
		// Arrange

		// Act
		string byEnvironment = RestoreDbTool.RestoreDbByEnvironmentToolName;
		string byCredentials = RestoreDbTool.RestoreDbByCredentialsToolName;
		string toLocalServer = RestoreDbTool.RestoreDbToLocalServerToolName;

		// Assert
		byEnvironment.Should().Be("restore-db-by-environment",
			because: "the environment restore MCP contract should keep a stable tool identifier");
		byCredentials.Should().Be("restore-db-by-credentials",
			because: "the explicit-credentials restore MCP contract should keep a stable tool identifier");
		toLocalServer.Should().Be("restore-db-to-local-server",
			because: "the local-server restore MCP contract should keep a stable tool identifier");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks every restore-db MCP entrypoint as destructive because each flow mutates a target database.")]
	public void RestoreDb_Should_Expose_Destructive_Metadata_For_All_Tools() {
		// Arrange
		McpServerToolAttribute byEnvironment = GetToolAttribute(nameof(RestoreDbTool.RestoreByEnvironment));
		McpServerToolAttribute byCredentials = GetToolAttribute(nameof(RestoreDbTool.RestoreByCredentials));
		McpServerToolAttribute toLocalServer = GetToolAttribute(nameof(RestoreDbTool.RestoreToLocalServer));

		// Act
		bool[] destructiveFlags = [byEnvironment.Destructive, byCredentials.Destructive, toLocalServer.Destructive];

		// Assert
		destructiveFlags.Should().OnlyContain(flag => flag,
			because: "every restore-db MCP entrypoint can replace a database and must be advertised as destructive");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-based restore-db MCP tool through the environment-aware command resolver and forwards the requested overrides.")]
	public void RestoreDbByEnvironment_Should_Resolve_Command_And_Map_Arguments() {
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
		RestoreDbByEnvironmentArgs args = new("sandbox", @"C:\backups\db.backup", "sandbox_db", true, true, false);

		// Act
		CommandExecutionResult result = tool.RestoreByEnvironment(args);

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
			because: "environment-based MCP execution should use the resolved command instance");
		resolvedCommand.ReceivedOptions.Should().NotBeNull(
			because: "the resolved restore-db command should receive the mapped MCP options");
		resolvedCommand.ReceivedOptions!.Environment.Should().Be("sandbox",
			because: "environmentName should map directly to EnvironmentOptions.Environment");
		resolvedCommand.ReceivedOptions.BackupPath.Should().Be(@"C:\backups\db.backup",
			because: "backupPath should map directly to RestoreDbCommandOptions.BackupPath");
		resolvedCommand.ReceivedOptions.DbName.Should().Be("sandbox_db",
			because: "dbName should map directly to RestoreDbCommandOptions.DbName");
		resolvedCommand.ReceivedOptions.Force.Should().BeTrue(
			because: "force should be preserved for legacy environment-based restore flows");
		resolvedCommand.ReceivedOptions.AsTemplate.Should().BeTrue(
			because: "asTemplate should map directly to the restore-db command options");
		resolvedCommand.ReceivedOptions.DisableResetPassword.Should().BeFalse(
			because: "disableResetPassword should map directly to the restore-db command options");
		result.LogFilePath.Should().NotBeNullOrWhiteSpace(
			because: "the MCP result should surface the generated database-operation log path");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the explicit-credentials restore-db MCP contract into the legacy direct database restore options and returns the log artifact path.")]
	public void RestoreDbByCredentials_Should_Map_Explicit_Credentials_Arguments() {
		// Arrange
		TestLogger logger = new();
		DbOperationLogContextAccessor dbOperationLogContextAccessor = new();
		IDbOperationLogSessionFactory dbOperationLogSessionFactory =
			new DbOperationLogSessionFactory(logger, dbOperationLogContextAccessor);
		FakeRestoreDbCommand command = new(logger, exitCode: 0, dbOperationLogSessionFactory);
		RestoreDbTool tool = new(command, logger, Substitute.For<IToolCommandResolver>(), dbOperationLogContextAccessor);
		RestoreDbByCredentialsArgs args = new(
			"mssql://localhost:1433",
			"sa",
			"Password1!",
			@"C:\sql-share",
			@"C:\backups\db.bak",
			"sandbox_db",
			true,
			true,
			false);

		// Act
		CommandExecutionResult result = tool.RestoreByCredentials(args);

		// Assert
		command.ReceivedOptions.Should().NotBeNull(
			because: "the direct credentials MCP path should invoke the injected restore-db command");
		command.ReceivedOptions!.DbServerUri.Should().Be("mssql://localhost:1433",
			because: "dbServerUri should map into the legacy direct restore options");
		command.ReceivedOptions.DbUser.Should().Be("sa",
			because: "dbUser should map into the legacy direct restore options");
		command.ReceivedOptions.DbPassword.Should().Be("Password1!",
			because: "dbPassword should map into the legacy direct restore options");
		command.ReceivedOptions.DbWorknigFolder.Should().Be(@"C:\sql-share",
			because: "dbWorkingFolder should map into the legacy direct restore options");
		command.ReceivedOptions.BackUpFilePath.Should().Be(@"C:\backups\db.bak",
			because: "backupPath should map into the legacy BackUpFilePath option");
		command.ReceivedOptions.DbName.Should().Be("sandbox_db",
			because: "dbName should map into the legacy direct restore options");
		command.ReceivedOptions.Force.Should().BeTrue(
			because: "force should be preserved for legacy overwrite behavior");
		command.ReceivedOptions.AsTemplate.Should().BeTrue(
			because: "asTemplate should map into the legacy direct restore options");
		command.ReceivedOptions.DisableResetPassword.Should().BeFalse(
			because: "disableResetPassword should map into the legacy direct restore options");
		result.LogFilePath.Should().NotBeNullOrWhiteSpace(
			because: "the MCP result should surface the generated database-operation log path");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the local-server restore-db MCP contract into the current local restore options and preserves the drop-if-exists flag.")]
	public void RestoreDbToLocalServer_Should_Map_Local_Server_Arguments() {
		// Arrange
		TestLogger logger = new();
		DbOperationLogContextAccessor dbOperationLogContextAccessor = new();
		IDbOperationLogSessionFactory dbOperationLogSessionFactory =
			new DbOperationLogSessionFactory(logger, dbOperationLogContextAccessor);
		FakeRestoreDbCommand command = new(logger, exitCode: 0, dbOperationLogSessionFactory);
		RestoreDbTool tool = new(command, logger, Substitute.For<IToolCommandResolver>(), dbOperationLogContextAccessor);
		RestoreDbToLocalServerArgs args = new("local-sql", @"C:\backups\db.bak", "sandbox_db", true, true, false);

		// Act
		CommandExecutionResult result = tool.RestoreToLocalServer(args);

		// Assert
		command.ReceivedOptions.Should().NotBeNull(
			because: "the local-server MCP path should execute the restore-db command with mapped options");
		command.ReceivedOptions!.DbServerName.Should().Be("local-sql",
			because: "dbServerName should map directly into RestoreDbCommandOptions.DbServerName");
		command.ReceivedOptions.BackupPath.Should().Be(@"C:\backups\db.bak",
			because: "backupPath should map directly into RestoreDbCommandOptions.BackupPath");
		command.ReceivedOptions.DbName.Should().Be("sandbox_db",
			because: "dbName should map directly into RestoreDbCommandOptions.DbName");
		command.ReceivedOptions.DropIfExists.Should().BeTrue(
			because: "dropIfExists should be preserved for the local restore flow");
		command.ReceivedOptions.AsTemplate.Should().BeTrue(
			because: "asTemplate should be preserved for the local restore flow");
		command.ReceivedOptions.DisableResetPassword.Should().BeFalse(
			because: "disableResetPassword should be preserved for the local restore flow");
		result.LogFilePath.Should().NotBeNullOrWhiteSpace(
			because: "the MCP result should surface the generated database-operation log path");
	}

	[Test]
	[Category("Unit")]
	[Description("Restore-db MCP prompts mention the log-file-path artifact so agents know where to look for engine-level restore diagnostics.")]
	public void RestoreDbPrompt_Should_Mention_Log_File_Path() {
		// Arrange

		// Act
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
		environmentPrompt.Should().Contain("disableResetPassword",
			because: "the environment restore prompt should mention how to skip the password-reset script when needed");
		environmentPrompt.Should().Contain("asTemplate",
			because: "the environment restore prompt should explain template-only PostgreSQL execution");
		credentialsPrompt.Should().Contain("log-file-path",
			because: "the credentials restore prompt should tell agents where detailed restore diagnostics are returned");
		credentialsPrompt.Should().Contain("disableResetPassword",
			because: "the credentials restore prompt should mention how to skip the password-reset script when needed");
		credentialsPrompt.Should().Contain("asTemplate",
			because: "the credentials restore prompt should explain template-only PostgreSQL execution");
		localPrompt.Should().Contain("log-file-path",
			because: "the local-server restore prompt should tell agents where detailed restore diagnostics are returned");
		localPrompt.Should().Contain("disableResetPassword",
			because: "the local-server restore prompt should mention how to skip the password-reset script when needed");
		localPrompt.Should().Contain("asTemplate",
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
