using System;
using System.Text.Json;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Covers ENG-93365 message selection. Both MCP error paths (the call-tool filter and the nested
/// <c>clio-run</c> dispatcher) delegate here, so pinning this one type keeps them from drifting apart.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class SurfacedExceptionMessageTests
{
	private const string RawParserFragment = "is an invalid start of a value";

	[Test]
	[Description("Resolve unwraps a dispatch wrapper to the inner-most cause so a generic wrapper never hides the real failure.")]
	public void Resolve_ReturnsInnermostMessage_WhenExceptionIsWrapped() {
		// Arrange
		InvalidOperationException exception = new(
			"Outer wrapper message.",
			new InvalidOperationException("Environment with key 'NoSuchEnv' not found."));

		// Act
		string message = SurfacedExceptionMessage.Resolve(exception);

		// Assert
		message.Should().Be("Environment with key 'NoSuchEnv' not found.",
			"an unmarked wrapper must yield the inner-most cause the caller can act on");
	}

	[Test]
	[Description("Resolve keeps an authoritative exception's own message instead of the inner parser text it replaces.")]
	public void Resolve_KeepsAuthoritativeMessage_WhenExceptionCarriesTheMarker() {
		// Arrange
		AuthoritativeException exception = new(
			"SelectQuery returned an HTML page instead of JSON (URL: endpoint).",
			new JsonException("'<' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0."));

		// Act
		string message = SurfacedExceptionMessage.Resolve(exception);

		// Assert
		message.Should().Contain("HTML page instead of JSON",
			"the classified message was written for the caller and must not be replaced");
		message.Should().NotContain(RawParserFragment,
			"the raw parser text the classified message replaces must never be surfaced (ENG-93365)");
	}

	[Test]
	[Description("Resolve stops at the outermost authoritative exception even when it is itself wrapped by dispatch machinery.")]
	public void Resolve_StopsAtAuthoritativeMessage_WhenItIsWrappedByDispatchMachinery() {
		// Arrange
		InvalidOperationException exception = new(
			"Dispatch wrapper.",
			new AuthoritativeException(
				"SelectQuery returned an unparseable response (URL: endpoint).",
				new JsonException("'<' is an invalid start of a value.")));

		// Act
		string message = SurfacedExceptionMessage.Resolve(exception);

		// Assert
		message.Should().Contain("unparseable response",
			"the walk must reach the authoritative exception through the wrapper and then stop there");
		message.Should().NotContain(RawParserFragment,
			"the walk must not continue past the authoritative exception into the parser detail");
	}

	[Test]
	[Description("Resolve returns the message of an exception that has no inner exception.")]
	public void Resolve_ReturnsOwnMessage_WhenExceptionHasNoInnerException() {
		// Arrange
		InvalidOperationException exception = new("Package 'UsrApp' is locked.");

		// Act
		string message = SurfacedExceptionMessage.Resolve(exception);

		// Assert
		message.Should().Be("Package 'UsrApp' is locked.",
			"a single exception carries its own message unchanged");
	}

	[Test]
	[Description("Resolve rejects a null exception instead of surfacing an empty message.")]
	public void Resolve_ThrowsArgumentNullException_WhenExceptionIsNull() {
		// Act
		Action act = () => SurfacedExceptionMessage.Resolve(null!);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			"a missing exception is a programming error, not a message to surface");
	}

	private sealed class AuthoritativeException : InvalidOperationException, IAuthoritativeErrorMessage
	{
		public AuthoritativeException(string message, Exception innerException)
			: base(message, innerException) {
		}
	}
}
