using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using Creatio.Client;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

// These tests intentionally exercise the obsolete facade to protect existing binary consumers.
#pragma warning disable CS0618
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CreatioAuthClientCompatibilityTests {
	[Test]
	[Description("Maps CreatioClient credential rejection to the established sanitized browser authentication error.")]
	public async Task LoginAsync_ShouldMapUnauthorizedAccessException() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new UnauthorizedAccessException("secret-value"));
		CreatioAuthClient sut = new(_ => client);

		// Act
		Func<Task> act = () => sut.LoginAsync(Environment());

		// Assert
		(await act.Should().ThrowAsync<CreatioAuthenticationException>(
			because: "the compatibility facade must retain its former rejection classification"))
			.Which.Message.Should().NotContain("secret-value",
				because: "credential details must never be exposed");
		await client.Received(1).LoginAsync(30_000, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Exports the authenticated CreatioClient cookies in browser storage-state shape.")]
	public async Task LoginAsync_ShouldExportSessionCookies() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
		client.ExportSessionCookies().Returns([
			new CreatioSessionCookie(".ASPXAUTH", "token", "dev.creatio.com", "/", true, true, "Lax",
				DateTime.MinValue)
		]);
		CreatioAuthClient sut = new(_ => client);

		// Act
		StorageStateResult result = await sut.LoginAsync(Environment());

		// Assert
		result.Cookies.Should().ContainSingle(cookie => cookie.Name == ".ASPXAUTH" && cookie.Value == "token",
			because: "the compatibility facade must preserve browser cookie handoff");
		client.Received(1).Dispose();
	}

	[Test]
	[Description("Propagates caller cancellation with the original cancellation token.")]
	public async Task LoginAsync_ShouldPropagateCallerCancellation() {
		// Arrange
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(call => throw new OperationCanceledException(call.Arg<CancellationToken>()));
		CreatioAuthClient sut = new(_ => client);

		// Act
		Func<Task> act = () => sut.LoginAsync(Environment(), cancellation.Token);

		// Assert
		(await act.Should().ThrowAsync<OperationCanceledException>(
			because: "caller cancellation must not be translated into a connectivity error"))
			.Which.CancellationToken.Should().Be(cancellation.Token,
				because: "the original token identifies caller cancellation");
	}

	[Test]
	[Description("Maps an internal login timeout to the established sanitized connectivity error.")]
	public async Task LoginAsync_ShouldMapUncancelledTimeoutToConnectivityError() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new OperationCanceledException("secret-timeout"));
		CreatioAuthClient sut = new(_ => client);

		// Act
		Func<Task> act = () => sut.LoginAsync(Environment());

		// Assert
		(await act.Should().ThrowAsync<CreatioAuthenticationException>(
			because: "an uncancelled transport timeout is a connectivity failure"))
			.Which.Message.Should().NotContain("secret-timeout",
				because: "transport details must not leak through the compatibility facade");
	}

	[Test]
	[Description("Maps an HTTP transport failure to the established sanitized connectivity error.")]
	public async Task LoginAsync_ShouldMapHttpFailureToConnectivityError() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new HttpRequestException("secret-host"));
		CreatioAuthClient sut = new(_ => client);

		// Act
		Func<Task> act = () => sut.LoginAsync(Environment());

		// Assert
		(await act.Should().ThrowAsync<CreatioAuthenticationException>(
			because: "an HTTP failure is a connectivity failure"))
			.Which.Message.Should().NotContain("secret-host",
				because: "transport details must not leak through the compatibility facade");
	}

	private static EnvironmentSettings Environment() => new() {
		Uri = "https://dev.creatio.com",
		Login = "Supervisor",
		Password = "secret"
	};
}
#pragma warning restore CS0618
