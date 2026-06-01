using System;
using Clio.Common;
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
	[Description("A persistent login redirect does not loop: the call runs at most twice and the second body is returned as-is.")]
	public void ExecuteWithReauthRetry_PersistentRedirect_DoesNotLoop() {
		int calls = 0;
		int reauths = 0;
		const string login = "<!DOCTYPE html><html>NuiLogin</html>";

		string result = CreatioClientAdapter.ExecuteWithReauthRetry(
			() => { calls++; return login; },
			() => reauths++);

		result.Should().Be(login, "because the second result is returned without further detection");
		calls.Should().Be(2, "because there is exactly one retry");
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
}
