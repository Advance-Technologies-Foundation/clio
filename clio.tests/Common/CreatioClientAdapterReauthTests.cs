using System;
using Clio.Common;
using Creatio.Client;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class CreatioClientAdapterReauthTests {

	[Test]
	[Description("When the first response is valid, the service call runs once and re-auth is not invoked.")]
	public void ExecuteWithReauthRetry_ValidResponse_DoesNotReauthenticate() {
		int calls = 0;
		int reauths = 0;

		string result = CreatioClientAdapter.ExecuteWithReauthRetry(
			() => { calls++; return "{\"success\":true}"; },
			() => reauths++);

		result.Should().Be("{\"success\":true}");
		calls.Should().Be(1, "because a valid response needs no retry");
		reauths.Should().Be(0, "because the session was alive");
	}

	[Test]
	[Description("On a login redirect, re-auth runs once and the call is retried exactly once, returning the recovered response.")]
	public void ExecuteWithReauthRetry_ExpiredSession_RelogsInAndRetriesOnce() {
		int calls = 0;
		int reauths = 0;

		string result = CreatioClientAdapter.ExecuteWithReauthRetry(
			() => { calls++; return calls == 1 ? "<!DOCTYPE html><html>NuiLogin</html>" : "{\"success\":true}"; },
			() => reauths++);

		result.Should().Be("{\"success\":true}", "because the retry after re-login succeeds");
		calls.Should().Be(2, "because the call runs once, hits the login page, then runs once more");
		reauths.Should().Be(1, "because re-auth happens exactly once");
	}

	[Test]
	[Description("A persistent auth failure (re-auth did not restore the session) does not loop: the call runs exactly twice, then a typed auth error is thrown instead of returning the login/401 body to a deserializer.")]
	public void ExecuteWithReauthRetry_PersistentAuthFailure_ThrowsTypedAuthError() {
		int calls = 0;
		int reauths = 0;
		const string login = "<!DOCTYPE html><html>NuiLogin</html>";

		Action act = () => CreatioClientAdapter.ExecuteWithReauthRetry(
			() => { calls++; return login; },
			() => reauths++);

		act.Should().Throw<UnauthorizedAccessException>("because re-auth did not restore the session");
		calls.Should().Be(2, "because there is exactly one retry — no third attempt");
		reauths.Should().Be(1, "because re-auth is attempted once");
	}

	[Test]
	[Description("A wrong-credentials UnauthorizedAccessException from re-auth propagates and the call is not retried.")]
	public void ExecuteWithReauthRetry_BadCredentials_PropagatesWithoutRetry() {
		int calls = 0;

		Action act = () => CreatioClientAdapter.ExecuteWithReauthRetry(
			() => { calls++; return "<!DOCTYPE html><html>NuiLogin</html>"; },
			() => throw new UnauthorizedAccessException("bad creds"));

		act.Should().Throw<UnauthorizedAccessException>("because wrong credentials are not an expiry to retry");
		calls.Should().Be(1, "because the retry must not run when re-auth itself fails");
	}

	[Test]
	[Description("Version gate: an observed version that is already stale (renewed by another caller) skips the redundant login and leaves the version unchanged.")]
	public void ReauthenticateOnce_StaleObservedVersion_SkipsLogin() {
		CreatioClientAdapter adapter = NewAdapterWithoutClient();
		int logins = 0;
		int baseline = adapter.SessionVersion;

		adapter.ReauthenticateOnce(baseline, () => logins++);   // matches → renews, version = baseline + 1
		adapter.ReauthenticateOnce(baseline, () => logins++);   // now stale → must skip

		logins.Should().Be(1, "because the second caller observed a stale version and must skip the redundant login");
		adapter.SessionVersion.Should().Be(baseline + 1, "because only the first re-login advanced the version");
	}

	[Test]
	[Description("Version gate: a matching observed version triggers exactly one login and advances the version by one.")]
	public void ReauthenticateOnce_MatchingObservedVersion_LogsInOnceAndBumps() {
		CreatioClientAdapter adapter = NewAdapterWithoutClient();
		int logins = 0;
		int observed = adapter.SessionVersion;

		adapter.ReauthenticateOnce(observed, () => logins++);

		logins.Should().Be(1, "because the observed version matched the current generation");
		adapter.SessionVersion.Should().Be(observed + 1, "because a successful re-login advances the generation");
	}

	[Test]
	[Description("Version gate ordering: a failing login must NOT advance the version (the bump happens only after login succeeds), so the next caller still re-authenticates. This guards against reordering the increment before the login.")]
	public void ReauthenticateOnce_LoginThrows_DoesNotAdvanceVersionAndPropagates() {
		CreatioClientAdapter adapter = NewAdapterWithoutClient();
		int observed = adapter.SessionVersion;

		Action act = () => adapter.ReauthenticateOnce(observed, () => throw new UnauthorizedAccessException("bad creds"));

		act.Should().Throw<UnauthorizedAccessException>("because a failed re-login must surface");
		adapter.SessionVersion.Should().Be(observed, "because the version must not advance when login fails");
	}

	// The version gate never touches the live client (login is injected), so a never-resolved
	// Lazy is enough to construct the adapter under test.
	private static CreatioClientAdapter NewAdapterWithoutClient() =>
		new(new Lazy<CreatioClient>(() => null));
}
