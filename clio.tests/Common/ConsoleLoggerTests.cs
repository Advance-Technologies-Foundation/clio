using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
[NonParallelizable]
internal class ConsoleLoggerTests
{

	[TestCase("inf")]
	[TestCase("warn")]
	[TestCase("error")]
	public void WriteError_ShouldAddTimestamp(string type) {

		// Arrange
		Program.AddTimeStampToOutput = true;
		StringBuilder stringBuilder = new StringBuilder();
		StringWriter textWriter = new StringWriter(stringBuilder);
		TextWriter originalOut = Console.Out;
		TextWriter originalError = Console.Error;
		Console.SetOut(textWriter);
		Console.SetError(textWriter);
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		logger.Start();

		try {
			// Act
			var timeStamp = DateTime.Now;
			switch (type) {
				case "inf":   logger.WriteInfo("Test info");       break;
				case "warn":  logger.WriteWarning("Test warning"); break;
				case "error": logger.WriteError("Test error");     break;
			}
			logger.FlushAndSnapshotMessages();

			// Assert
			string consoleText = stringBuilder.ToString();
			AssertTimeStamp(timeStamp, consoleText);
		}
		finally {
			Console.SetOut(originalOut);
			Console.SetError(originalError);
		}
	}

	public void AssertTimeStamp(DateTime timeStamp, string consoleText) {
		var consoleTimeStamp = DateTime.Parse(consoleText.Substring(0,8));
		(consoleTimeStamp - timeStamp).Should().BeLessThan(TimeSpan.FromSeconds(1));
	}

	[TestCase("inf")]
	[TestCase("warn")]
	[TestCase("error")]
	public void WriteError_ShouldNotAddTimestamp(string type) {

		// Arrange
		Program.AddTimeStampToOutput = false;
		var stringBuilder = new StringBuilder();
		var textWriter = new StringWriter(stringBuilder);
		TextWriter originalOut = Console.Out;
		TextWriter originalError = Console.Error;
		Console.SetOut(textWriter);
		Console.SetError(textWriter);
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		logger.Start();

		try {
			// Act
			switch (type) {
				case "inf":   logger.WriteInfo("Test info");       break;
				case "warn":  logger.WriteWarning("Test warning"); break;
				case "error": logger.WriteError("Test error");     break;
			}
			logger.FlushAndSnapshotMessages();

			// Assert
			var consoleText = stringBuilder.ToString();
			consoleText.Should().StartWith("[");
		}
		finally {
			Console.SetOut(originalOut);
			Console.SetError(originalError);
		}
	}

	[Test]
	public void Dispose_test() {
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		var mockLogFileWtiter = Substitute.For<System.IO.TextWriter>();
		logger.LogFileWriter = mockLogFileWtiter;
		logger.Dispose();
		mockLogFileWtiter.Received().Dispose();
	}

	[Test]
	public void Dispose_WhenLogFileWriterIsNull() {
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		logger.LogFileWriter = null;
		Assert.DoesNotThrow(() => logger.Dispose());
	}

	[Test]
	public void Dispose_Twice_WhenLogFileWriterIsNull() {
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		logger.LogFileWriter = Substitute.For<System.IO.TextWriter>();
		logger.Dispose();
		Assert.That(logger.LogFileWriter, Is.Null);
		Assert.DoesNotThrow(() => logger.Dispose());
	}

	[Test]
	[Description("Flushes queued log messages before returning a preserved snapshot")]
	public void FlushAndSnapshotMessages_Should_DrainQueuedMessages_BeforeReturningSnapshot() {
		// Arrange
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		bool previousPreserveMessages = logger.PreserveMessages;
		Console.SetOut(TextWriter.Null);
		Console.SetError(TextWriter.Null);
		logger.ClearMessages();
		logger.PreserveMessages = true;
		logger.WriteInfo("Queued info");
		logger.WriteWarning("Queued warning");

		try {
			// Act
			IReadOnlyList<LogMessage> snapshot = logger.FlushAndSnapshotMessages();
			string[] values = snapshot.Select(message => message.Value?.ToString()).ToArray();

			// Assert
			values.Should().ContainInOrder(
				["Queued info", "Queued warning"],
				because: "the snapshot should flush pending queued messages before it is returned");
		}
		finally {
			logger.ClearMessages();
			logger.PreserveMessages = previousPreserveMessages;
		}
	}

	[Test]
	[Description("Clears flushed messages so prior queued entries do not leak into the next snapshot")]
	public void ClearMessages_Should_Prevent_PreviousQueuedMessages_From_Leaking_IntoNextSnapshot() {
		// Arrange
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		bool previousPreserveMessages = logger.PreserveMessages;
		Console.SetOut(TextWriter.Null);
		Console.SetError(TextWriter.Null);
		logger.ClearMessages();
		logger.PreserveMessages = true;
		logger.WriteInfo("First batch");
		logger.ClearMessages();
		logger.WriteInfo("Second batch");

		try {
			// Act
			IReadOnlyList<LogMessage> snapshot = logger.FlushAndSnapshotMessages(clearMessages: true);
			IReadOnlyList<LogMessage> nextSnapshot = logger.FlushAndSnapshotMessages();
			string[] snapshotValues = snapshot.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

			// Assert
			snapshotValues.Should().ContainSingle(
				message => string.Equals(message, "Second batch", StringComparison.Ordinal),
				because: "clearing after the first batch should prevent prior queued messages from leaking into the next capture");
			snapshotValues.Should().NotContain(
				message => string.Equals(message, "First batch", StringComparison.Ordinal),
				because: "the cleared first batch should not appear in the later snapshot");
			nextSnapshot.Should().BeEmpty(
				because: "clearMessages=true should empty the preserved buffer after capture");
		}
		finally {
			logger.ClearMessages();
			logger.PreserveMessages = previousPreserveMessages;
		}
	}

