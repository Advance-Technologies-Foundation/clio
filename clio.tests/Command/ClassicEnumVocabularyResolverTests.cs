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
	private const string LoginUrl = BaseUri + "/Login/NuiLogin.aspx";
	private const string SysEnumsUrl = BaseUri + "/core/ac178e58d3511cf431f45b6b63ecb001/Terrasoft/core/enums/sysenums.js";

	private IClassicEnumVocabularySourceParser _parser;

	[SetUp]
	public void Setup() => _parser = Substitute.For<IClassicEnumVocabularySourceParser>();

	private static EnvironmentSettings Settings(string uri = BaseUri) => new() { Uri = uri };

	private static IHttpClientFactory FactoryReturning(IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> byUrl) {
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new StubHandler(byUrl), disposeHandler: true));
		return factory;
	}

	private static IHttpClientFactory ThrowingFactory(Exception exception) {
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new ThrowingHandler(exception), disposeHandler: true));
		return factory;
	}

	[Test]
	[Description("Resolve fetches the login page then sysenums.js at the hash it names, and hands the body to the parser verbatim.")]
	public void Resolve_ShouldFetchLoginPageThenSysEnums_AndDelegateToParser() {
		// Arrange
		const string loginHtml = "<html>...<script src=\"/core/ac178e58d3511cf431f45b6b63ecb001/Terrasoft/init.js\">...";
		const string sysEnumsBody = "Terrasoft.ViewItemType = { GRID_LAYOUT: 0 };";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[LoginUrl] = (HttpStatusCode.OK, loginHtml),
			[SysEnumsUrl] = (HttpStatusCode.OK, sysEnumsBody)
		};
		var expected = new ClassicEnumVocabularyParseResult(
			new Dictionary<string, IReadOnlyDictionary<string, long>> {
				["ViewItemType"] = new Dictionary<string, long> { ["GRID_LAYOUT"] = 0 }
			},
			Array.Empty<string>());
		_parser.Parse(sysEnumsBody).Returns(expected);
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
		result.Enums.Should().BeSameAs(expected.Enums, because: "the resolver hands the parser's own result straight through when the fetch succeeds");
		_parser.Received(1).Parse(sysEnumsBody);
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
			[LoginUrl] = (HttpStatusCode.NotFound, string.Empty)
		};
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
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
			[LoginUrl] = (HttpStatusCode.OK, "<html>no core path here</html>")
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
		const string loginHtml = "<html>...<script src=\"/core/ac178e58d3511cf431f45b6b63ecb001/Terrasoft/init.js\">...";
		var byUrl = new Dictionary<string, (HttpStatusCode, string)> {
			[LoginUrl] = (HttpStatusCode.OK, loginHtml),
			[SysEnumsUrl] = (HttpStatusCode.NotFound, string.Empty)
		};
		var resolver = new ClassicEnumVocabularyResolver(Settings(), FactoryReturning(byUrl), _parser);

		// Act
		ClassicEnumVocabularyParseResult result = resolver.Resolve();

		// Assert
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

	private sealed class StubHandler(IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> byUrl) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
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
