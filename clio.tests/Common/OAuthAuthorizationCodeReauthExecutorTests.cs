using System;
using System.Collections.Generic;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Unit tests for <see cref="OAuthAuthorizationCodeReauthExecutor"/>: the client of an OAuth
/// authorization-code environment is renewed with a current token instead of keeping the one it was built
/// with for the lifetime of its container (the mcp-server per-session case).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal sealed class OAuthAuthorizationCodeReauthExecutorTests {

	private const string LoginPage = "<html><form action=\"/Login/NuiLogin.aspx\"></form></html>";

	private string _currentToken;
	private List<string> _refreshedFrom;
	private int _renewCount;
	private OAuthAuthorizationCodeReauthExecutor _sut;

	[SetUp]
	public void SetUp() {
		_currentToken = "token-1";
		_refreshedFrom = [];
		_renewCount = 0;
		_sut = new OAuthAuthorizationCodeReauthExecutor(
			() => _currentToken,
			rejected => {
				_refreshedFrom.Add(rejected);
				_currentToken = "token-refreshed";
				return _currentToken;
			},
			() => _renewCount++);
	}

	[Test]
	[Description("Before any client exists there is no token to compare, so the call runs as is and nothing is renewed.")]
	public void Execute_ShouldNotRenew_WhenNoClientHasBeenBuilt() {
		// Act
		string result = _sut.Execute(() => "ok", ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);

		// Assert
		result.Should().Be("ok");
		_renewCount.Should().Be(0, because: "the factory resolves a current token when it builds the first client");
	}

	[Test]
	[Description("While the token the client carries is still the current one, calls cost no renewal.")]
	public void Execute_ShouldNotRenew_WhenTheTokenIsUnchanged() {
		// Arrange
		_sut.AcquireClientToken();

		// Act
		_sut.Execute(() => "ok", ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);
		_sut.Execute(() => "ok", ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);

		// Assert
		_renewCount.Should().Be(0);
	}

	[Test]
	[Description("Once the OAuth service returns a different token (it refreshed inside the pre-expiry window), the client is renewed before the call, exactly once, so the request never goes out with the expired token.")]
	public void Execute_ShouldRenewTheClientBeforeTheCall_WhenTheTokenWasReplaced() {
		// Arrange
		_sut.AcquireClientToken();
		_currentToken = "token-2";
		int renewsSeenByCall = -1;

		// Act
		_sut.Execute(() => {
			renewsSeenByCall = _renewCount;
			return "ok";
		}, ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);
		_sut.Execute(() => "ok", ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);

		// Assert
		renewsSeenByCall.Should().Be(1, because: "the renewal must happen before the request is sent");
		_renewCount.Should().Be(1,
			because: "until the renewed client is built there is no carried token, so the second call must not renew again");
		_refreshedFrom.Should().BeEmpty(because: "an ordinary expiry is handled by the service without a forced refresh");
	}

	[Test]
	[Description("A new client built after a renewal records its token, so a later replacement is detected again.")]
	public void Execute_ShouldRenewAgain_AfterTheRenewedClientRecordedItsToken() {
		// Arrange
		_sut.AcquireClientToken();
		_currentToken = "token-2";
		_sut.Execute(() => "ok", ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);
		_sut.AcquireClientToken().Should().Be("token-2");
		_currentToken = "token-3";

		// Act
		_sut.Execute(() => "ok", ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);

		// Assert
		_renewCount.Should().Be(2);
	}

	[Test]
	[Description("A response that reads as an expired session forces a refresh of the rejected token (its expiry has not been reached, so an ordinary resolve would return it again), renews the client and retries the call once.")]
	public void Execute_ShouldRefreshTheRejectedTokenAndRetry_WhenTheResponseIsAnExpiredSession() {
		// Arrange
		_sut.AcquireClientToken();
		int calls = 0;

		// Act
		string result = _sut.Execute(() => ++calls == 1 ? LoginPage : "ok",
			ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);

		// Assert
		result.Should().Be("ok");
		calls.Should().Be(2);
		_refreshedFrom.Should().Equal(["token-1"], because: "the token the server rejected is the one to refresh");
		_renewCount.Should().Be(1);
	}

	[Test]
	[Description("A write is never issued twice: the token is still refreshed and the client renewed for the next call, but the original response is returned.")]
	public void Execute_ShouldNotReplayAWrite_WhenTheResponseIsAnExpiredSession() {
		// Arrange
		_sut.AcquireClientToken();
		int calls = 0;

		// Act
		string result = _sut.Execute(() => {
			calls++;
			return LoginPage;
		}, ReauthExecutor.IsSessionExpiredResponse, replayAllowed: false);

		// Assert
		result.Should().Be(LoginPage);
		calls.Should().Be(1);
		_refreshedFrom.Should().ContainSingle();
		_renewCount.Should().Be(1);
	}

	[Test]
	[Description("When the refresh itself fails (the refresh token is dead), its actionable error reaches the caller instead of a silent retry with the rejected token.")]
	public void Execute_ShouldSurfaceTheRefreshFailure_WhenTheTokenCannotBeRefreshed() {
		// Arrange
		OAuthAuthorizationCodeReauthExecutor sut = new(() => "token-1",
			_ => throw new InvalidOperationException("Run: clio login -e work"), () => _renewCount++);
		sut.AcquireClientToken();

		// Act
		Action act = () => sut.Execute(() => LoginPage, ReauthExecutor.IsSessionExpiredResponse, replayAllowed: true);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*clio login*");
		_renewCount.Should().Be(0);
	}
}
