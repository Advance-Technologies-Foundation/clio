using System;
using System.IO;
using Autofac.Core;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
[Category("Unit")]
public class ExceptionReadableMessageExtensionTestCase
{

    [Test]
    public void GetReadableMessageException_PrintsCorrectMessage_WhenDependencyResolutionException()
    {
        Exception innerException = new("InnerMessage");
        DependencyResolutionException exception = new("Message", innerException);
        string messageResult = exception.GetReadableMessageException();
        messageResult.Should().Be($"{innerException.Message}");
    }

    [Test]
    public void GetReadableMessageException_PrintsCorrectMessage_WhenDependencyResolutionExceptionAndSetsDebug()
    {
        Exception innerException = new("InnerMessage");
        DependencyResolutionException exception = new("Message", innerException);
        string messageResult = exception.GetReadableMessageException(true);
        messageResult.Should().Be(exception.ToString());
    }

    [Test]
    public void
        GetReadableMessageException_PrintsCorrectMessage_WhenDependencyResolutionExceptionWithoutInnerException()
    {
        DependencyResolutionException exception = new("Message");
        string messageResult = exception.GetReadableMessageException();
        messageResult.Should().Be($"{exception.Message}");
    }

    [Test]
    public void GetReadableMessageException_PrintsCorrectMessage_WhenException()
    {
        Exception exception = new("Message");
        string messageResult = exception.GetReadableMessageException();
        messageResult.Should().Be($"{exception.Message}");
    }

    [Test]
    public void GetReadableMessageException_PrintsCorrectMessage_WhenExceptionAndSetsDebug()
    {
        Exception exception = new("Message");
        string messageResult = exception.GetReadableMessageException(true);
        messageResult.Should().Be(exception.ToString());
    }

    [Test]
    public void GetReadableMessageException_PrintsCorrectMessage_WhenFileNotFoundException()
    {
        FileNotFoundException exception = new("FileMessage", "FileName");
        string messageResult = exception.GetReadableMessageException();
        messageResult.Should().Be($"{exception.Message}{exception.FileName}");
    }

    [Test]
    public void GetReadableMessageException_PrintsCorrectMessage_WhenFileNotFoundExceptionAndSetsDebug()
    {
        FileNotFoundException exception = new("FileMessage", "FileName");
        string messageResult = exception.GetReadableMessageException(true);
        messageResult.Should().Be(exception.ToString());
    }

}
