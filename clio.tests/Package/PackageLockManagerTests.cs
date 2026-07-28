using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Property("Module", "Package")]
public class PackageLockManagerTests : BaseClioModuleTests {

	private IApplicationClient _applicationClient;
	private IPackageLockManager _manager;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_applicationClient = Substitute.For<IApplicationClient>();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(_applicationClient);
		containerBuilder.AddSingleton(applicationClientFactory);
		containerBuilder.AddTransient<IPackageLockManager, PackageLockManager>();
	}

	public override void Setup() {
		EnvironmentSettings.IsNetCore = true;
		base.Setup();
		_manager = Container.GetRequiredService<IPackageLockManager>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Wraps a non-JSON gate response (HTTP error page) in a diagnostic InvalidOperationException " +
		"instead of surfacing the raw JsonException, so the real cause is not hidden.")]
	public void Unlock_ShouldThrowClearInvalidOperationException_WhenGateReturnsHtmlErrorPage() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><body>500 - Internal server error.</body></html>");

		// Act
		Action act = () => _manager.Unlock(new List<string>());

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an HTML/non-JSON gate response is a transport-level failure that must be reported clearly")
			.WithMessage("*non-JSON response*",
				because: "the message must point the user at the Creatio logs / cliogate version rather than a cryptic JSON parse error")
			.Which.InnerException.Should().BeOfType<System.Text.Json.JsonException>(
				because: "the original parse failure is preserved as the inner exception for diagnostics");
	}

	[Test]
	[Description("Does not copy a non-JSON response body into the user-facing exception because upstream error pages may contain sensitive details.")]
	public void Unlock_ShouldNotExposeResponseBody_WhenNonJsonResponseIsReturned() {
		// Arrange
		const string responseBody = "sensitive upstream response";
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseBody);

		// Act
		Action act = () => _manager.Unlock(new List<string>());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a non-JSON body is a transport-level failure")
			.Which.Message.Should().NotContain(responseBody,
				because: "upstream error content may contain secrets or internal details that must not reach ordinary output");
	}

	[Test]
	[Description("Wraps an empty gate response in a clear InvalidOperationException instead of surfacing a raw JSON parse error.")]
	public void Unlock_ShouldThrowClearInvalidOperationException_WhenGateReturnsEmptyResponse() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		Action act = () => _manager.Unlock(new List<string>());

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
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns((string)null);

		// Act
		Action act = () => _manager.Unlock(new List<string>());

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
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("true");

		// Act
		Action act = () => _manager.Unlock(new List<string>());

		// Assert
		act.Should().NotThrow(
			because: "a true gate result means the packages were unlocked successfully");
	}

	[Test]
	[Description("Throws the 'returned false' InvalidOperationException when the gate reports a false result.")]
	public void Unlock_ShouldThrowInvalidOperationException_WhenGateReturnsFalse() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("false");

		// Act
		Action act = () => _manager.Unlock(new List<string>());

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a false gate result means the unlock operation did not succeed")
			.WithMessage("*returned false*",
				because: "the existing false-result diagnostic must be preserved");
	}
}
