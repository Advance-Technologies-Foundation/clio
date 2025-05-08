using System;
using System.IO;
using System.Text;
using System.Threading;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests.Common;

[TestFixture]
internal class ConsoleLoggerTests
{
    [TestCase("inf")]
    [TestCase("warn")]
    [TestCase("error")]
    public void WriteError_ShouldAddTimestamp(string type)
    {
        // Arrange
        Program.AddTimeStampToOutput = true;
        StringBuilder stringBuilder = new();
        StringWriter textWriter = new(stringBuilder);
        Console.SetOut(textWriter);

        ILogger logger = ConsoleLogger.Instance;

        // Act
        DateTime timeStamp = DateTime.Now;
        logger.Start();
        switch (type)
        {
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

    public void AssertTimeStamp(DateTime timeStamp, string consoleText)
    {
        DateTime consoleTimeStamp = DateTime.Parse(consoleText.Substring(0, 8));
        (consoleTimeStamp - timeStamp).Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [TestCase("inf")]
    [TestCase("warn")]
    [TestCase("error")]
    public void WriteError_ShouldNotAddTimestamp(string type)
    {
        // Arrange
        Program.AddTimeStampToOutput = false;
        StringBuilder stringBuilder = new();
        StringWriter textWriter = new(stringBuilder);
        Console.SetOut(textWriter);

        ILogger logger = ConsoleLogger.Instance;

        // Act
        logger.Start();
        switch (type)
        {
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
        consoleText.Should().StartWith("[");
    }

    [Test]
    public void Dispose_test()
    {
        ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
        TextWriter? mockLogFileWtiter = Substitute.For<TextWriter>();
        logger.LogFileWriter = mockLogFileWtiter;
        logger.Dispose();
        mockLogFileWtiter.Received().Dispose();
    }

    [Test]
    public void Dispose_WhenLogFileWriterIsNull()
    {
        ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
        logger.LogFileWriter = null;
        Assert.DoesNotThrow(() => logger.Dispose());
    }

    [Test]
    public void Dispose_Twice_WhenLogFileWriterIsNull()
    {
        ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
        logger.LogFileWriter = Substitute.For<TextWriter>();
        logger.Dispose();
        Assert.That(logger.LogFileWriter, Is.Null);
        Assert.DoesNotThrow(() => logger.Dispose());
    }
}
