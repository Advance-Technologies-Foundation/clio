using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal class ConsoleLoggerTests
{

	[TestCase("inf")]
	[TestCase("warn")]
	[TestCase("error")]
	public void WriteError_ShouldAddTimestamp(string type) {

		// Arrange
		Program.AddTimeStampToOutput = true;
		StringBuilder stringBuilder = new StringBuilder();
		StringWriter textWriter = new System.IO.StringWriter(stringBuilder);
		Console.SetOut(textWriter);
		Console.SetError(textWriter);

		ILogger logger = ConsoleLogger.Instance;

		// Act
		var timeStamp = DateTime.Now;
		logger.Start();
		switch (type) {
			case "inf":
				logger.WriteInfo("Test info");
				break;
			case "warn":
				logger.WriteWarning("Test warning");
				break;
			case "error":
				logger.WriteError("Test error");
				break;
		}
		Thread.Sleep(300);

		// Assert
		string consoleText = stringBuilder.ToString();
		AssertTimeStamp(timeStamp, consoleText);
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
		var textWriter = new System.IO.StringWriter(stringBuilder);
		Console.SetOut(textWriter);
		Console.SetError(textWriter);

		var logger = ConsoleLogger.Instance;

		// Act
		logger.Start();
		switch (type) {
			case "inf":
				logger.WriteInfo("Test info");
				break;
			case "warn":
				logger.WriteWarning("Test warning");
				break;
			case "error":
				logger.WriteError("Test error");
				break;
		}
		Thread.Sleep(300);

		// Assert
		var consoleText = stringBuilder.ToString();
		consoleText.Should().StartWith("[");
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

}
