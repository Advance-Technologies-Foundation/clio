using System;
using System.Linq;
using System.Reflection;
using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ExternalKnowledgeBoundaryTests {
	[Test]
	[Description("Prevents article-specific MCP resource classes from returning to the Clio binary.")]
	public void McpAssembly_ShouldContainOnlyGenericKnowledgeResources_WhenInspected() {
		// Arrange
		Assembly assembly = typeof(MultiSourceKnowledgeResource).Assembly;

		// Act
		Type[] articleResourceTypes = assembly.GetTypes()
			.Where(type => type.GetCustomAttribute<McpServerResourceTypeAttribute>() is not null)
			.Where(type => type.Name.Contains("Guidance", StringComparison.Ordinal))
			.ToArray();
		MethodInfo[] legacyKnowledgeResources = assembly.GetTypes()
			.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
			.Where(method => method.GetCustomAttribute<McpServerResourceAttribute>()?.UriTemplate is string uri
				&& (uri.StartsWith("docs://mcp/guides/", StringComparison.Ordinal)
					|| uri.StartsWith("docs://mcp/references/", StringComparison.Ordinal)))
			.ToArray();

		// Assert
		articleResourceTypes.Should().BeEmpty(
			because: "guidance article content and discovery metadata belong to external knowledge libraries");
		legacyKnowledgeResources.Should().OnlyContain(
			method => method.DeclaringType == typeof(MultiSourceKnowledgeResource),
			because: "Clio may retain only generic publisher-declared legacy guidance and reference URI adapters");
	}
}
