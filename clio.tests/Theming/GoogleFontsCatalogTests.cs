using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
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

	private sealed class ThrowingHandler(Func<Exception> fault) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			throw fault();
	}

	private sealed class FakeTimeProvider : TimeProvider {
		private DateTimeOffset _now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

		public override DateTimeOffset GetUtcNow() => _now;

		public void Advance(TimeSpan delta) => _now += delta;
	}

	private static HttpResponseMessage JsonResponse(HttpStatusCode status) =>
		new(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

	private static GoogleFontsCatalog CatalogReturning(
		Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler, TimeProvider timeProvider = null) {
		handler = new StubHandler(respond);
		return new GoogleFontsCatalog(new HttpClient(handler), new GoogleFontsAvailabilityCache(timeProvider ?? new FakeTimeProvider()));
	}

	[Test]
	[Description("A family the Google Fonts catalogue serves JSON metadata for is reported as available.")]
	public async Task LookupAsync_ShouldReportInCatalog_WhenMetadataFound() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(_ => JsonResponse(HttpStatusCode.OK), out _);

		// Act / Assert
		(await catalog.LookupAsync("Roboto", CancellationToken.None)).Should().Be(GoogleFontAvailability.InCatalog,
			because: "the metadata endpoint answers 200 with JSON for a family Google actually hosts");
	}

	[Test]
	[Description("A 200 that is not JSON (consent page, bot check, SPA shell) is inconclusive, not a published family.")]
	public async Task LookupAsync_ShouldReportUnverified_WhenSuccessIsNotJson() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(
			_ => new HttpResponseMessage(HttpStatusCode.OK) {
				Content = new StringContent("<html></html>", Encoding.UTF8, "text/html")
			}, out _);

		// Act / Assert
		(await catalog.LookupAsync("Roboto", CancellationToken.None)).Should().Be(GoogleFontAvailability.Unverified,
			because: "the endpoint is an undocumented contract, so only a JSON success may claim the family is published");
	}

	[Test]
	[Description("A family the Google Fonts catalogue does not know is reported as missing, which is what suppresses its @import.")]
	public async Task LookupAsync_ShouldReportNotInCatalog_WhenMetadataMissing() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out _);

		// Act / Assert
		(await catalog.LookupAsync("Verdana", CancellationToken.None)).Should().Be(GoogleFontAvailability.NotInCatalog,
			because: "404 is the catalogue's answer for a family it does not host, unlike the css2 endpoint which answers 200 with a look-alike substitute");
	}

	[Test]
	[Description("A transport failure or unexpected status is reported as unverified rather than guessed either way.")]
	[TestCase(HttpStatusCode.InternalServerError, TestName = "LookupAsync_ShouldReportUnverified_ForServerError")]
	[TestCase(HttpStatusCode.Forbidden, TestName = "LookupAsync_ShouldReportUnverified_ForForbidden")]
	[TestCase(HttpStatusCode.Found, TestName = "LookupAsync_ShouldReportUnverified_ForRedirect")]
	[TestCase(HttpStatusCode.MovedPermanently, TestName = "LookupAsync_ShouldReportUnverified_ForPermanentRedirect")]
	[TestCase(HttpStatusCode.NoContent, TestName = "LookupAsync_ShouldReportUnverified_ForSuccessWithoutJsonBody")]
	public async Task LookupAsync_ShouldReportUnverified_ForUnexpectedStatus(HttpStatusCode status) {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(_ => new HttpResponseMessage(status), out _);

		// Act / Assert
		(await catalog.LookupAsync("Roboto", CancellationToken.None)).Should().Be(GoogleFontAvailability.Unverified,
			because: "an inconclusive answer must not be reported as either present or absent — the import is kept and a warning says so");
	}

	[Test]
	[Description("A network exception or an elapsed probe budget is reported as unverified, so an offline environment never silently classifies a font.")]
	[TestCase(typeof(HttpRequestException), TestName = "LookupAsync_ShouldReportUnverified_WhenRequestThrows")]
	[TestCase(typeof(TaskCanceledException), TestName = "LookupAsync_ShouldReportUnverified_WhenProbeTimesOut")]
	public async Task LookupAsync_ShouldReportUnverified_WhenTransportFails(Type faultType) {
		// Arrange
		ThrowingHandler handler = new(() => (Exception)Activator.CreateInstance(faultType, "no network"));
		GoogleFontsCatalog catalog = new(new HttpClient(handler), new GoogleFontsAvailabilityCache(new FakeTimeProvider()));

		// Act / Assert
		(await catalog.LookupAsync("Roboto", CancellationToken.None)).Should().Be(GoogleFontAvailability.Unverified,
			because: "an unreachable catalogue is an unknown answer, not a missing font");
	}

	[Test]
	[Description("A caller-requested cancellation propagates instead of being swallowed into an availability verdict.")]
	public async Task LookupAsync_ShouldPropagateCancellation_WhenCallerCancels() {
		// Arrange
		using CancellationTokenSource cts = new();
		await cts.CancelAsync();
		ThrowingHandler handler = new(() => new OperationCanceledException(cts.Token));
		GoogleFontsCatalog catalog = new(new HttpClient(handler), new GoogleFontsAvailabilityCache(new FakeTimeProvider()));

		// Act
		Func<Task> act = () => catalog.LookupAsync("Roboto", cts.Token);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "only the probe's own budget expiry maps to Unverified — the caller's cancellation is theirs to observe");
	}

	[Test]
	[Description("A blank family is reported as unverified without issuing a request.")]
	[TestCase(null, TestName = "LookupAsync_ShouldNotRequest_ForNullFamily")]
	[TestCase("   ", TestName = "LookupAsync_ShouldNotRequest_ForWhitespaceFamily")]
	public async Task LookupAsync_ShouldReportUnverifiedWithoutRequest_ForBlankFamily(string family) {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(_ => JsonResponse(HttpStatusCode.OK), out StubHandler handler);

		// Act
		GoogleFontAvailability availability = await catalog.LookupAsync(family, CancellationToken.None);

		// Assert
		availability.Should().Be(GoogleFontAvailability.Unverified,
			because: "there is no family to look up");
		handler.RequestedUris.Should().BeEmpty(
			because: "a blank family must not cost a network round trip");
	}

	[Test]
	[Description("The family is URL-encoded into the metadata path so multi-word families resolve.")]
	public async Task LookupAsync_ShouldUrlEncodeTheFamily_ForMultiWordNames() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(_ => JsonResponse(HttpStatusCode.OK), out StubHandler handler);

		// Act
		await catalog.LookupAsync("  Playfair Display  ", CancellationToken.None);

		// Assert
		handler.RequestedUris.Should().ContainSingle()
			.Which.Should().Be("https://fonts.google.com/metadata/fonts/Playfair%20Display",
				because: "the family is trimmed and percent-encoded, otherwise a multi-word family would 404 and be misreported as missing");
	}

	[Test]
	[Description("A definitive verdict is memoized, so repeated lookups of the same family cost one round trip.")]
	[TestCase(HttpStatusCode.OK, GoogleFontAvailability.InCatalog, TestName = "LookupAsync_ShouldCache_InCatalog")]
	[TestCase(HttpStatusCode.NotFound, GoogleFontAvailability.NotInCatalog, TestName = "LookupAsync_ShouldCache_NotInCatalog")]
	public async Task LookupAsync_ShouldServeSecondLookupFromCache_ForDefinitiveVerdict(
		HttpStatusCode status, GoogleFontAvailability expected) {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(
			_ => status == HttpStatusCode.OK ? JsonResponse(status) : new HttpResponseMessage(status),
			out StubHandler handler);

		// Act
		GoogleFontAvailability first = await catalog.LookupAsync("Roboto", CancellationToken.None);
		GoogleFontAvailability second = await catalog.LookupAsync("Roboto", CancellationToken.None);

		// Assert
		first.Should().Be(expected,
			because: "the first lookup reports the verdict the endpoint's status maps to");
		second.Should().Be(expected,
			because: "the cached verdict must match the probed one, or the memo would answer with something the catalogue never gave");
		handler.RequestedUris.Should().HaveCount(1,
			because: "a definitive answer within the TTL must not be re-probed");
	}

	[Test]
	[Description("An unverified verdict is held only for a short transient window: a blocked network costs one probe budget per window instead of one per build, and recovery is picked up as soon as the window passes.")]
	public async Task LookupAsync_ShouldServeFromCacheWithinTheTransientWindow_ThenReprobe_AfterUnverifiedVerdict() {
		// Arrange
		FakeTimeProvider clock = new();
		int calls = 0;
		GoogleFontsCatalog catalog = CatalogReturning(
			_ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : JsonResponse(HttpStatusCode.OK),
			out StubHandler handler, clock);

		// Act
		GoogleFontAvailability first = await catalog.LookupAsync("Roboto", CancellationToken.None);
		GoogleFontAvailability withinWindow = await catalog.LookupAsync("Roboto", CancellationToken.None);
		clock.Advance(TimeSpan.FromSeconds(31));
		GoogleFontAvailability afterWindow = await catalog.LookupAsync("Roboto", CancellationToken.None);

		// Assert
		first.Should().Be(GoogleFontAvailability.Unverified);
		withinWindow.Should().Be(GoogleFontAvailability.Unverified,
			because: "a second build seconds later must not pay the probe budget again");
		handler.RequestedUris.Should().HaveCount(2,
			because: "one probe for the outage and one after the transient window — not one per lookup");
		afterWindow.Should().Be(GoogleFontAvailability.InCatalog,
			because: "the short window is what keeps a recovered network from staying misclassified");
	}

	[Test]
	[Description("The capacity bound holds under concurrent stores too, within the tolerance a check-then-act over a concurrent dictionary can give.")]
	public void Store_ShouldStayNearCapacity_UnderConcurrentStores() {
		// Arrange
		GoogleFontsAvailabilityCache cache = new(new FakeTimeProvider());

		// Act
		System.Threading.Tasks.Parallel.For(0, 2000,
			index => cache.Store($"Family {index}", GoogleFontAvailability.InCatalog));

		// Assert
		cache.EntryCount.Should().BeLessThanOrEqualTo(512 + System.Environment.ProcessorCount,
			because: "the capacity check is a check-then-act over a concurrent dictionary, so concurrent writers may each pass it once — the bound stays tight, not exact");
	}

	[Test]
	[Description("A definitive verdict outlives the transient window, so the short window applies only to unverified outcomes.")]
	public async Task LookupAsync_ShouldKeepDefinitiveVerdict_BeyondTheTransientWindow() {
		// Arrange
		FakeTimeProvider clock = new();
		GoogleFontsCatalog catalog = CatalogReturning(_ => JsonResponse(HttpStatusCode.OK), out StubHandler handler, clock);

		// Act
		GoogleFontAvailability first = await catalog.LookupAsync("Roboto", CancellationToken.None);
		clock.Advance(TimeSpan.FromSeconds(31));
		GoogleFontAvailability afterTransientWindow = await catalog.LookupAsync("Roboto", CancellationToken.None);

		// Assert
		first.Should().Be(GoogleFontAvailability.InCatalog);
		afterTransientWindow.Should().Be(GoogleFontAvailability.InCatalog,
			because: "a published family stays published — the short window exists for unverifiable outcomes only");
		handler.RequestedUris.Should().HaveCount(1,
			because: "a definitive verdict keeps its full TTL, so the transient window must not shorten it");
	}

	[Test]
	[Description("A cached verdict expires after the TTL and the family is probed again.")]
	public async Task LookupAsync_ShouldReprobe_AfterCacheTtlElapses() {
		// Arrange
		FakeTimeProvider clock = new();
		GoogleFontsCatalog catalog = CatalogReturning(_ => JsonResponse(HttpStatusCode.OK), out StubHandler handler, clock);
		await catalog.LookupAsync("Roboto", CancellationToken.None);

		// Act
		clock.Advance(TimeSpan.FromMinutes(6));
		await catalog.LookupAsync("Roboto", CancellationToken.None);

		// Assert
		handler.RequestedUris.Should().HaveCount(2,
			because: "the catalogue changes over time, so a verdict is only trusted for the TTL");
	}

	[Test]
	[Description("The expired entry is replaced by a freshly probed verdict on the next lookup, so the singleton neither serves a stale answer nor accumulates dead entries.")]
	public async Task LookupAsync_ShouldReprobeAndReplaceExpiredEntry_OnNextLookup() {
		// Arrange
		FakeTimeProvider clock = new();
		GoogleFontsAvailabilityCache cache = new(clock);
		StubHandler handler = new(_ => JsonResponse(HttpStatusCode.OK));
		GoogleFontsCatalog catalog = new(new HttpClient(handler), cache);
		await catalog.LookupAsync("Roboto", CancellationToken.None);

		// Act
		clock.Advance(TimeSpan.FromMinutes(6));
		GoogleFontAvailability afterExpiry = await catalog.LookupAsync("Roboto", CancellationToken.None);

		// Assert
		afterExpiry.Should().Be(GoogleFontAvailability.InCatalog,
			because: "the expired verdict is re-probed rather than served stale");
		handler.RequestedUris.Should().HaveCount(2,
			because: "the second lookup must reach the endpoint again once the TTL has elapsed");
		cache.EntryCount.Should().Be(1,
			because: "the expired entry is replaced by the fresh verdict instead of accumulating alongside it");
	}

	[Test]
	[Description("A family that breaks the name contract is reported unverified without a request and without a cache entry, so an unbounded caller string never reaches the endpoint or the process-wide memo.")]
	[TestCase("Evil'; }", TestName = "LookupAsync_ShouldNotProbe_ForFamilyBreakingTheGrammar")]
	[TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA1", TestName = "LookupAsync_ShouldNotProbe_ForOverLengthFamily")]
	public async Task LookupAsync_ShouldReportUnverifiedWithoutRequest_ForInvalidFamily(string family) {
		// Arrange
		GoogleFontsAvailabilityCache cache = new(new FakeTimeProvider());
		StubHandler handler = new(_ => JsonResponse(HttpStatusCode.OK));
		GoogleFontsCatalog catalog = new(new HttpClient(handler), cache);

		// Act
		GoogleFontAvailability availability = await catalog.LookupAsync(family, CancellationToken.None);

		// Assert
		availability.Should().Be(GoogleFontAvailability.Unverified,
			because: "the probe is advisory, so an invalid family is unknown rather than a throw — the builder still rejects it");
		handler.RequestedUris.Should().BeEmpty(
			because: "the grammar and length bound are enforced before anything reaches the outbound URL");
		cache.EntryCount.Should().Be(0,
			because: "an unbounded caller-supplied string must not be pinned in the process-lifetime memo");
	}

	[Test]
	[Description("The cache stops growing at its capacity instead of accumulating one entry per distinct family spelling for the life of the process.")]
	public void Store_ShouldStopGrowing_AtCapacity() {
		// Arrange
		GoogleFontsAvailabilityCache cache = new(new FakeTimeProvider());

		// Act
		for (int i = 0; i < 700; i++) {
			cache.Store($"Family {i}", GoogleFontAvailability.InCatalog);
		}

		// Assert
		cache.EntryCount.Should().Be(512,
			because: "the bound has to hold exactly — a sweep that over-removes would silently drop live verdicts while still satisfying a less-than assertion");
	}

	[Test]
	[Description("Reading an expired entry evicts it there and then, rather than leaving it to be shadowed by a later store — the offline case, where the follow-up probe is Unverified and stores nothing, depends on it.")]
	public void TryGet_ShouldEvictExpiredEntry_OnTheReadThatObservesIt() {
		// Arrange
		FakeTimeProvider clock = new();
		GoogleFontsAvailabilityCache cache = new(clock);
		cache.Store("Roboto", GoogleFontAvailability.InCatalog);

		// Act
		clock.Advance(TimeSpan.FromMinutes(6));
		bool hit = cache.TryGet("Roboto", out _);

		// Assert
		hit.Should().BeFalse(because: "the entry outlived its TTL, so it must not answer");
		cache.EntryCount.Should().Be(0,
			because: "an expired entry is evicted on the read that observes it — nothing re-stores it when the follow-up probe comes back Unverified");
	}

	[Test]
	[Description("A full cache reclaims room by sweeping entries whose TTL has passed, so capacity is a bound rather than a permanent freeze.")]
	public void Store_ShouldSweepExpiredEntries_WhenAtCapacity() {
		// Arrange
		FakeTimeProvider clock = new();
		GoogleFontsAvailabilityCache cache = new(clock);
		for (int i = 0; i < 512; i++) {
			cache.Store($"Family {i}", GoogleFontAvailability.InCatalog);
		}

		// Act
		clock.Advance(TimeSpan.FromMinutes(6));
		cache.Store("Fresh", GoogleFontAvailability.InCatalog);

		// Assert
		cache.TryGet("Fresh", out GoogleFontAvailability stored).Should().BeTrue(
			because: "the sweep reclaimed room, so the new verdict is memoized instead of skipped");
		stored.Should().Be(GoogleFontAvailability.InCatalog,
			because: "the reclaimed slot holds the verdict that was just stored");
		cache.EntryCount.Should().Be(1,
			because: "every entry the sweep found expired is gone, leaving only the freshly stored one");
	}

	[Test]
	[Description("A family the full cache already holds can still be refreshed, so a long-lived server cannot freeze a stale verdict once capacity is reached.")]
	public void Store_ShouldRefreshExistingKey_WhenAtCapacity() {
		// Arrange
		GoogleFontsAvailabilityCache cache = new(new FakeTimeProvider());
		for (int i = 0; i < 512; i++) {
			cache.Store($"Family {i}", GoogleFontAvailability.InCatalog);
		}

		// Act
		cache.Store("Family 0", GoogleFontAvailability.NotInCatalog);

		// Assert
		cache.TryGet("Family 0", out GoogleFontAvailability refreshed).Should().BeTrue(
			because: "an already-held key stays retrievable after a refresh at capacity");
		refreshed.Should().Be(GoogleFontAvailability.NotInCatalog,
			because: "the capacity guard applies to NEW keys only — refusing to update a key already held would pin an outdated verdict for the life of the process");
	}

	[Test]
	[Description("The lookup canonicalizes internal whitespace itself, so the probe spelling and the cache key never depend on the caller normalizing first.")]
	public async Task LookupAsync_ShouldCollapseWhitespace_BeforeProbingAndCaching() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(_ => JsonResponse(HttpStatusCode.OK), out StubHandler handler);

		// Act
		await catalog.LookupAsync("Open  Sans", CancellationToken.None);
		await catalog.LookupAsync("Open Sans", CancellationToken.None);

		// Assert
		handler.RequestedUris.Should().ContainSingle()
			.Which.Should().Be("https://fonts.google.com/metadata/fonts/Open%20Sans",
				because: "the exact-match endpoint 404s an un-collapsed spelling, and both spellings must share one cache entry");
	}

	[Test]
	[Description("Two catalog instances sharing the singleton cache serve the second lookup without a probe — the catalog is a transient typed client, so a per-instance memo would never hit in production.")]
	public async Task LookupAsync_ShouldShareVerdictsAcrossCatalogInstances_ThroughTheSingletonCache() {
		// Arrange
		GoogleFontsAvailabilityCache sharedCache = new(new FakeTimeProvider());
		StubHandler firstHandler = new(_ => JsonResponse(HttpStatusCode.OK));
		StubHandler secondHandler = new(_ => JsonResponse(HttpStatusCode.OK));
		GoogleFontsCatalog firstCatalog = new(new HttpClient(firstHandler), sharedCache);
		GoogleFontsCatalog secondCatalog = new(new HttpClient(secondHandler), sharedCache);

		// Act
		GoogleFontAvailability first = await firstCatalog.LookupAsync("Roboto", CancellationToken.None);
		GoogleFontAvailability second = await secondCatalog.LookupAsync("Roboto", CancellationToken.None);

		// Assert
		first.Should().Be(GoogleFontAvailability.InCatalog,
			because: "the first catalog instance probes and gets the published verdict");
		second.Should().Be(GoogleFontAvailability.InCatalog,
			because: "the second instance must report the verdict the shared cache already holds");
		firstHandler.RequestedUris.Should().HaveCount(1,
			because: "the family is probed exactly once across both catalog instances");
		secondHandler.RequestedUris.Should().BeEmpty(
			because: "each build-theme call resolves a fresh transient catalog, so the memo must live in the shared singleton to save the round trip");
	}

	[Test]
	[Description("Cache keys are case-sensitive, matching the endpoint: Roboto and roboto are different lookups.")]
	public async Task LookupAsync_ShouldProbeSeparately_ForCaseVariantFamilies() {
		// Arrange
		GoogleFontsCatalog catalog = CatalogReturning(
			request => request.RequestUri!.AbsoluteUri.EndsWith("/Roboto", StringComparison.Ordinal)
				? JsonResponse(HttpStatusCode.OK)
				: new HttpResponseMessage(HttpStatusCode.NotFound),
			out StubHandler handler);

		// Act
		GoogleFontAvailability exact = await catalog.LookupAsync("Roboto", CancellationToken.None);
		GoogleFontAvailability lowercase = await catalog.LookupAsync("roboto", CancellationToken.None);

		// Assert
		exact.Should().Be(GoogleFontAvailability.InCatalog,
			because: "the exactly spelled family is the one the endpoint publishes");
		lowercase.Should().Be(GoogleFontAvailability.NotInCatalog,
			because: "the endpoint is case-sensitive, so a case-folded cache hit would fabricate an answer the catalogue never gave");
		handler.RequestedUris.Should().HaveCount(2,
			because: "case variants are distinct cache keys, so each spelling must reach the endpoint on its own");
	}
}

