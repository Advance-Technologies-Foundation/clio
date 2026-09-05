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


	[TestCase("<!DOCTYPE html><html><head><title>404 - File or directory not found.</title></head></html>", 404,
		TestName = "IIS 404 page")]
	[TestCase("<html><head><TITLE>405 - HTTP verb used to access this page is not allowed.</TITLE></head></html>", 405,
		TestName = "IIS 405 page with an upper-case title tag")]
	[TestCase("<html><head><title>  503  -  Service Unavailable</title></head></html>", 503,
		TestName = "padded status in the title")]
	[TestCase("<html><head><title>401 : Unauthorized</title></head></html>", 401,
		TestName = "colon separator")]
	[TestCase("<html><head><title>HTTP Error 500.0 - Internal Server Error</title></head><body><h1>HTTP Error 500.0 - Internal Server Error</h1></body></html>", 500,
		TestName = "IIS detailed HTTP Error form with a sub-status")]
	[TestCase("<html><head><title>502 Bad Gateway</title></head><body><center><h1>502 Bad Gateway</h1></center><hr><center>nginx</center></body></html>", 502,
		TestName = "nginx page with no separator at all")]
	[TestCase("<html><head><title>404 Not Found</title></head></html>", 404,
		TestName = "default nginx/Apache 404 with no separator")]
	[TestCase("<html><head><title lang=\"en\">404 - File or directory not found.</title></head></html>", 404,
		TestName = "title tag carrying attributes")]
	[TestCase("<html><head><title>404</title></head></html>", 404,
		TestName = "status immediately followed by the closing tag")]
	[Description("Reads the HTTP status out of an IIS-style error page title, which is the only place it is available because the transport returns the body alone")]
	public void TryGetMarkupErrorStatusCode_ShouldRead_TheStatusFromThePageTitle(string body, int expected) {
		// Arrange
		// The body is the arranged input.

		// Act
		bool detected = CreatioResponseError.TryGetMarkupErrorStatusCode(body, out int statusCode);

		// Assert
		detected.Should().BeTrue(
			because: "an IIS error page states its status in the title and nowhere else in the response clio can see");
		statusCode.Should().Be(expected,
			because: "the status is what a caller keys the documented async-gap retry off");
	}

	[TestCase("<html><head><title>Request Error</title></head></html>", TestName = "title with no status")]
	[TestCase("<html><head><title>2026 - Report</title></head></html>", TestName = "a number outside the HTTP status range")]
	[TestCase("<html><head><title>200 - OK</title></head></html>", TestName = "a success status is never a failure status")]
	[TestCase("<html><head><title>302 - Found</title></head></html>", TestName = "a redirect status is not a failure status")]
	[TestCase("<html><head><title>Web Application Firewall - Request Blocked</title></head></html>", TestName = "a WAF page that names no status")]
	[TestCase("{\"value\":[]}", TestName = "an ordinary JSON body")]
	[TestCase("", TestName = "an empty body")]
	[Description("Reports no status rather than inventing one when the body carries no HTTP status in its title")]
	public void TryGetMarkupErrorStatusCode_ShouldReportNothing_WhenThereIsNoStatusInTheTitle(string body) {
		// Arrange
		// The body is the arranged input.

		// Act
		bool detected = CreatioResponseError.TryGetMarkupErrorStatusCode(body, out int statusCode);

		// Assert
		detected.Should().BeFalse(
			because: "a status a caller could act on must never be guessed from a page that does not state one");
		statusCode.Should().Be(0,
			because: "the out value stays at its default when nothing was parsed");
	}

	[Test]
	[Description("Classifies an HTML 404 as an entity that is not exposed over OData and repeats the shared async-gap hint")]
	public void TryDescribeMarkupErrorResponse_ShouldName_TheEntityAndTheAsyncGapHint_ForA404() {
		// Arrange
		string body = "<!DOCTYPE html><html><head><title>404 - File or directory not found.</title></head><body>iis</body></html>";

		// Act
		bool detected = CreatioResponseError.TryDescribeMarkupErrorResponse(body, "UsrThing", out string message,
			out int? statusCode);

		// Assert
		detected.Should().BeTrue(
			because: "an HTML page is never an OData response and must be classified before JSON parsing is attempted");
		statusCode.Should().Be(404,
			because: "the 404 is what distinguishes the transient rebuild window from a permanently unexposed entity");
		message.Should().Contain("UsrThing",
			because: "the failure has to name the entity the caller asked for");
		message.Should().Contain(CreatioResponseError.UnregisteredEntityHint,
			because: "the HTML 404 and the JSON routing 404 are the same condition and must share one locally authored hint");
		message.Should().NotContain("File or directory not found",
			because: "no fragment of a server or proxy page may be copied into an MCP transcript");
	}

	[Test]
	[Description("Does not claim a JSON OData body as an HTML error page")]
	public void TryDescribeMarkupErrorResponse_ShouldNotClaim_AJsonBody() {
		// Arrange
		string body = "{\"@odata.context\":\"http://creatio/odata/$metadata#Contact\",\"value\":[]}";

		// Act
		bool detected = CreatioResponseError.TryDescribeMarkupErrorResponse(body, "Contact", out string _, out int? _);

		// Assert
		detected.Should().BeFalse(
			because: "a genuine OData payload must reach the JSON classification path untouched");
	}

	[Test]
	[Description("Reports the HTML page's HTTP status in the write-path diagnostic, which is the only place a write caller can learn which hop answered")]
	public void DescribeNonJsonResponse_ShouldName_TheHttpStatusOfTheErrorPage() {
		// Arrange
		string body = "<html><head><title>401 - Unauthorized: Access is denied due to invalid credentials.</title></head></html>";

		// Act
		string message = CreatioResponseError.DescribeNonJsonResponse(body);

		// Assert
		message.Should().Contain("HTTP 401 error page",
			because: "the write transport never throws and never exposes a status, so the page title is the only "
				+ "signal that separates an auth failure from a routing failure");
		message.Should().Contain(CreatioResponseError.NonJsonResponseHint,
			because: "naming the status must not replace the locally authored explanation of what a non-JSON body means");
	}

	[Test]
	[Description("Omits the status sentence entirely when the non-JSON body states no HTTP status, rather than guessing one")]
	public void DescribeNonJsonResponse_ShouldOmit_TheStatusSentence_WhenThereIsNoStatus() {
		// Arrange
		const string body = "IIS: The request could not be mapped to an application.";

		// Act
		string message = CreatioResponseError.DescribeNonJsonResponse(body);

		// Assert
		message.Should().NotContain("error page",
			because: "a status sentence for a body that states no status would be an invention");
		message.Should().Contain("did not return a JSON response",
			because: "the caller still has to learn the body was not JSON");
	}

	[Test]
	[Description("An HTML page that states no HTTP status gets the neutral non-JSON read diagnosis, never the execute-esq steer that only applies to a 404")]
	public void TryDescribeMarkupErrorResponse_ShouldStayNeutral_WhenThePageStatesNoStatus() {
		// Arrange
		//Creatio's own outage page. An SSO/proxy login page has the same property: it says nothing
		//about whether the entity has an OData controller.
		const string body = "<html><head><title>Request Error</title></head><body>outage</body></html>";

		// Act
		bool detected = CreatioResponseError.TryDescribeMarkupErrorResponse(body, "Contact", out string message,
			out int? statusCode);

		// Assert
		detected.Should().BeTrue(
			because: "an HTML body is still not an OData response and must not reach the JSON parser");
		statusCode.Should().BeNull(
			because: "the page names no status and one must never be invented");
		message.Should().NotContain("execute-esq",
			because: "an outage page or a login page says nothing about whether the entity is exposed over OData, "
				+ "and steering the caller onto another tool costs them the real cause");
		message.Should().NotContain(CreatioResponseError.UnregisteredEntityHint,
			because: "waiting out a non-existent OData rebuild would delay diagnosing an outage or an expired session");
		message.Should().Be(CreatioResponseError.DescribeNonJsonReadResponse(),
			because: "with no status there is nothing to add to the neutral non-JSON diagnosis");
	}

	[Test]
	[Description("A non-404 HTML error page states only what was observed and does not claim the request never reached a Creatio OData controller")]
	public void TryDescribeMarkupErrorResponse_ShouldStateOnlyWhatWasObserved_ForANon404() {
		// Arrange
		const string body = "<html><head><title>502 Bad Gateway</title></head><body>nginx</body></html>";

		// Act
		CreatioResponseError.TryDescribeMarkupErrorResponse(body, "Contact", out string message, out int? statusCode);

		// Assert
		statusCode.Should().Be(502,
			because: "the status is what tells the caller this is an infrastructure hop rather than a query problem");
		message.Should().Contain("HTTP 502 error page",
			because: "the observed fact is that an error page came back with that status");
		message.Should().NotContain("never reached",
			because: "clio saw only the body: a gateway may well have reached Creatio and failed on the way back, "
				+ "so asserting where the request stopped is a claim the evidence does not support");
		message.Should().NotContain("Bad Gateway",
			because: "no fragment of a proxy page may be copied into an MCP transcript");
	}
}
