using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class PackageLockManagerTests {

	private static (PackageLockManager manager, IApplicationClient applicationClient) CreateManager() {
		// Real ServiceUrlBuilder so the ClioGate route mapping is exercised; only the HTTP boundary is faked.
		EnvironmentSettings environmentSettings = new() {
			Uri = "http://localhost",
			IsNetCore = true
		};
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(applicationClient);
		IServiceUrlBuilder serviceUrlBuilder = new ServiceUrlBuilder(environmentSettings);
		PackageLockManager manager =
			new(environmentSettings, applicationClientFactory, serviceUrlBuilder);
		return (manager, applicationClient);
	}

	[Test]
	[Description("Wraps a non-JSON gate response (HTTP error page) in a diagnostic InvalidOperationException " +
		"instead of surfacing the raw JsonException, so the real cause is not hidden.")]
	public void Unlock_ShouldThrowClearInvalidOperationException_WhenGateReturnsHtmlErrorPage() {
		// Arrange
		(PackageLockManager manager, IApplicationClient applicationClient) = CreateManager();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><body>500 - Internal server error.</body></html>");

		// Act
		Action act = () => manager.Unlock(new List<string>());

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an HTML/non-JSON gate response is a transport-level failure that must be reported clearly")
			.WithMessage("*non-JSON response*",
				because: "the message must point the user at the Creatio logs / cliogate version rather than a cryptic JSON parse error")
			.Which.InnerException.Should().BeOfType<System.Text.Json.JsonException>(
				because: "the original parse failure is preserved as the inner exception for diagnostics");
	}

	[Test]
	[Description("Caps the echoed response-body excerpt at 200 characters so a multi-kilobyte HTML error page does not flood the error message.")]
	public void Unlock_ShouldTruncateEchoedBody_WhenNonJsonResponseIsLarge() {
		// Arrange
		(PackageLockManager manager, IApplicationClient applicationClient) = CreateManager();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new string('x', 5000));

		// Act
		Action act = () => manager.Unlock(new List<string>());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a non-JSON body is a transport-level failure")
			.Which.Message.Should()
				.Contain(new string('x', 200) + "...",
					because: "the echoed body is capped at exactly 200 characters followed by an ellipsis")
				.And.NotContain(new string('x', 201),
					because: "no more than 200 characters of the raw body are echoed into the message");
	}

	[Test]
	[Description("Wraps an empty gate response in a clear InvalidOperationException instead of surfacing a raw JSON parse error.")]
	public void Unlock_ShouldThrowClearInvalidOperationException_WhenGateReturnsEmptyResponse() {
		// Arrange
		(PackageLockManager manager, IApplicationClient applicationClient) = CreateManager();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		Action act = () => manager.Unlock(new List<string>());

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an empty gate response is a transport-level failure that must be reported clearly")
			.WithMessage("*empty response*",
				because: "the message must point the user at the Creatio logs / cliogate version");
	}

	[Test]
	[Description("Wraps a null gate response in a clear InvalidOperationException rather than letting a raw ArgumentNullException escape the JSON parse.")]
	public void Unlock_ShouldThrowClearInvalidOperationException_WhenGateReturnsNullResponse() {
		// Arrange
		(PackageLockManager manager, IApplicationClient applicationClient) = CreateManager();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns((string)null);

		// Act
		Action act = () => manager.Unlock(new List<string>());

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a null gate response must be guarded before JSON parsing, not surface as a raw ArgumentNullException")
			.WithMessage("*empty response*",
				because: "null and empty both indicate the gate returned no usable body");
	}

	[Test]
	[Description("Completes without throwing when the gate returns a JSON true result for the unlock-all (empty) payload.")]
	public void Unlock_ShouldNotThrow_WhenGateReturnsTrue() {
		// Arrange
		(PackageLockManager manager, IApplicationClient applicationClient) = CreateManager();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("true");

		// Act
		Action act = () => manager.Unlock(new List<string>());

		// Assert
		act.Should().NotThrow(
			because: "a true gate result means the packages were unlocked successfully");
	}

	[Test]
	[Description("Throws the 'returned false' InvalidOperationException when the gate reports a false result.")]
	public void Unlock_ShouldThrowInvalidOperationException_WhenGateReturnsFalse() {
		// Arrange
		(PackageLockManager manager, IApplicationClient applicationClient) = CreateManager();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("false");

		// Act
		Action act = () => manager.Unlock(new List<string>());

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a false gate result means the unlock operation did not succeed")
			.WithMessage("*returned false*",
				because: "the existing false-result diagnostic must be preserved");
	}
}
