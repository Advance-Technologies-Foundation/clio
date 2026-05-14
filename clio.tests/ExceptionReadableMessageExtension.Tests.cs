using System;
using System.IO;
using System.Net;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
[Property("Module", "Core")]
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
	public void GetReadableMessageException_PrintsCorrectMessage_WhenInvalidOperationException() {
		var innerException = new Exception("InnerMessage");
		var exception = new InvalidOperationException("Message", innerException);
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{innerException.Message}");
	}

	[Test]
	public void
		GetReadableMessageException_PrintsCorrectMessage_WhenInvalidOperationExceptionWithoutInnerException() {
		var exception = new InvalidOperationException("Message");
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{exception.Message}");
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenInvalidOperationExceptionAndSetsDebug() {
		var innerException = new Exception("InnerMessage");
		var exception = new InvalidOperationException("Message", innerException);
		var messageResult = exception.GetReadableMessageException(true);
		messageResult.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_PrintsFriendlyMessage_WhenWebExceptionConnectFailure() {
		var exception = new WebException("Connection refused (localhost:1616)", WebExceptionStatus.ConnectFailure);
		var result = exception.GetReadableMessageException();
		result.Should().StartWith("Cannot connect to the application:");
		result.Should().Contain("Make sure the site is running and accessible");
		result.Should().NotContain("   at ");
	}

	[Test]
	public void GetReadableMessageException_PrintsFullStackTrace_WhenWebExceptionConnectFailureAndDebugMode() {
		WebException exception;
		try { throw new WebException("Connection refused (localhost:1616)", WebExceptionStatus.ConnectFailure); }
		catch (WebException e) { exception = e; }
		var result = exception.GetReadableMessageException(debug: true);
		result.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_PrintsMessage_WhenWebExceptionOtherStatus() {
		var exception = new WebException("Not Found", WebExceptionStatus.ProtocolError);
		var result = exception.GetReadableMessageException();
		result.Should().Be(exception.Message);
		result.Should().NotContain("Cannot connect to the application");
	}
}
