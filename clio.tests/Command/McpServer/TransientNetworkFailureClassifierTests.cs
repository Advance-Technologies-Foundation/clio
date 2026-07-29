using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class TransientNetworkFailureClassifierTests {

	[Test]
	[Category("Unit")]
	[Description("Classifies typed transport exceptions (DNS/reset/timeout family) as transient.")]
	public void IsTransient_Should_Return_True_When_Typed_Transport_Exception() {
		// Arrange
		Exception[] transportExceptions = [
			new HttpRequestException("boom"),
			new SocketException(),
			new WebException("boom"),
			new TimeoutException("boom"),
			new TaskCanceledException("boom"),
			new IOException("boom")
		];

		// Act & Assert
		foreach (Exception exception in transportExceptions) {
			TransientNetworkFailureClassifier.IsTransient(exception).Should().BeTrue(
				because: $"{exception.GetType().Name} is a transient network-level fault worth retrying");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Walks inner exceptions so a transient fault wrapped by another exception is still transient.")]
	public void IsTransient_Should_Return_True_When_Transient_Fault_Is_An_Inner_Exception() {
		// Arrange
		var wrapped = new InvalidOperationException("outer", new SocketException());

		// Act
		bool result = TransientNetworkFailureClassifier.IsTransient(wrapped);

		// Assert
		result.Should().BeTrue(
			because: "the transient SocketException nested inside the outer exception must be detected");
	}

	[Test]
	[Category("Unit")]
	[Description("Walks the flattened AggregateException tree so a transient inner fault is detected.")]
	public void IsTransient_Should_Return_True_When_Aggregate_Wraps_A_Transient_Fault() {
		// Arrange
		var aggregate = new AggregateException(
			new InvalidOperationException("unrelated"),
			new HttpRequestException("No such host is known."));

		// Act
		bool result = TransientNetworkFailureClassifier.IsTransient(aggregate);

		// Assert
		result.Should().BeTrue(
			because: "the transient HttpRequestException inside the AggregateException must be detected");
	}

	[Test]
	[Category("Unit")]
	[Description("Does NOT classify a non-network business exception as transient.")]
	public void IsTransient_Should_Return_False_When_Business_Exception() {
		// Arrange
		var businessError = new InvalidOperationException("Schema validation failed: column already exists.");

		// Act
		bool result = TransientNetworkFailureClassifier.IsTransient(businessError);

		// Assert
		result.Should().BeFalse(
			because: "a server-side validation/business error must fail fast, not be retried");
	}

	[Test]
	[Category("Unit")]
	[Description("Never classifies an unrecoverable exception (programming defect) as transient.")]
	public void IsTransient_Should_Return_False_When_Unrecoverable_Exception() {
		// Arrange
		var defect = new NullReferenceException("object reference not set");

		// Act
		bool result = TransientNetworkFailureClassifier.IsTransient(defect);

		// Assert
		result.Should().BeFalse(
			because: "a programming defect must propagate, never be masked as a retryable transient fault");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a null exception.")]
	public void IsTransient_Should_Return_False_When_Null() {
		// Arrange & Act
		bool result = TransientNetworkFailureClassifier.IsTransient(null);

		// Assert
		result.Should().BeFalse(
			because: "there is nothing to retry when no exception was raised");
	}

	[TestCase("One or more errors occurred. (No such host is known.)")]
	[TestCase("Name or service not known")]
	[TestCase("An existing connection was forcibly closed by the remote host")]
	[TestCase("Connection refused")]
	[TestCase("The operation has timed out")]
	[TestCase("An error occurred while sending the request.")]
	[TestCase("Response status code does not indicate success: 503 (Service Unavailable).")]
	[TestCase("502 Bad Gateway")]
	[Category("Unit")]
	[Description("Recognizes known transient network failure markers in a logged error message (case-insensitive).")]
	public void IsTransientErrorMessage_Should_Return_True_When_Message_Matches_A_Transient_Marker(string message) {
		// Act
		bool result = TransientNetworkFailureClassifier.IsTransientErrorMessage(message);

		// Assert
		result.Should().BeTrue(
			because: $"'{message}' names a transient network-level failure and should be retried");
	}

	[TestCase("Schema UsrGenre already exists in package UsrApp.")]
	[TestCase("Compilation failed: CS1002 ; expected.")]
	[TestCase("Compilation failed: CS0503 ; the modifier is not valid.")]
	[TestCase("Schema Usr503Report already exists in package UsrApp.")]
	[TestCase("Inserted 502 rows; 504 duplicates skipped.")]
	[TestCase("Record 502e1d3a-0000-0000-0000-000000000504 not found.")]
	[TestCase("")]
	[TestCase(null)]
	[Category("Unit")]
	[Description("Does not classify business/validation/compilation messages, or durable messages carrying incidental 5xx-like digit runs (CS0503, schema names, record counts, GUID fragments), or empty input, as transient.")]
	public void IsTransientErrorMessage_Should_Return_False_When_Message_Is_Not_Transient(string? message) {
		// Act
		bool result = TransientNetworkFailureClassifier.IsTransientErrorMessage(message);

		// Assert
		result.Should().BeFalse(
			because: "only transient network-level failures are retried; durable errors must fail fast");
	}
}
