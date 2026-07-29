using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class GoogleFontsCatalogTests {

	private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
		public List<string> RequestedUris { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			RequestedUris.Add(request.RequestUri!.AbsoluteUri);
			return Task.FromResult(respond(request));
		}
	}

	private static GoogleFontsCatalog CatalogReturning(HttpStatusCode status, out StubHandler handler) {
		handler = new StubHandler(_ => new HttpResponseMessage(status));
		return new GoogleFontsCatalog(new HttpClient(handler));
	}

	[Test]
	[Description("A family the Google Fonts catalogue serves metadata for is reported as available.")]
	public void Lookup_ShouldReportInCatalog_WhenMetadataFound() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(HttpStatusCode.OK, out _);

		// Act / Assert
		catalog.Lookup("Roboto").Should().Be(GoogleFontAvailability.InCatalog,
			because: "the metadata endpoint answers 200 for a family Google actually hosts");
	}

	[Test]
	[Description("A family the Google Fonts catalogue does not know is reported as missing, which is what drives the confirm-as-local flow.")]
	public void Lookup_ShouldReportNotInCatalog_WhenMetadataMissing() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(HttpStatusCode.NotFound, out _);

		// Act / Assert
		catalog.Lookup("Verdana").Should().Be(GoogleFontAvailability.NotInCatalog,
			because: "404 is the catalogue's answer for a family it does not host, unlike the css2 endpoint which answers 200 with a look-alike substitute");
	}

	[Test]
	[Description("A transport failure or unexpected status is reported as unverified rather than guessed either way.")]
	[TestCase(HttpStatusCode.InternalServerError, TestName = "Lookup_ShouldReportUnverified_ForServerError")]
	[TestCase(HttpStatusCode.Forbidden, TestName = "Lookup_ShouldReportUnverified_ForForbidden")]
	public void Lookup_ShouldReportUnverified_ForUnexpectedStatus(HttpStatusCode status) {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(status, out _);

		// Act / Assert
		catalog.Lookup("Roboto").Should().Be(GoogleFontAvailability.Unverified,
			because: "an inconclusive answer must not be reported as either present or absent, so the agent asks the user instead");
	}

	[Test]
	[Description("A network exception is reported as unverified, so an offline environment never silently classifies a font.")]
	public void Lookup_ShouldReportUnverified_WhenRequestThrows() {
		// Arrange
		StubHandler handler = new(_ => throw new HttpRequestException("no network"));
		GoogleFontsCatalog catalog = new(new HttpClient(handler));

		// Act / Assert
		catalog.Lookup("Roboto").Should().Be(GoogleFontAvailability.Unverified,
			because: "an unreachable catalogue is an unknown answer, not a missing font");
	}

	[Test]
	[Description("A blank family is reported as unverified without issuing a request.")]
	[TestCase(null, TestName = "Lookup_ShouldNotRequest_ForNullFamily")]
	[TestCase("   ", TestName = "Lookup_ShouldNotRequest_ForWhitespaceFamily")]
	public void Lookup_ShouldReportUnverifiedWithoutRequest_ForBlankFamily(string family) {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(HttpStatusCode.OK, out StubHandler handler);

		// Act
		GoogleFontAvailability availability = catalog.Lookup(family);

		// Assert
		availability.Should().Be(GoogleFontAvailability.Unverified,
			because: "there is no family to look up");
		handler.RequestedUris.Should().BeEmpty(
			because: "a blank family must not cost a network round trip");
	}

	[Test]
	[Description("The family is URL-encoded into the metadata path so multi-word families resolve.")]
	public void Lookup_ShouldUrlEncodeTheFamily_ForMultiWordNames() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(HttpStatusCode.OK, out StubHandler handler);

		// Act
		catalog.Lookup("  Playfair Display  ");

		// Assert
		handler.RequestedUris.Should().ContainSingle()
			.Which.Should().Be("https://fonts.google.com/metadata/fonts/Playfair%20Display",
				because: "the family is trimmed and percent-encoded, otherwise a multi-word family would 404 and be misreported as missing");
	}
}