	[SetUp]
	public void ResetRuntimeModeBeforeTest() {
		// The logger is a process-wide singleton; guarantee every test in this fixture starts in the
		// default (non-MCP) run-mode so an earlier test can never leak console-output suppression here.
		((ConsoleLogger)ConsoleLogger.Instance).RuntimeMode = null;
	}

	[TearDown]
	public void ResetRuntimeModeAfterTest() {
		// Clear any run-mode a test below set so the shared singleton never leaks MCP suppression out.
		((ConsoleLogger)ConsoleLogger.Instance).RuntimeMode = null;
	}

	[Test]
	[Description("Suppresses decorated info, error, warning and undecorated console writes when the injected run-mode reports MCP server mode.")]
	public void Write_ShouldSuppressConsoleOutput_WhenRuntimeModeIsMcpServer() {
		// Arrange
		bool previousTimestamp = Program.AddTimeStampToOutput;
		Program.AddTimeStampToOutput = false;
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		logger.Start();
		// Drain any messages queued by an earlier test to the current console BEFORE redirecting, so the
		// capture window below observes only this test's (suppressed) writes and not stragglers drained by
		// the process-wide background print thread.
		logger.FlushAndSnapshotMessages();
		StringBuilder stringBuilder = new StringBuilder();
		StringWriter textWriter = new StringWriter(stringBuilder);
		TextWriter originalOut = Console.Out;
		TextWriter originalError = Console.Error;
		Console.SetOut(textWriter);
		Console.SetError(textWriter);
		logger.RuntimeMode = new RuntimeMode(IsMcpServerMode: true);

		try {
			// Act — exercises the former suppression sites for info, error, warning and undecorated writes.
			logger.WriteInfo("info-under-mcp");
			logger.WriteError("error-under-mcp");
			logger.WriteWarning("warning-under-mcp");
			logger.WriteLine("line-under-mcp");
			logger.FlushAndSnapshotMessages();

			// Assert
			stringBuilder.ToString().Should().BeEmpty(
				because: "MCP server mode must suppress every decorated console write so stdout carries only protocol traffic");
		}
		finally {
			Console.SetOut(originalOut);
			Console.SetError(originalError);
			Program.AddTimeStampToOutput = previousTimestamp;
		}
	}

	[Test]
	[Description("Emits decorated info and error console output when the injected run-mode is not MCP server mode.")]
	public void Write_ShouldEmitConsoleOutput_WhenRuntimeModeIsNotMcpServer() {
		// Arrange
		bool previousTimestamp = Program.AddTimeStampToOutput;
		Program.AddTimeStampToOutput = false;
		StringBuilder stringBuilder = new StringBuilder();
		StringWriter textWriter = new StringWriter(stringBuilder);
		TextWriter originalOut = Console.Out;
		TextWriter originalError = Console.Error;
		Console.SetOut(textWriter);
		Console.SetError(textWriter);
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		logger.RuntimeMode = new RuntimeMode(IsMcpServerMode: false);
		logger.Start();

		try {
			// Act
			logger.WriteInfo("info-emitted");
			logger.WriteError("error-emitted");
			logger.FlushAndSnapshotMessages();

			// Assert
			string consoleText = stringBuilder.ToString();
			consoleText.Should().Contain("info-emitted",
				because: "a non-MCP run must emit decorated info output to the console exactly as before");
			consoleText.Should().Contain("error-emitted",
				because: "a non-MCP run must emit decorated error output to the console exactly as before");
		}
		finally {
			Console.SetOut(originalOut);
			Console.SetError(originalError);
			Program.AddTimeStampToOutput = previousTimestamp;
		}
	}

	[Test]
	[Description("Routes BeginSpinner through a suppressed WriteInfo and skips EndSpinner rendering when the injected run-mode reports MCP server mode.")]
	public void Spinner_ShouldSuppressConsoleOutput_WhenRuntimeModeIsMcpServer() {
		// Arrange
		bool previousTimestamp = Program.AddTimeStampToOutput;
		Program.AddTimeStampToOutput = false;
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		logger.Start();
		// Drain any messages queued by an earlier test to the current console BEFORE redirecting, so the
		// capture window below observes only this test's (suppressed) writes and not stragglers drained by
		// the process-wide background print thread.
		logger.FlushAndSnapshotMessages();
		StringBuilder stringBuilder = new StringBuilder();
		StringWriter textWriter = new StringWriter(stringBuilder);
		TextWriter originalOut = Console.Out;
		TextWriter originalError = Console.Error;
		Console.SetOut(textWriter);
		Console.SetError(textWriter);
		logger.RuntimeMode = new RuntimeMode(IsMcpServerMode: true);

		try {
			// Act — BeginSpinner's IsMcpServerMode check (former site 481) short-circuits to a suppressed
			// WriteInfo, so no spinner thread starts; EndSpinner's guard (former site 514) is a safe no-op.
			logger.BeginSpinner("spinner-under-mcp");
			logger.EndSpinner();
			logger.FlushAndSnapshotMessages();

			// Assert
			stringBuilder.ToString().Should().BeEmpty(
				because: "MCP server mode routes the spinner through a suppressed WriteInfo and skips end-of-spinner rendering");
		}
		finally {
			Console.SetOut(originalOut);
			Console.SetError(originalError);
			Program.AddTimeStampToOutput = previousTimestamp;
		}
	}

}
