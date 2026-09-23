using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Unit tests for <see cref="OAuthAuthorizationCodeService.ResolveAsync"/>, driven through an
/// in-process stub <see cref="HttpMessageHandler"/> and a substituted <see cref="IOAuthTokenStore"/>
/// (no sockets, no Creatio, no files).
/// </summary>
/// <remarks>
/// The rule under test decides whether a refresh failure DELETES the cached session. Getting it wrong
/// is silent and expensive: a proxy 407 or a WAF block page is not an OAuth answer, and treating it as
/// terminal throws away a refresh token that is still valid for days - on an SSO-only environment that
/// halts every automated command until a human is at a browser.
/// </remarks>
[Category("Unit")]
[Property("Module", "Common")]
[TestFixture]
public sealed class OAuthAuthorizationCodeServiceTests {

	private const string TokenEndpoint = "https://id.example/connect/token";

	[Test]
	[Description("An OAuth-defined terminal error means the refresh token itself is dead, so the cached session is dropped and the user is told to sign in again.")]
	[TestCase(HttpStatusCode.BadRequest, "invalid_grant")]
	[TestCase(HttpStatusCode.BadRequest, "invalid_client")]
	[TestCase(HttpStatusCode.Unauthorized, "unauthorized_client")]
	public async Task ResolveAsync_ShouldDeleteTheToken_WhenTheServerReportsATerminalOAuthError(
		HttpStatusCode status, string error) {
		// Arrange
		IOAuthTokenStore store = CreateStoreWithExpiredToken();
		OAuthAuthorizationCodeService service = CreateService(store,
			StubHttpMessageHandler.Returning(status, $"{{\"error\":\"{error}\"}}"));

		// Act
		Func<Task> act = () => service.ResolveAsync(CreateEnvironment());

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>();
		store.Received(1).Delete(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("A 4xx whose body is not an OAuth error - a proxy 407, a WAF block page, an empty body, a 404 from a misrouted endpoint - says nothing about the refresh token, so the cached session must survive and the failure cost a retry rather than an interactive sign-in.")]
	[TestCase(HttpStatusCode.ProxyAuthenticationRequired, "<html>proxy authentication required</html>")]
	[TestCase(HttpStatusCode.Forbidden, "<html>blocked by policy</html>")]
	[TestCase(HttpStatusCode.NotFound, "")]
	[TestCase(HttpStatusCode.BadRequest, "not json at all")]
	public async Task ResolveAsync_ShouldKeepTheToken_WhenTheFailureIsNotAnOAuthError(
		HttpStatusCode status, string body) {
		// Arrange
		IOAuthTokenStore store = CreateStoreWithExpiredToken();
		OAuthAuthorizationCodeService service = CreateService(store,
			StubHttpMessageHandler.Returning(status, body));

		// Act
		Func<Task> act = () => service.ResolveAsync(CreateEnvironment());

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>();
		store.DidNotReceive().Delete(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("A server-side fault is transient by definition, so it must never cost the cached credential.")]
	public async Task ResolveAsync_ShouldKeepTheToken_WhenTheServerFails() {
		// Arrange
		IOAuthTokenStore store = CreateStoreWithExpiredToken();
		OAuthAuthorizationCodeService service = CreateService(store,
			StubHttpMessageHandler.Returning(HttpStatusCode.InternalServerError, "boom"));

		// Act
		Func<Task> act = () => service.ResolveAsync(CreateEnvironment());

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>();
		store.DidNotReceive().Delete(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("A refresh response that omits refresh_token leaves the previous one in force; dropping it would make the next refresh fail against a server that simply does not rotate.")]
	public async Task ResolveAsync_ShouldCarryTheOldRefreshTokenForward_WhenTheResponseOmitsIt() {
		// Arrange
		IOAuthTokenStore store = CreateStoreWithExpiredToken();
		OAuthAuthorizationCodeService service = CreateService(store, StubHttpMessageHandler.Returning(
			HttpStatusCode.OK, "{\"access_token\":\"fresh-access\",\"expires_in\":3600}"));

		// Act
		OAuthTokenSet resolved = await service.ResolveAsync(CreateEnvironment());

		// Assert
		resolved.AccessToken.Should().Be("fresh-access", because: "the refreshed access token is what callers use");
		resolved.RefreshToken.Should().Be("old-refresh",
			because: "a server that does not rotate refresh tokens expects the original one on the next refresh");
		store.Received(1).Write(Arg.Any<EnvironmentSettings>(), Arg.Any<OAuthTokenSet>());
	}

	[Test]
	[Description("The reported message names the OAuth error so the user can act on it, and never carries token material.")]
	public async Task ResolveAsync_ShouldNameTheOAuthError_WithoutEchoingTokenMaterial() {
		// Arrange
		IOAuthTokenStore store = CreateStoreWithExpiredToken();
		OAuthAuthorizationCodeService service = CreateService(store, StubHttpMessageHandler.Returning(
			HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"expired\"}"));

		// Act
		Func<Task> act = () => service.ResolveAsync(CreateEnvironment());

		// Assert
		(await act.Should().ThrowAsync<InvalidOperationException>())
			.Which.Message.Should().Contain("invalid_grant").And.NotContain("old-refresh");
	}

	[Test]
	[Description("A still-valid cached token is returned without any network call, so routine commands cost no round-trip.")]
	public async Task ResolveAsync_ShouldReturnTheCachedToken_WhenItIsStillValid() {
		// Arrange
		IOAuthTokenStore store = Substitute.For<IOAuthTokenStore>();
		store.BuildKey(Arg.Any<EnvironmentSettings>()).Returns("key");
		store.TryRead(Arg.Any<EnvironmentSettings>(), out Arg.Any<OAuthTokenSet>())
			.Returns(call => {
				call[1] = BuildToken(DateTimeOffset.UtcNow.AddHours(1));
				return true;
			});
		StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
		OAuthAuthorizationCodeService service = CreateService(store, handler);

		// Act
		OAuthTokenSet resolved = await service.ResolveAsync(CreateEnvironment());

		// Assert
		resolved.AccessToken.Should().Be("old-access", because: "a token outside the pre-expiry window needs no refresh");
		handler.RequestCount.Should().Be(0, because: "a valid cached token must not cost a token-endpoint round-trip");
	}

	[Test]
	[Description("RefreshAsync refreshes a token the server rejected even though its expiry has not been reached, because ResolveAsync would keep returning it.")]
	public async Task RefreshAsync_ShouldRefresh_WhenTheRejectedTokenIsStillInsideItsLifetime() {
		// Arrange
		IOAuthTokenStore store = CreateStoreWithToken(DateTimeOffset.UtcNow.AddHours(1));
		StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(
			HttpStatusCode.OK, "{\"access_token\":\"fresh-access\",\"expires_in\":3600}");
		OAuthAuthorizationCodeService service = CreateService(store, handler);

		// Act
		OAuthTokenSet refreshed = await service.RefreshAsync(CreateEnvironment(), "old-access");
		OAuthTokenSet resolved = await service.ResolveAsync(CreateEnvironment());

		// Assert
		refreshed.AccessToken.Should().Be("fresh-access");
		resolved.AccessToken.Should().Be("fresh-access", because: "the refreshed token replaces the cached one");
		handler.RequestCount.Should().Be(1);
		store.Received(1).Write(Arg.Any<EnvironmentSettings>(), Arg.Any<OAuthTokenSet>());
	}

	[Test]
	[Description("When a token other than the rejected one is already available (another caller refreshed first), RefreshAsync returns it without rotating the refresh token again.")]
	public async Task RefreshAsync_ShouldReuseANewerToken_WhenTheRejectedOneWasAlreadyReplaced() {
		// Arrange
		IOAuthTokenStore store = CreateStoreWithToken(DateTimeOffset.UtcNow.AddHours(1));
		StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
		OAuthAuthorizationCodeService service = CreateService(store, handler);

		// Act
		OAuthTokenSet resolved = await service.RefreshAsync(CreateEnvironment(), "an-older-access-token");

		// Assert
		resolved.AccessToken.Should().Be("old-access");
		handler.RequestCount.Should().Be(0, because: "the stored token is not the one the server rejected");
	}

	private static IOAuthTokenStore CreateStoreWithToken(DateTimeOffset expiresAt) {
		IOAuthTokenStore store = Substitute.For<IOAuthTokenStore>();
		store.BuildKey(Arg.Any<EnvironmentSettings>()).Returns("key");
		store.TryRead(Arg.Any<EnvironmentSettings>(), out Arg.Any<OAuthTokenSet>())
			.Returns(call => {
				call[1] = BuildToken(expiresAt);
				return true;
			});
		return store;
	}

	private static EnvironmentSettings CreateEnvironment() => new() {
		Uri = "https://work.creatio.com",
		EnvironmentName = "work",
		ClientId = "clio-client",
		AuthFlow = OAuthFlow.AuthorizationCode
	};

	private static OAuthTokenSet BuildToken(DateTimeOffset expiresAt) =>
		new("old-access", "old-refresh", expiresAt, TokenEndpoint, "clio-client", DateTimeOffset.UtcNow);

	private static IOAuthTokenStore CreateStoreWithExpiredToken() {
		IOAuthTokenStore store = Substitute.For<IOAuthTokenStore>();
		store.BuildKey(Arg.Any<EnvironmentSettings>()).Returns("key");
		store.TryRead(Arg.Any<EnvironmentSettings>(), out Arg.Any<OAuthTokenSet>())
			.Returns(call => {
				call[1] = BuildToken(DateTimeOffset.UtcNow.AddSeconds(-10));
				return true;
			});
		return store;
	}

	private static OAuthAuthorizationCodeService CreateService(IOAuthTokenStore store, HttpMessageHandler handler) {
		IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
		return new OAuthAuthorizationCodeService(httpClientFactory, store, Substitute.For<ILogger>(),
			Substitute.For<IServiceUrlBuilder>(), Substitute.For<IProcessExecutor>());
	}

	private sealed class StubHttpMessageHandler : HttpMessageHandler {

		private HttpStatusCode _status;
		private string _body;

		internal int RequestCount { get; private set; }

		internal static StubHttpMessageHandler Returning(HttpStatusCode status, string body) =>
			new() { _status = status, _body = body };

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken) {
			RequestCount++;
			return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
		}
	}
}
