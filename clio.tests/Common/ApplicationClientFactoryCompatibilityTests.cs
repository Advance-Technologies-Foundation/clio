using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ApplicationClientFactoryCompatibilityTests {
	[Test]
	[Description("Wraps a legacy two-method factory without taking ownership of its shared disposable client.")]
	public void CreateOwnedClient_ShouldForwardWithoutDisposingLegacyClient() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient, IDisposable>();
		client.ExecuteGetRequest("route").Returns("response");
		IApplicationClientFactory factory = new LegacyFactory(client);

		// Act
		string result;
		using (IOwnedApplicationClient lease = factory.CreateOwnedClient(new EnvironmentSettings())) {
			result = lease.ExecuteGetRequest("route");
		}

		// Assert
		result.Should().Be("response", because: "the lease must forward the stable client contract");
		((IDisposable)client).DidNotReceive().Dispose();
	}

	[Test]
	[Description("Returns an explicitly owned factory client unchanged so its ownership signal is preserved.")]
	public void CreateOwnedClient_ShouldPreserveOwnedClientIdentity() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		IApplicationClientFactory factory = new LegacyFactory(client);

		// Act
		IOwnedApplicationClient result = factory.CreateOwnedClient(new EnvironmentSettings());

		// Assert
		result.Should().BeSameAs(client, because: "an explicit ownership contract must not be wrapped or weakened");
	}

	[Test]
	[Description("Fails deterministically when extended transport is requested from a pure legacy client.")]
	public async Task CreateOwnedClient_ShouldRejectUnsupportedExtendedOperation() {
		// Arrange
		IApplicationClientFactory factory = new LegacyFactory(Substitute.For<IApplicationClient>());
		using IOwnedApplicationClient lease = factory.CreateOwnedClient(new EnvironmentSettings());

		// Act
		Func<Task> act = () => lease.LoginAsync(cancellationToken: CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<NotSupportedException>(
			because: "legacy implementations never promised the new async and cookie operations");
	}

	private sealed class LegacyFactory(IApplicationClient client) : IApplicationClientFactory {
		public IApplicationClient CreateClient(EnvironmentSettings environment) => client;
		public IApplicationClient CreateEnvironmentClient(EnvironmentSettings environment) => client;
	}
}
