using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using ConsoleTables;
using FluentAssertions;
using FluentValidation.Results;
using k8s;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class InstallerCommandLogArtifactTests {
	[Test]
	[Description("deploy-creatio always creates a temp database-operation log artifact and reports its path through normal CLI logger output.")]
	public void Execute_WhenInstallerRuns_ReportsLogPath_And_CreatesArtifact() {
		// Arrange
		TestLogger logger = new();
		IDbOperationLogContextAccessor contextAccessor = new DbOperationLogContextAccessor();
		IDbOperationLogSessionFactory sessionFactory = new DbOperationLogSessionFactory(logger, contextAccessor);
		ICreatioInstallerService installerService = Substitute.For<ICreatioInstallerService>();
		installerService.Execute(Arg.Any<PfInstallerOptions>()).Returns(0);
		InstallerCommand sut = new(installerService, logger, Substitute.For<IKubernetes>(), sessionFactory);

		// Act
		int result = sut.Execute(new PfInstallerOptions {
			IsSilent = true,
			DbServerName = "local-pg"
		});

		// Assert
		result.Should().Be(0, because: "the installer service reported a successful deployment");
		string logFilePath = logger.GetLatestArtifactPath();
		logFilePath.Should().NotBeNullOrWhiteSpace(
			because: "deploy-creatio should always report the generated database-operation log path");
		File.Exists(logFilePath).Should().BeTrue(
			because: "deploy-creatio should create the temp database-operation log artifact before execution");
		File.ReadAllText(logFilePath).Should().Contain("[DB-OPERATION] deploy-creatio",
			because: "the temp artifact should record which database operation produced it");
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
