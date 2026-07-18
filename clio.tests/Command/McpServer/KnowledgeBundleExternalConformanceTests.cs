using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Integration")]
[Property("Module", "McpServer")]
public sealed class KnowledgeBundleExternalConformanceTests {
	private ServiceProvider _container;

	[TearDown]
	public void TearDown() {
		_container?.Dispose();
	}

	[Test]
	[Description("Serves the external clio-knowledge ESQ bundle byte-identically without copying guidance into the Clio repository.")]
	public void Activate_Should_Match_External_Frozen_Oracle_When_Conformance_Root_Is_Provided() {
		// Arrange
		string root = Environment.GetEnvironmentVariable("CLIO_KNOWLEDGE_CONFORMANCE_ROOT");
		if (string.IsNullOrWhiteSpace(root)) {
			Assert.Ignore("Set CLIO_KNOWLEDGE_CONFORMANCE_ROOT to a clio-knowledge checkout to run cross-repository conformance.");
		}
		string bundlePath = Path.Combine(root, "fixtures", "bundles", "esq-v0", "valid.zip");
		string publicKeyPath = Path.Combine(root, "fixtures", "keys", "p1-test-public.pem");
		string oraclePath = Path.Combine(root, "fixtures", "oracles", "esq", "resources", "esq-filters.md");
		IKnowledgeBundleTrustStore trustStore = Substitute.For<IKnowledgeBundleTrustStore>();
		string publicKey = File.ReadAllText(publicKeyPath);
		trustStore.TryGetPublicKeyPem("p1-test", out Arg.Any<string>())
			.Returns(callInfo => {
				callInfo[1] = publicKey;
				return true;
			});
		ServiceCollection services = new();
		services.AddSingleton(trustStore);
		services.AddSingleton(new KnowledgeBundleClientCapabilities(
			new Version(8, 1, 0),
			new Version(1, 0, 0),
			new HashSet<string>(StringComparer.Ordinal) { "get-guidance" }));
		services.AddSingleton<IKnowledgeBundleRuntime, KnowledgeBundleRuntime>();
		_container = services.BuildServiceProvider();
		IKnowledgeBundleRuntime runtime = _container.GetRequiredService<IKnowledgeBundleRuntime>();
		using FileStream candidate = File.OpenRead(bundlePath);
		byte[] expectedBytes = File.ReadAllBytes(oraclePath);

		// Act
		KnowledgeBundleActivationResult activation = runtime.Activate(candidate);
		KnowledgeArticleLookup lookup = runtime.Find("esq-filters");

		// Assert
		activation.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "the external producer and Clio consumer must conform to the same signed v0 contract");
		lookup.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "the frozen ESQ article is declared by the verified external bundle");
		Encoding.UTF8.GetBytes(lookup.Article!.Text).Should().Equal(expectedBytes,
			because: "externalization must preserve the compiled Clio oracle byte-for-byte");
	}
}
