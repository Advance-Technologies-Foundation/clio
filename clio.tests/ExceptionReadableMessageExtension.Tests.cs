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
	public void GetReadableMessageException_PrintsCorrectMessage_WhenFileNotFoundException() {
		var exception = new FileNotFoundException("FileMessage", "FileName");
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{exception.Message}{exception.FileName}");
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenFileNotFoundExceptionAndSetsDebug() {
		var exception = new FileNotFoundException("FileMessage", "FileName");
		var messageResult = exception.GetReadableMessageException(true);
		messageResult.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenException() {
		var exception = new Exception("Message");
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{exception.Message}");
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenExceptionAndSetsDebug() {
		var exception = new Exception("Message");
		var messageResult = exception.GetReadableMessageException(true);
		messageResult.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenDependencyResolutionException() {
		var innerException = new Exception("InnerMessage");
		var exception = new DependencyResolutionException("Message", innerException);
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{innerException.Message}");
	}

	[Test]
	public void
		GetReadableMessageException_PrintsCorrectMessage_WhenDependencyResolutionExceptionWithoutInnerException() {
		var exception = new DependencyResolutionException("Message");
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{exception.Message}");
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenDependencyResolutionExceptionAndSetsDebug() {
		var innerException = new Exception("InnerMessage");
		var exception = new DependencyResolutionException("Message", innerException);
		var messageResult = exception.GetReadableMessageException(true);
		messageResult.Should().Be(exception.ToString());
	}
}