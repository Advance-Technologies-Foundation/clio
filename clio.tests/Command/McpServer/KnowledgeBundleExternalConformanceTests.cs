using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
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
		services.AddSingleton(Substitute.For<IKnowledgeBundleActivator>());
		services.AddSingleton(Substitute.For<IFeatureToggleService>());
		services.AddSingleton<IKnowledgeGuidanceSource, KnowledgeGuidanceSource>();
		services.AddSingleton<KnowledgeGuidanceResourceAdapter>();
		services.AddTransient<GuidanceGetTool>();
		services.AddTransient<EsqFiltersGuidanceResource>();
		_container = services.BuildServiceProvider();
		IKnowledgeBundleRuntime runtime = _container.GetRequiredService<IKnowledgeBundleRuntime>();
		GuidanceGetTool tool = _container.GetRequiredService<GuidanceGetTool>();
		EsqFiltersGuidanceResource resource = _container.GetRequiredService<EsqFiltersGuidanceResource>();
		using FileStream candidate = File.OpenRead(bundlePath);
		byte[] expectedBytes = File.ReadAllBytes(oraclePath);

		// Act
		KnowledgeBundleActivationResult activation = runtime.Activate(candidate);
		GuidanceGetResponse toolResponse = tool.GetGuidance(new GuidanceGetArgs("esq-filters")).GetAwaiter().GetResult();
		TextResourceContents resourceResponse = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the real docs resource adapter must expose text guidance").Which;

		// Assert
		activation.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "the external producer and Clio consumer must conform to the same signed v0 contract");
		toolResponse.Success.Should().BeTrue(
			because: "get-guidance must delegate to the active verified external bundle");
		Encoding.UTF8.GetBytes(toolResponse.Article!.Text).Should().Equal(expectedBytes,
			because: "get-guidance must preserve the compiled Clio oracle byte-for-byte");
		Encoding.UTF8.GetBytes(resourceResponse.Text).Should().Equal(expectedBytes,
			because: "docs resources must preserve the same compiled Clio oracle byte-for-byte");
	}
}
