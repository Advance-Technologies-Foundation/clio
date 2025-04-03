using System;
using NUnit.Framework;
using NSubstitute;
using cliogate.Files.cs;
using Cliogate.Services;

namespace cliogate.tests
{
	[TestFixture]
	public class LiveLoggerTests
	{
		private IWebSocket _mockWebSocket;
		private LiveLogger _liveLogger;

		[SetUp]
		public void Setup()
		{
			_mockWebSocket = Substitute.For<IWebSocket>();
			_liveLogger = new LiveLogger(_mockWebSocket);
		}

		[Test]
		public void Log_InfoMessage_UsesLogInfo()
		{
			// Arrange
			string message = "[INF] - This is an info message";

			// Act
			_liveLogger.Log(message);

			// Assert
			_mockWebSocket.Received(1).PostMessageToAll("Clio", "Show logs", "<span style='color:green'>[INF]</span> - This is an info message");
		}

		[Test]
		public void Log_ErrorMessage_UsesLogError()
		{
			// Arrange
			string message = "[ERR] - This is an error message";

			// Act
			_liveLogger.Log(message);

			// Assert
			_mockWebSocket.Received(1).PostMessageToAll("Clio", "Show logs", "<span style='color:red'>[ERR]</span> - This is an error message");
		}

		[Test]
		public void Log_WarnMessage_UsesLogWarn()
		{
			// Arrange
			string message = "[WAR] - This is a warning message";

			// Act
			_liveLogger.Log(message);

			// Assert
			_mockWebSocket.Received(1).PostMessageToAll("Clio", "Show logs", "<span style='color:yellow'>[WAR]</span> - This is a warning message");
		}

		[Test]
		public void Log_WarnMessage_UsesLogWarn_WithoutDash() {
			// Arrange
			string message = "[WAR] This is a warning message";

			// Act
			_liveLogger.Log(message);

			// Assert
			_mockWebSocket.Received(1).PostMessageToAll("Clio", "Show logs", "<span style='color:yellow'>[WAR]</span> - This is a warning message");
		}

		[Test]
		public void Log_RegularMessage_UsesPostMessageToAll()
		{
			// Arrange
			string message = "This is a regular message";

			// Act
			_liveLogger.Log(message);

			// Assert
			_mockWebSocket.Received(1).PostMessageToAll("Clio", "Show logs", message);
		}
	}
}
