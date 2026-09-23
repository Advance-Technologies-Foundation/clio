using System;
using Clio.Common;
using Creatio.Client;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Unit tests for the renewable <see cref="CreatioClientTransport"/> used by OAuth authorization-code
/// clients. The clients are bearer-token clients, whose construction makes no network call.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal sealed class CreatioClientTransportRenewTests {

	[Test]
	[Description("After Renew the next use builds a new client through the factory, so it carries the current token.")]
	public void Renew_ShouldBuildANewClientOnNextUse() {
		// Arrange
		int built = 0;
		using CreatioClientTransport sut = new(() => {
			built++;
			return new CreatioClient("https://creatio.test", "token-" + built, true);
		});
		sut.EnsureCreated();

		// Act
		sut.Renew();
		sut.EnsureCreated();
		sut.EnsureCreated();

		// Assert
		built.Should().Be(2);
		sut.IsCreated.Should().BeTrue();
	}

	[Test]
	[Description("Renewing a transport whose client was never built creates nothing, so an unused client still costs no token read.")]
	public void Renew_ShouldNotBuildAClient_WhenNoneWasBuiltYet() {
		// Arrange
		int built = 0;
		using CreatioClientTransport sut = new(() => {
			built++;
			return new CreatioClient("https://creatio.test", "token", true);
		});

		// Act
		sut.Renew();

		// Assert
		built.Should().Be(0);
		sut.IsCreated.Should().BeFalse();
	}

	[Test]
	[Description("A replaced client may still be serving a request on another thread, so it stays usable until the transport is disposed, and is disposed with it.")]
	public void Dispose_ShouldDisposeReplacedClients() {
		// Arrange
		CreatioClient first = new("https://creatio.test", "token-1", true);
		bool firstReturned = false;
		CreatioClientTransport sut = new(() => {
			if (firstReturned) {
				return new CreatioClient("https://creatio.test", "token-2", true);
			}
			firstReturned = true;
			return first;
		});
		sut.EnsureCreated();
		sut.Renew();

		// Act
		Action useBeforeDispose = () => first.ExportSessionCookies();
		useBeforeDispose.Should().NotThrow(because: "a replaced client is kept alive until the transport is disposed");
		sut.Dispose();
		Action useAfterDispose = () => first.ExportSessionCookies();

		// Assert
		useAfterDispose.Should().Throw<ObjectDisposedException>();
	}

	[Test]
	[Description("A failed first build is not cached: once a token is available (for example after clio login), the next use builds the client.")]
	public void Client_ShouldRebuild_WhenTheFirstBuildThrew() {
		// Arrange
		int attempts = 0;
		using CreatioClientTransport sut = new(() => {
			attempts++;
			if (attempts == 1) {
				throw new InvalidOperationException("no valid session");
			}
			return new CreatioClient("https://creatio.test", "token", true);
		});
		Action firstUse = sut.EnsureCreated;
		firstUse.Should().Throw<InvalidOperationException>();

		// Act
		Action secondUse = sut.EnsureCreated;

		// Assert
		secondUse.Should().NotThrow(because: "a faulted build must not stick in the renewable transport");
		attempts.Should().Be(2);
		sut.IsCreated.Should().BeTrue();
	}

	[Test]
	[Description("A transport over a fixed lazy client has no factory to build a replacement with, so Renew fails loudly.")]
	public void Renew_ShouldThrow_WhenTheTransportWrapsAFixedClient() {
		// Arrange
		using CreatioClientTransport sut = new(new Lazy<CreatioClient>(() => null));

		// Act
		Action act = sut.Renew;

		// Assert
		act.Should().Throw<InvalidOperationException>();
	}
}
