using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins the provenance surface the detail responses expose (<c>documentationSource</c> and
/// <c>documentationWarning</c>). Provenance visibility is what justifies the deliberate
/// absence of a CDN fall-through while a local override is active (issue #1361), so the
/// aggregation rules are asserted here rather than only in the TeamCity-only e2e suite.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentDocumentationLoaderTests {
	private const string Version = "8.2.1";

	[Test]
	[Description("An entry declaring no docs produces no provenance at all, so an absent field means 'no documentation exists' rather than 'provenance unknown'.")]
	public async Task LoadAsync_Reports_No_Provenance_When_No_Docs_Are_Declared() {
		// Arrange
		StubDocsClient docsClient = new();

		// Act
		ComponentDocumentationOutcome outcome = await ComponentDocumentationLoader
			.LoadAsync(docsClient, docs: null, Version, CancellationToken.None);

		// Assert
		outcome.Documentation.Should().BeNull(because: "nothing was declared, so nothing can be served");
		outcome.Source.Should().BeNull(
			because: "emitting 'none' here would make a component that ships no docs look like a failed fetch");
		outcome.Warning.Should().BeNull(because: "there is no missing file to warn about");
	}

	[Test]
	[Description("Files served by different tiers aggregate to documentationSource 'mixed'.")]
	public async Task LoadAsync_Reports_Mixed_When_Tiers_Differ() {
		// Arrange
		StubDocsClient docsClient = new StubDocsClient()
			.Seed("docs/a.md", "# A", ComponentDocumentationSource.Local)
			.Seed("docs/b.md", "# B", ComponentDocumentationSource.Cdn);

		// Act
		ComponentDocumentationOutcome outcome = await ComponentDocumentationLoader
			.LoadAsync(docsClient, new List<string> { "docs/a.md", "docs/b.md" }, Version, CancellationToken.None);

		// Assert
		outcome.Source.Should().Be("mixed",
			because: "a response assembled from a working copy and the published CDN must not claim either tier alone");
		outcome.Documentation.Should().Contain("# A").And.Contain("# B",
			because: "both files were served and are concatenated in registry order");
		outcome.Warning.Should().BeNull(because: "every declared file was served");
	}

	[Test]
	[Description("A file served from the working copy that is an empty stub keeps documentationSource 'local' and produces no 'not found' warning.")]
	public async Task LoadAsync_Reports_Local_For_A_Served_But_Empty_File() {
		// Arrange
		StubDocsClient docsClient = new StubDocsClient()
			.Seed("docs/a.md", string.Empty, ComponentDocumentationSource.Local);

		// Act
		ComponentDocumentationOutcome outcome = await ComponentDocumentationLoader
			.LoadAsync(docsClient, new List<string> { "docs/a.md" }, Version, CancellationToken.None);

		// Assert
		outcome.Source.Should().Be("local",
			because: "the file exists in the working copy — it was served, it is simply still an empty generator stub");
		outcome.Documentation.Should().BeNull(because: "an empty stub contributes no markdown block");
		outcome.Warning.Should().BeNull(
			because: "telling the developer to generate a file that already exists is the diagnostic defect being fixed");
	}

	[Test]
	[Description("When every declared file misses under an active override the outcome is 'none' with a warning naming each registry-relative path and the override variable, and no host path.")]
	public async Task LoadAsync_Reports_None_And_Warns_For_Every_Local_Miss() {
		// Arrange
		StubDocsClient docsClient = new StubDocsClient()
			.SeedLocalMiss("docs/a.md", RegistryFlavor.Web.LocalFileEnvironmentVariable)
			.SeedLocalMiss("docs/b.md", RegistryFlavor.Web.LocalFileEnvironmentVariable);

		// Act
		ComponentDocumentationOutcome outcome = await ComponentDocumentationLoader
			.LoadAsync(docsClient, new List<string> { "docs/a.md", "docs/b.md" }, Version, CancellationToken.None);

		// Assert
		outcome.Documentation.Should().BeNull(
			because: "substituting the published CDN copy for a missing local file is the defect being fixed");
		outcome.Source.Should().Be("none", because: "no tier served anything");
		outcome.Warning.Should().Contain("docs/a.md").And.Contain("docs/b.md",
			because: "the developer has to know every recipe still to generate, not just the first");
		outcome.Warning.Should().Contain(RegistryFlavor.Web.LocalFileEnvironmentVariable,
			because: "naming the override that captured the paths is what makes the warning actionable");
	}

	private sealed class StubDocsClient : IComponentRegistryDocsClient {
		private readonly Dictionary<string, ComponentDocumentationFetchResult> _results = new();

		public StubDocsClient Seed(string docPath, string content, ComponentDocumentationSource source) {
			_results[docPath] = new ComponentDocumentationFetchResult(content, source);
			return this;
		}

		public StubDocsClient SeedLocalMiss(string docPath, string overrideVariable) {
			_results[docPath] = new ComponentDocumentationFetchResult(
				Content: null, ComponentDocumentationSource.None, overrideVariable);
			return this;
		}

		public Task<ComponentDocumentationFetchResult> GetDocAsync(
			string version, string docPath, CancellationToken cancellationToken = default) =>
			Task.FromResult(_results.TryGetValue(docPath, out ComponentDocumentationFetchResult? result)
				? result
				: ComponentDocumentationFetchResult.Missing);
	}
}