/// <summary>
/// Canary against the live Google Fonts metadata endpoint — the contract
/// <see cref="GoogleFontsCatalog"/> encodes is undocumented, so this is the early-warning signal
/// if Google changes it. Run explicitly; it needs outbound network access.
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Probes the live fonts.google.com metadata endpoint; run manually to re-verify the undocumented contract.")]
public sealed class GoogleFontsCatalogEndpointCanaryTests {

	[Test]
	[Description("The live endpoint still answers JSON-200 for a published family, 404 for an unpublished one, and stays case-sensitive.")]
	public async Task LiveMetadataEndpoint_ShouldStillHonourTheEncodedContract() {
		// Arrange
		using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
		httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("clio");
		GoogleFontsCatalog catalog = new(httpClient, new GoogleFontsAvailabilityCache(TimeProvider.System));

		// Act
		GoogleFontAvailability published = await catalog.LookupAsync("Roboto", CancellationToken.None);
		GoogleFontAvailability multiWord = await catalog.LookupAsync("Playfair Display", CancellationToken.None);
		GoogleFontAvailability unpublished = await catalog.LookupAsync("Verdana", CancellationToken.None);
		GoogleFontAvailability wrongCase = await catalog.LookupAsync("roboto", CancellationToken.None);

		// Assert
		published.Should().Be(GoogleFontAvailability.InCatalog,
			because: "the live endpoint must still answer JSON-200 for a family Google publishes");
		multiWord.Should().Be(GoogleFontAvailability.InCatalog,
			because: "percent-encoded spaces must keep resolving multi-word families");
		unpublished.Should().Be(GoogleFontAvailability.NotInCatalog,
			because: "a web-safe family Google does not host must still 404 — css2 would serve a look-alike instead");
		wrongCase.Should().Be(GoogleFontAvailability.NotInCatalog,
			because: "the endpoint is case-sensitive with no server-side correction; if this starts passing, the case guidance can be relaxed");
	}
}
