using System;
using System.Text;
using System.Threading;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Clio.Tests.Common
{
	[TestFixture]
	internal class ConsoleLoggerTests
	{

		[TestCase("inf")]
		[TestCase("warn")]
		[TestCase("error")]
		public void WriteError_ShouldAddTimestamp(string type) {

			// Arrange
			Program.AddTimeStampToOutput = true;
			var stringBuilder = new StringBuilder();
			var textWriter = new System.IO.StringWriter(stringBuilder);
			Console.SetOut(textWriter);

			var logger = ConsoleLogger.Instance;

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
			var consoleText = stringBuilder.ToString();
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
			logger.LogFileWriter = Substitute.For<System.IO.TextWriter>();
			logger.Dispose();
			logger.LogFileWriter.Received().Dispose();
		}
	}
}
