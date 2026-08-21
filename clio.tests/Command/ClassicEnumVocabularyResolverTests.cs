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

	// .NET Core serves the login page off the site root; .NET Framework serves it behind the /0 application root.
	// Asserted as exact URLs (not merely as "something 404'd") because an unmapped URL in StubHandler also 404s —
	// a wrong URL would otherwise be indistinguishable from a legitimately unreachable stand.
	private const string NetCoreLoginUrl = BaseUri + "/Login/Login.html";
	private const string NetFrameworkLoginUrl = BaseUri + "/0/Login/NuiLogin.aspx";
	private const string NetCoreSysEnumsUrl = BaseUri + "/core/" + Hash + "/Terrasoft/core/enums/sysenums.js";
	private const string NetFrameworkSysEnumsUrl = BaseUri + "/0/core/" + Hash + "/Terrasoft/core/enums/sysenums.js";

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

	private static string LoginHtmlNaming(string corePath) =>
		$"<html>...<script src=\"{corePath}/core/{Hash}/Terrasoft/init.js\">...";

	[Test]
	[Description("On a .NET Core stand Resolve fetches /Login/Login.html and sysenums.js off the bare site root, and hands the body to the parser verbatim.")]
	public void Resolve_ShouldUseNetCoreUrls_WhenEnvironmentIsNetCore() {
		// Arrange
		const string sysEnumsBody = "Terrasoft.ViewItemType = { GRID_LAYOUT: 0 };";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[NetCoreLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming(string.Empty)),
			[NetCoreSysEnumsUrl] = (HttpStatusCode.OK, sysEnumsBody)
		};
		var expected = new ClassicEnumVocabularyParseResult(
			new Dictionary<string, IReadOnlyDictionary<string, long>> {
				["ViewItemType"] = new Dictionary<string, long> { ["GRID_LAYOUT"] = 0 }
			},
			Array.Empty<string>());
		_parser.Parse(sysEnumsBody).Returns(expected);
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(
			Settings(isNetCore: true), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([NetCoreLoginUrl, NetCoreSysEnumsUrl],
			because: ".NET Core serves both the login page and the hashed static tree off the site root, with no /0 prefix");
		result.Enums.Should().BeSameAs(expected.Enums, because: "the resolver hands the parser's own result straight through when the fetch succeeds");
		_parser.Received(1).Parse(sysEnumsBody);
	}

	[Test]
	[Description("On a .NET Framework stand Resolve fetches /0/Login/NuiLogin.aspx and the /0-rooted sysenums.js the login page itself names.")]
	public void Resolve_ShouldUseNetFrameworkUrls_WhenEnvironmentIsNetFramework() {
		// Arrange
		const string sysEnumsBody = "Terrasoft.ContentType = { TEXT: 1 };";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[NetFrameworkLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming("/0")),
			[NetFrameworkSysEnumsUrl] = (HttpStatusCode.OK, sysEnumsBody)
		};
		var expected = new ClassicEnumVocabularyParseResult(
			new Dictionary<string, IReadOnlyDictionary<string, long>> {
				["ContentType"] = new Dictionary<string, long> { ["TEXT"] = 1 }
			},
			Array.Empty<string>());
		_parser.Parse(sysEnumsBody).Returns(expected);
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(
			Settings(isNetCore: false), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([NetFrameworkLoginUrl, NetFrameworkSysEnumsUrl],
			because: ".NET Framework serves the login page and the hashed static tree behind the /0 application root");
		result.Enums.Should().BeSameAs(expected.Enums, because: "a Framework stand must resolve its vocabulary exactly like a Core one");
		_parser.Received(1).Parse(sysEnumsBody);
	}

	[Test]
	[Description("A .NET Framework login page that names '/core/<hash>/' without the /0 prefix still resolves, falling back to the IsNetCore-derived static root.")]
	public void Resolve_ShouldFallBackToRuntimeStaticRoot_WhenLoginPageOmitsTheRootPrefix() {
		// Arrange
		const string sysEnumsBody = "Terrasoft.DataValueType = { GUID: 0 };";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[NetFrameworkLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming(string.Empty)),
			[NetFrameworkSysEnumsUrl] = (HttpStatusCode.OK, sysEnumsBody)
		};
		_parser.Parse(sysEnumsBody).Returns(new ClassicEnumVocabularyParseResult(
			new Dictionary<string, IReadOnlyDictionary<string, long>> {
				["DataValueType"] = new Dictionary<string, long> { ["GUID"] = 0 }
			},
			Array.Empty<string>()));
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(
			Settings(isNetCore: false), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([NetFrameworkLoginUrl, NetFrameworkSysEnumsUrl],
			because: "a marker without its own root prefix is completed from the runtime split, never double-prefixed");
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
	[Description("Resolve degrades to an empty result with a warning, never a thrown exception, when the login page cannot be fetched.")]
	public void Resolve_ShouldReturnEmptyWithWarning_WhenLoginPageFetchFails() {
		// Arrange
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[NetCoreLoginUrl] = (HttpStatusCode.NotFound, string.Empty)
		};
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([NetCoreLoginUrl],
			because: "the resolver stops at the login page it actually asked for, and asks for the runtime-correct one");
		result.Enums.Should().BeEmpty(because: "there is nothing to parse without the login page's content-hash marker");
		result.Warnings.Should().ContainSingle(w => w.Contains("login page"),
			because: "a stand that cannot serve the login page is a named, explainable degradation");
		_parser.DidNotReceiveWithAnyArgs().Parse(default);
	}

	[Test]
	[Description("Resolve degrades to an empty result with a warning when the login page carries no /core/<hash>/ marker.")]
	public void Resolve_ShouldReturnEmptyWithWarning_WhenContentHashMarkerIsAbsent() {
		// Arrange
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[NetCoreLoginUrl] = (HttpStatusCode.OK, "<html>no core path here</html>")
		};
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
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
			[NetCoreLoginUrl] = (HttpStatusCode.OK, LoginHtmlNaming(string.Empty)),
			[NetCoreSysEnumsUrl] = (HttpStatusCode.NotFound, string.Empty)
		};
		var requested = new List<string>();
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl, requested), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		requested.Should().Equal([NetCoreLoginUrl, NetCoreSysEnumsUrl],
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

	// Records every requested absolute URL so a test can assert the resolver asked for the runtime-correct path:
	// an unmapped URL degrades to 404 exactly like an unreachable one, so outcome assertions alone cannot tell a
	// wrong URL from a legitimately failing stand.
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
