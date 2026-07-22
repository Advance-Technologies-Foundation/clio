using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal sealed class NoReauthExecutorTests {

	[Test]
	[Description("Execute returns the wrapped call's result verbatim for the passthrough bearer path")]
	public void Execute_ShouldReturnCallResult_WhenInvoked() {
		// Arrange
		NoReauthExecutor sut = new();

		// Act
		string result = sut.Execute(() => "payload", _ => true);

		// Assert
		result.Should().Be("payload",
			because: "NoReauthExecutor must return the wrapped call's result unchanged");
	}

	[Test]
	[Description("Execute invokes the wrapped call exactly once and never retries")]
	public void Execute_ShouldInvokeCallExactlyOnce_WhenInvoked() {
		// Arrange
		NoReauthExecutor sut = new();
		int callCount = 0;

		// Act
		sut.Execute(() => {
			callCount++;
			return "payload";
		}, _ => true);

		// Assert
		callCount.Should().Be(1,
			because: "opaque bearer material cannot re-login, so the call must run exactly once with no retry");
	}

	[Test]
	[Description("Execute never evaluates the unauthorized predicate even when the result looks unauthorized")]
	public void Execute_ShouldNeverInvokePredicate_WhenResultLooksUnauthorized() {
		// Arrange
		NoReauthExecutor sut = new();
		int predicateInvocations = 0;

		// Act
		string result = sut.Execute(() => "login-page", _ => {
			predicateInvocations++;
			return true;
		});

		// Assert
		predicateInvocations.Should().Be(0,
			because: "the unauthorized predicate is intentionally ignored — there is no reauth path to trigger");
		result.Should().Be("login-page",
			because: "the original result is returned without any reauth-driven retry");
	}
}
