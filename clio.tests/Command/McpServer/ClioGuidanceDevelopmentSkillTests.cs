using System;
using FluentAssertions;
using NUnit.Framework;
using System.IO;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Verifies that Claude and Codex use portable redirects to one canonical clio guidance-development skill.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ClioGuidanceDevelopmentSkillTests {
	private static readonly string RepositoryRoot = Path.GetFullPath(
		Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	[Test]
	[Category("Unit")]
	[Description("Keeps Claude and Codex skill entry points portable while preserving one canonical guidance body under .ai.")]
	public void SkillEntryPoints_ShouldRedirectToCanonicalSkill_WhenRepositoryIsCheckedOut() {
		// Arrange
		string canonicalPath = Path.Combine(
			RepositoryRoot, ".ai", "skills", "clio-guidance-development", "SKILL.md");
		string claudePath = Path.Combine(
			RepositoryRoot, ".claude", "skills", "clio-guidance-development", "SKILL.md");
		string codexPath = Path.Combine(
			RepositoryRoot, ".codex", "skills", "clio-guidance-development", "SKILL.md");
		string canonicalMetadataPath = Path.Combine(
			RepositoryRoot, ".ai", "skills", "clio-guidance-development", "agents", "openai.yaml");
		string codexMetadataPath = Path.Combine(
			RepositoryRoot, ".codex", "skills", "clio-guidance-development", "agents", "openai.yaml");

		// Act
		string canonical = File.ReadAllText(canonicalPath).ReplaceLineEndings("\n");
		string claudeRedirect = File.ReadAllText(claudePath).ReplaceLineEndings("\n");
		string codexRedirect = File.ReadAllText(codexPath).ReplaceLineEndings("\n");
		string canonicalMetadata = File.ReadAllText(canonicalMetadataPath).ReplaceLineEndings("\n");
		string codexMetadata = File.ReadAllText(codexMetadataPath).ReplaceLineEndings("\n");
		string canonicalFrontmatter = canonical.Split("\n---\n", StringSplitOptions.None)[0] + "\n---\n";

		// Assert
		canonical.Should().Contain("## Run the evidence loop",
			because: "the complete reusable framework should live in the canonical .ai skill");
		claudeRedirect.Should().Be(codexRedirect,
			because: "Claude and Codex should use the same portable redirect contract");
		codexRedirect.Should().StartWith(canonicalFrontmatter,
			because: "agent-specific entry points must preserve the canonical skill trigger metadata");
		codexRedirect.Should().Contain("../../../.ai/skills/clio-guidance-development/SKILL.md",
			because: "each agent entry point should resolve the canonical skill with a relative path");
		codexRedirect.Should().NotContain("## Run the evidence loop",
			because: "agent-specific entry points must not duplicate canonical guidance rules");
		codexMetadata.Should().Be(canonicalMetadata,
			because: "Codex UI metadata should remain identical to the canonical skill metadata");
	}
}
