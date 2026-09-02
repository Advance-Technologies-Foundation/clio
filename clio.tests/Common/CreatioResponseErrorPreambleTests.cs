using System;
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

		//Warm the classifier first: the very first call pays one-off JIT and static-initialization cost
		//that has nothing to do with the shape being measured.
		CreatioResponseError.IsMarkup(body);
		CreatioResponseError.IsKnownErrorPage(body);

		// Act
		//Per-thread, not per-process. This assembly runs its fixtures in parallel, so
		//GC.GetTotalAllocatedBytes counts every unrelated fixture's allocations too - the same unchanged
		//span implementation measured about 59 MB under concurrent load and failed a ~1 MB budget.
		long before = GC.GetAllocatedBytesForCurrentThread();
		bool markup = CreatioResponseError.IsMarkup(body);
		bool knownErrorPage = CreatioResponseError.IsKnownErrorPage(body);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		// Assert
		markup.Should().BeTrue(because: "the first real tag after the preambles is <html>");
		knownErrorPage.Should().BeTrue(because: "the stripped body still carries the platform's own wording");
		allocated.Should().BeLessThan(body.Length * 4L,
			because: $"skipping a preamble must move an offset, not copy the remaining {body.Length} characters - the copying form allocated about 125 MB for a body this shape, and call-service normalizes the same body twice");
	}

	[TestCase(" ﻿<!DOCTYPE html><title>Request Error</title>",
		TestName = "Whitespace before the BOM")]
	[TestCase("﻿ <!DOCTYPE html><title>Request Error</title>",
		TestName = "BOM before the whitespace")]
	[TestCase("<?xml version=\"1.0\"?>﻿<html><title>Request Error</title></html>",
		TestName = "BOM after a processing instruction")]
	[TestCase("<?xml version=\"1.0\"?> ﻿ <?xml-stylesheet href=\"a.xsl\"?>​<html>x</html>",
		TestName = "Zero-width and whitespace interleaved between two processing instructions")]
	[Description("Whitespace and zero-width characters are trimmed in any order and after every processing instruction, so an IIS error page cannot slip through as successful plain text")]
	public void IsMarkup_ShouldSeeThroughInterleavedBlanks(string body) {
		// Act
		bool markup = CreatioResponseError.IsMarkup(body);

		// Assert
		markup.Should().BeTrue(
			because: "trimming each kind of blank only once left the other in front of the first tag, and "
				+ "the error page was then saved as a successful answer");
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
