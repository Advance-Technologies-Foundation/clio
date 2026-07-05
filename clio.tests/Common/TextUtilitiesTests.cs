namespace Clio.Tests.Common;

using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Common")]
public sealed class TextUtilitiesTests
{
	[Test]
	[Category("Unit")]
	[Description("Replaces every control character (newline, carriage return, tab, ANSI escape) with a space so untrusted text cannot forge extra output lines or inject terminal escape sequences.")]
	public void SanitizeForDisplay_ShouldReplaceControlCharactersWithSpaces_WhenTextContainsThem() {
		// Arrange
		const string text = "line1\r\nFAKE\tSUCCESS[31mred[0m";

		// Act
		string sanitized = TextUtilities.SanitizeForDisplay(text);

		// Assert
		sanitized.Should().NotContain("\n", because: "newlines that would forge extra output lines must be neutralised");
		sanitized.Should().NotContain("\r", because: "carriage returns that would forge extra output lines must be neutralised");
		sanitized.Should().NotContain("\t", because: "tabs are control characters and must be neutralised");
		sanitized.Should().NotContain("", because: "ANSI escape sequences must be neutralised before reaching a terminal");
		sanitized.Should().Contain("line1 ", because: "visible content must be preserved with control characters replaced by spaces");
	}

	[Test]
	[Category("Unit")]
	[Description("Caps text longer than the maximum length and appends an ellipsis so a large payload cannot flood the output.")]
	public void SanitizeForDisplay_ShouldTruncateAndAppendEllipsis_WhenTextExceedsMaxLength() {
		// Arrange
		string text = new('a', 5000);

		// Act
		string sanitized = TextUtilities.SanitizeForDisplay(text, maxLength: 500);

		// Assert
		sanitized.Should().HaveLength(503, because: "the 500-character cap plus a three-character ellipsis bounds the output");
		sanitized.Should().EndWith("...", because: "a truncated value must be marked as elided");
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves text at or below the maximum length unchanged so short, control-character-free bodies are surfaced verbatim.")]
	public void SanitizeForDisplay_ShouldReturnTextUnchanged_WhenWithinMaxLengthAndNoControlCharacters() {
		// Arrange
		const string text = "no permission";

		// Act
		string sanitized = TextUtilities.SanitizeForDisplay(text);

		// Assert
		sanitized.Should().Be(text, because: "a short, clean body needs no sanitisation");
	}

	[Test]
	[Category("Unit")]
	[TestCase(null, TestName = "SanitizeForDisplay returns null for null input")]
	[TestCase("", TestName = "SanitizeForDisplay returns empty for empty input")]
	[Description("Returns null or empty input unchanged so callers can interpolate the result without a null guard.")]
	public void SanitizeForDisplay_ShouldReturnInputUnchanged_WhenNullOrEmpty(string text) {
		// Act
		string sanitized = TextUtilities.SanitizeForDisplay(text);

		// Assert
		sanitized.Should().Be(text, because: "there is nothing to sanitise in null or empty input");
	}
}
