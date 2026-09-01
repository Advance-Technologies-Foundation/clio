using System;
using System.Diagnostics;
using System.Text;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CreatioResponseErrorPreambleTests
{

	// Enough prefixes that a per-iteration copy of the remainder is unmistakable: the string form copied
	// the whole rest of the body each time, so this body alone allocated on the order of 100 MB.
	private const int PreambleCount = 5_000;

	private static string BuildManyPreambleBody(string payload) {
		StringBuilder builder = new();
		for (int index = 0; index < PreambleCount; index++) {
			builder.Append("<?xml-stylesheet type=\"text/xsl\" href=\"a.xsl\"?>");
		}
		return builder.Append(payload).ToString();
	}

	[Test]
	[Description("Skips every processing instruction without copying the remainder, so a crafted body with thousands of preambles stays linear instead of costing quadratic time and allocation before classification")]
	public void StripMarkupPreamble_ShouldStayLinear_WhenBodyCarriesManyProcessingInstructions() {
		// Arrange
		string body = BuildManyPreambleBody("<html><title>Request Error</title></html>");

		// Act
		long before = GC.GetTotalAllocatedBytes(precise: true);
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool markup = CreatioResponseError.IsMarkup(body);
		bool knownErrorPage = CreatioResponseError.IsKnownErrorPage(body);
		stopwatch.Stop();
		long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

		// Assert
		markup.Should().BeTrue(because: "the first real tag after the preambles is <html>");
		knownErrorPage.Should().BeTrue(because: "the stripped body still carries the platform's own wording");
		allocated.Should().BeLessThan(body.Length * 4L,
			because: $"skipping a preamble must move an offset, not copy the remaining {body.Length} characters - the copying form allocated about 125 MB for a body this shape, and call-service normalizes the same body twice");
		stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
			because: "a small adversarial response must not buy quadratic work before it is even classified");
	}

	[Test]
	[Description("Returns exactly the text after the preambles, so the offset-based skip is behaviour-preserving")]
	public void StripMarkupPreamble_ShouldReturnTheTextAfterThePreambles() {
		// Arrange
		string body = "﻿  <?xml version=\"1.0\"?> <?xml-stylesheet href=\"a.xsl\"?>  <html>ok</html>";

		// Act
		string stripped = CreatioResponseError.StripMarkupPreamble(body);

		// Assert
		stripped.Should().Be("<html>ok</html>",
			because: "a byte-order mark, whitespace and every processing instruction are preamble, and nothing after the first real tag is touched");
	}

	[Test]
	[Description("Leaves an unterminated processing instruction in place rather than discarding the rest of the body")]
	public void StripMarkupPreamble_ShouldStop_WhenAProcessingInstructionIsUnterminated() {
		// Arrange
		string body = "<?xml version=\"1.0\"";

		// Act
		string stripped = CreatioResponseError.StripMarkupPreamble(body);

		// Assert
		stripped.Should().Be(body,
			because: "a truncated preamble has no end to skip past, so the body must be classified as it arrived");
	}

}
