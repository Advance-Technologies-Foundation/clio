using System;
using Clio;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.Kubernetes;

[TestFixture]
[Property("Module", "Common")]
public sealed class FakeKubernetesTests
{
	[Test]
	[Category("Unit")]
	[Description("Regression (ENG-91830): FakeKubernetes is the graceful fallback IKubernetes client " +
		"returned when no kubeconfig is present. The MCP server resolves it inside a per-request DI " +
		"scope and disposes that scope when the tool call completes, so a throwing Dispose surfaced as " +
		"an opaque MCP InternalError for every infrastructure tool on a no-Kubernetes host. Dispose must " +
		"be a no-op.")]
	public void Dispose_ShouldNotThrow_WhenInvokedByScopeTeardown()
	{
		// Arrange
		FakeKubernetes fakeKubernetes = new();

		// Act
		Action dispose = fakeKubernetes.Dispose;

		// Assert
		dispose.Should().NotThrow(
			because: "the no-Kubernetes fallback owns no unmanaged resources and the MCP per-request DI " +
				"scope disposes it on every tool call, so a throwing Dispose breaks every infrastructure tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Regression (ENG-91830): repeated DI-scope teardowns must keep Dispose safe, so the " +
		"no-op contract holds when the fallback client is disposed more than once.")]
	public void Dispose_ShouldRemainSafe_WhenInvokedMultipleTimes()
	{
		// Arrange
		FakeKubernetes fakeKubernetes = new();

		// Act
		Action disposeTwice = () =>
		{
			fakeKubernetes.Dispose();
			fakeKubernetes.Dispose();
		};

		// Assert
		disposeTwice.Should().NotThrow(
			because: "Dispose is a no-op and must stay idempotent across repeated scope teardowns");
	}
}
