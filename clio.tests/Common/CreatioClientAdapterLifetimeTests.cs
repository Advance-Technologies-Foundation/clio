using System;
using System.Net.Http;
using System.Threading.Tasks;
using Clio;
using Clio.Common;
using Creatio.Client;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal sealed class CreatioClientAdapterLifetimeTests {

	[Test]
	[Description("Disposing an adapter around a borrowed DI client does not dispose the shared CreatioClient")]
	public void Dispose_ShouldKeepClientUsable_WhenClientIsBorrowed() {
		// Arrange
		using CreatioClient client = new("https://localhost", "Supervisor", "Supervisor", true);
		CreatioClientAdapter sut = new(client);

		// Act
		sut.Dispose();
		Action clientAct = () => client.ExportSessionCookies();
		Action adapterAct = () => sut.ExportSessionCookies();

		// Assert
		clientAct.Should().NotThrow(
			because: "DI-owned clients can outlive an adapter while a SignalR listener finishes cancellation");
		adapterAct.Should().Throw<ObjectDisposedException>(
			because: "disposing the adapter must still prevent new operations through that adapter");
	}

	[Test]
	[Description("Disposing a factory-created adapter disposes its short-lived owned CreatioClient")]
	public void Dispose_ShouldDisposeClient_WhenClientIsFactoryOwned() {
		// Arrange
		ApplicationClientFactory factory = new(Substitute.For<IReauthExecutor>());
		IOwnedApplicationClient sut = factory.CreateFormsEnvironmentClient(new EnvironmentSettings {
			Uri = "https://localhost",
			Login = "Supervisor",
			Password = "Supervisor"
		});
		// Act
		sut.Dispose();
		Action act = () => sut.ExportSessionCookies();

		// Assert
		act.Should().Throw<ObjectDisposedException>(
			because: "disposing before first use must not allow the lazy client to be created afterward");
	}

	[Test]
	[Description("Disposing an owned adapter closes the captured CreatioClient transport.")]
	public async Task Dispose_ShouldCloseUnderlyingTransport_WhenOwnedClientWasInitialized() {
		// Arrange
		CreatioClient client = new("https://localhost", "token", useUntrustedSsl: false, isNetCore: true);
		CreatioClientAdapter sut = new(new Lazy<CreatioClient>(() => client), null,
			new NoReauthExecutor(), ownsClient: true);
		sut.ExportSessionCookies();

		// Act
		sut.Dispose();
		Func<Task> act = async () => {
			using HttpResponseMessage _ = await client.ExecuteGetRequestAsync("https://localhost/probe");
		};

		// Assert
		await act.Should().ThrowAsync<ObjectDisposedException>(
			because: "disposing the adapter must close the captured owned CreatioClient transport itself");
	}

	[Test]
	[Description("Disposing an adapter around a caller-supplied lazy client keeps that client borrowed.")]
	public void Dispose_ShouldKeepClientUsable_WhenLazyClientIsBorrowed() {
		// Arrange
		using CreatioClient client = new("https://localhost", "token", useUntrustedSsl: false, isNetCore: true);
		CreatioClientAdapter sut = new(new Lazy<CreatioClient>(() => client));
		sut.ExportSessionCookies();

		// Act
		sut.Dispose();
		Action act = () => client.ExportSessionCookies();

		// Assert
		act.Should().NotThrow(
			because: "caller-supplied direct and lazy clients must have identical borrowed ownership semantics");
	}
}
