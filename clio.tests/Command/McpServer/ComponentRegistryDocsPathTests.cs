using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentRegistryDocsPathTests {
	[TestCase("docs/data-grid.component.md")]
	[TestCase("docs/some-folder/nested.md")]
	[TestCase("docs/single.md")]
	[TestCase("docs/A_B-C.0.1.md")]
	[TestCase("mobile-docs/mobile-folder-tree-actions.component.md")]
	[TestCase("mobile-docs/some-folder/nested.md")]
	[TestCase("request-docs/close-page.request.md")]
	[TestCase("request-docs/some-folder/nested.request.md")]
	[Description("Well-formed docs paths emitted by the producers pass the validator unchanged — the component docs/ namespace, the mobile-docs/ namespace, and the request-docs/ namespace referenced from RequestRegistry.json.")]
	public void TryNormalise_Accepts_WellFormed_Paths(string input) {
		bool ok = ComponentRegistryDocsPath.TryNormalise(input, out string normalised);

		ok.Should().BeTrue(because: "the path matches the documented producer contract");
		normalised.Should().Be(input, because: "no rewriting is needed for valid paths");
	}

	[TestCase("../etc/passwd.md", Description = "Parent-directory escape via ../ is blocked.")]
	[TestCase("docs/../../etc/passwd.md", Description = "Embedded ../ in an otherwise-rooted path is blocked.")]
	[TestCase("/docs/data-grid.component.md", Description = "Leading slash (absolute path) is rejected.")]
	[TestCase("docs\\windows\\style.md", Description = "Backslashes never appear in a valid relative path.")]
	[TestCase("https://academy.creatio.com/api/mcp/8.3.4/docs/data-grid.component.md", Description = "Full URLs are rejected — only relative repo paths are allowed.")]
	[TestCase("not-docs/data-grid.component.md", Description = "Must start with the docs/, mobile-docs/ or request-docs/ namespace; an arbitrary '-docs/' prefix is rejected.")]
	[TestCase("web-docs/data-grid.component.md", Description = "Only the mobile- and request- prefixes are allowed on the docs namespace; other prefixes are rejected.")]
	[TestCase("requests-docs/close-page.request.md", Description = "Near-miss namespace (requests-docs) is rejected — only the exact request-docs/ prefix is allowed.")]
	[TestCase("request-docs/../docs/escape.md", Description = "Embedded ../ in the request-docs namespace is blocked.")]
	[TestCase("docs/data-grid.component", Description = "Must end with .md (extension is part of the contract).")]
	[TestCase("docs/data-grid component.md", Description = "Spaces are not part of the allowed character class.")]
	[TestCase("docs/", Description = "Empty filename after the namespace is rejected.")]
	[TestCase("docs/.md", Description = "Empty stem is rejected.")]
	[TestCase("", Description = "Empty string is rejected.")]
	[TestCase(null, Description = "Null is rejected without throwing.")]
	public void TryNormalise_Rejects_Malformed_Or_Hostile_Paths(string? input) {
		bool ok = ComponentRegistryDocsPath.TryNormalise(input, out _);

		ok.Should().BeFalse(because: "the producer contract forbids this path shape");
	}

	[Test]
	[Description("Surrounding whitespace is trimmed before validation so a slightly dirty payload still passes.")]
	public void TryNormalise_Trims_Surrounding_Whitespace() {
		bool ok = ComponentRegistryDocsPath.TryNormalise("  docs/data-grid.component.md  ", out string normalised);

		ok.Should().BeTrue();
		normalised.Should().Be("docs/data-grid.component.md");
	}
}
