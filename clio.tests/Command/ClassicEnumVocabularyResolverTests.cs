using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="ClassicEnumVocabularyResolver"/> — the unauthenticated fetch of a TARGET stand's own
/// login page (for its content-hash) and <c>sysenums.js</c>, feeding <see cref="IClassicEnumVocabularySourceParser"/>
/// so <c>enumVocabulary</c> reflects the stand's actual platform build (ENG-95412).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ClassicEnumVocabularyResolverTests {

	private const string BaseUri = "http://stand.example.local";
	private const string Hash = "ac178e58d3511cf431f45b6b63ecb001";

	// The three login-page locations the resolver knows: .NET Core's site-root Login.html, .NET Framework's
	// /0-rooted NuiLogin.aspx, and the site-root NuiLogin.aspx real stands also answer. IsNetCore picks the ORDER,
	// not the only attempt — so these are asserted as exact URLs in exact sequence. That precision matters because
	// StubHandler answers any UNMAPPED url with 404, exactly like an unreachable stand: assertions on the outcome
	// alone would pass just as happily against completely wrong URLs.
	private const string CoreRootLoginUrl = BaseUri + "/Login/Login.html";
	private const string SiteRootNuiLoginUrl = BaseUri + "/Login/NuiLogin.aspx";
	private const string ZeroRootNuiLoginUrl = BaseUri + "/0/Login/NuiLogin.aspx";
	private const string SiteRootSysEnumsUrl = BaseUri + "/core/" + Hash + "/Terrasoft/core/enums/sysenums.js";
	private const string ZeroRootSysEnumsUrl = BaseUri + "/0/core/" + Hash + "/Terrasoft/core/enums/sysenums.js";

	private IClassicEnumVocabularySourceParser _parser;

	[SetUp]
	public void Setup() => _parser = Substitute.For<IClassicEnumVocabularySourceParser>();

	private static EnvironmentSettings Settings(string uri = BaseUri, bool isNetCore = true) =>
		new() { Uri = uri, IsNetCore = isNetCore };

	private static IHttpClientFactory FactoryReturning(
		IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> byUrl, List<string> requested = null) {
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>())
			.Returns(_ => new HttpClient(new StubHandler(byUrl, requested), disposeHandler: true));
		return factory;
	}

	private static IHttpClientFactory ThrowingFactory(Exception exception) {
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new ThrowingHandler(exception), disposeHandler: true));
		return factory;
	}

	// A login page whose script src names the content hash under <paramref name="rootPrefix"/> ("" or "/0").
	private static string LoginHtmlNaming(string rootPrefix) =>
		$"<html>...<script src=\"{rootPrefix}/core/{Hash}/Terrasoft/init.js\">...";

	private static ClassicEnumVocabularyParseResult SingleEnum(string enumName, string memberName, long value) =>
		new(new Dictionary<string, IReadOnlyDictionary<string, long>> {
				[enumName] = new Dictionary<string, long> { [memberName] = value }
			},
			Array.Empty<string>());

	[Test]
	[Description("On a .NET Core stand Resolve tries /Login/Login.html FIRST and, when it answers, reads sysenums.js off the bare site root, handing the body to the parser verbatim.")]
	public void Resolve_ShouldUseNetCoreUrlsFirst_WhenEnvironmentIsNetCore() {
		// Arrange
		const string sysEnumsBody = "Terrasoft.ViewItemType = { GRID_LAYOUT: 0 };";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[CoreRootLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming(string.Empty)),
			[SiteRootSysEnumsUrl] = (HttpStatusCode.OK, sysEnumsBody)
		};
		ClassicEnumVocabularyParseResult expected = SingleEnum("ViewItemType", "GRID_LAYOUT", 0);
		_parser.Parse(sysEnumsBody).Returns(expected);
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(
			Settings(isNetCore: true), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([CoreRootLoginUrl, SiteRootSysEnumsUrl],
			because: "the runtime's own shape is tried first, so a healthy Core stand costs exactly one login-page request");
		result.Enums.Should().BeSameAs(expected.Enums, because: "the resolver hands the parser's own result straight through when the fetch succeeds");
		_parser.Received(1).Parse(sysEnumsBody);
	}

	[Test]
	[Description("On a .NET Framework stand Resolve tries /0/Login/NuiLogin.aspx FIRST and reads the /0-rooted sysenums.js the login page itself names.")]
	public void Resolve_ShouldUseNetFrameworkUrlsFirst_WhenEnvironmentIsNetFramework() {
		// Arrange
		const string sysEnumsBody = "Terrasoft.ContentType = { TEXT: 1 };";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[ZeroRootNuiLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming("/0")),
			[ZeroRootSysEnumsUrl] = (HttpStatusCode.OK, sysEnumsBody)
		};
		ClassicEnumVocabularyParseResult expected = SingleEnum("ContentType", "TEXT", 1);
		_parser.Parse(sysEnumsBody).Returns(expected);
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(
			Settings(isNetCore: false), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([ZeroRootNuiLoginUrl, ZeroRootSysEnumsUrl],
			because: ".NET Framework serves the login page and the hashed static tree behind the /0 application root");
		result.Enums.Should().BeSameAs(expected.Enums, because: "a Framework stand must resolve its vocabulary exactly like a Core one");
		_parser.Received(1).Parse(sysEnumsBody);
	}

	[Test]
	[Description("A stand that answers only the site-root NuiLogin.aspx still resolves: the runtime's preferred URL is tried first, then the alternates, and the static root follows the login page that actually answered.")]
	public void Resolve_ShouldFallBackToTheNextCandidate_WhenTheRuntimePreferredLoginPageIsAbsent() {
		// Arrange — a real shape: /Login/Login.html is not served, /Login/NuiLogin.aspx is, off the site root.
		const string sysEnumsBody = "Terrasoft.DataValueType = { GUID: 0 };";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[SiteRootNuiLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming(string.Empty)),
			[SiteRootSysEnumsUrl] = (HttpStatusCode.OK, sysEnumsBody)
		};
		_parser.Parse(sysEnumsBody).Returns(SingleEnum("DataValueType", "GUID", 0));
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(
			Settings(isNetCore: true), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([CoreRootLoginUrl, SiteRootNuiLoginUrl, SiteRootSysEnumsUrl],
			because: "committing to a single URL per runtime would omit enumVocabulary here instead of falling through to the shape the stand does serve");
		result.Enums.Should().ContainKey("DataValueType",
			because: "the vocabulary must resolve on every stand that serves a login page at any known location");
	}

	[Test]
	[Description("A .NET Framework login page that names '/core/<hash>/' without the /0 prefix resolves against the root implied by the login page that answered, never a double-prefixed one.")]
	public void Resolve_ShouldUseTheRootOfTheAnsweringLoginPage_WhenTheMarkerOmitsItsPrefix() {
		// Arrange
		const string sysEnumsBody = "Terrasoft.DataValueType = { GUID: 0 };";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[ZeroRootNuiLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming(string.Empty)),
			[ZeroRootSysEnumsUrl] = (HttpStatusCode.OK, sysEnumsBody)
		};
		_parser.Parse(sysEnumsBody).Returns(SingleEnum("DataValueType", "GUID", 0));
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(
			Settings(isNetCore: false), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([ZeroRootNuiLoginUrl, ZeroRootSysEnumsUrl],
			because: "a marker without its own root prefix is completed from the login page that served it, never double-prefixed");
		result.Enums.Should().ContainKey("DataValueType");
	}

	[Test]
	[Description("Resolve omits enumVocabulary and warns, without throwing, when the environment URI is not configured.")]
	public void Resolve_ShouldReturnEmptyWithWarning_WhenUriIsMissing() {
		// Arrange
		var resolver = new ClassicEnumVocabularyResolver(Settings(uri: null), Substitute.For<IHttpClientFactory>(), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		result.Enums.Should().BeEmpty(because: "there is no target to fetch from");
		result.Warnings.Should().ContainSingle(because: "the missing URI is a single, explainable degradation, not a crash");
		_parser.DidNotReceiveWithAnyArgs().Parse(default);
	}

	[Test]
	[Description("Resolve tries every known login-page location and degrades to ONE aggregated warning, never a thrown exception, when none of them answers.")]
	public void Resolve_ShouldReturnEmptyWithOneAggregatedWarning_WhenNoLoginPageAnswers() {
		// Arrange — nothing is mapped, so every candidate 404s.
		var byUrl = new Dictionary<string, (HttpStatusCode, string)>();
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([CoreRootLoginUrl, SiteRootNuiLoginUrl, ZeroRootNuiLoginUrl],
			because: "every known location must be tried before giving up, in the order the declared runtime implies");
		result.Enums.Should().BeEmpty(because: "there is nothing to parse without the login page's content-hash marker");
		result.Warnings.Should().ContainSingle(w => w.Contains("login page") && w.Contains("404"),
			because: "one warning naming every location tried is readable; one warning per attempt is noise");
		_parser.DidNotReceiveWithAnyArgs().Parse(default);
	}

	[Test]
	[Description("A login page that answers but carries no /core/<hash>/ marker is not the end of the road — the remaining candidates are tried before degrading.")]
	public void Resolve_ShouldKeepTryingCandidates_WhenALoginPageCarriesNoContentHashMarker() {
		// Arrange
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[CoreRootLoginUrl] = (HttpStatusCode.OK, "<html>no core path here</html>")
		};
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([CoreRootLoginUrl, SiteRootNuiLoginUrl, ZeroRootNuiLoginUrl],
			because: "a 200 without the marker is as useless as a 404, so the walk continues instead of stopping there");
		result.Enums.Should().BeEmpty(because: "without the hash there is no sysenums.js URL to build");
		result.Warnings.Should().ContainSingle(w => w.Contains("content-hash"),
			because: "the caller must know WHY nothing was resolved, not just that nothing was");
		_parser.DidNotReceiveWithAnyArgs().Parse(default);
	}

	[Test]
	[Description("Resolve degrades to an empty result with a warning when sysenums.js itself cannot be fetched (e.g. truncated connection, 404 at the hashed path).")]
	public void Resolve_ShouldReturnEmptyWithWarning_WhenSysEnumsFetchFails() {
		// Arrange
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[CoreRootLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming(string.Empty)),
			[SiteRootSysEnumsUrl] = (HttpStatusCode.NotFound, string.Empty)
		};
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([CoreRootLoginUrl, SiteRootSysEnumsUrl],
			because: "the hash resolved, so the second fetch must be the exact hashed URL, not a guess");
		result.Enums.Should().BeEmpty(because: "the hash resolved but the file behind it did not, so there is still nothing to parse");
		result.Warnings.Should().ContainSingle(w => w.Contains("sysenums.js"),
			because: "the warning must name the file that failed, not just 'something went wrong'");
		_parser.DidNotReceiveWithAnyArgs().Parse(default);
	}

	[Test]
	[Description("Resolve degrades to an empty result with a warning, never a thrown exception, on a transport-level failure (host unreachable).")]
	public void Resolve_ShouldReturnEmptyWithWarning_WhenTransportThrows() {
		// Arrange
		var resolver = new ClassicEnumVocabularyResolver(Settings(), ThrowingFactory(new HttpRequestException("name or service not known")), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		result.Enums.Should().BeEmpty(because: "an unreachable host leaves nothing resolved");
		result.Warnings.Should().ContainSingle(because: "a transport exception must degrade to a warning, never propagate as a thrown exception");
	}

	// Records every requested absolute URL so a test can assert the resolver asked for the runtime-correct paths, in
	// order: an unmapped URL degrades to 404 exactly like an unreachable one, so outcome assertions alone cannot tell
	// a wrong URL from a legitimately failing stand.
	private sealed class StubHandler(
		IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> byUrl,
		List<string> requested) : HttpMessageHandler {

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			requested?.Add(request.RequestUri!.ToString());
			(HttpStatusCode status, string body) = byUrl.TryGetValue(request.RequestUri!.ToString(),
				out (HttpStatusCode Status, string Body) mapped)
				? mapped
				: (HttpStatusCode.NotFound, string.Empty);
			return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request, Content = new StringContent(body) });
		}
	}

	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromException<HttpResponseMessage>(exception);
	}
}
