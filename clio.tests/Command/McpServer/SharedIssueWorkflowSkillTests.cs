using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Verifies that Claude and Codex use portable redirects to the shared issue-workflow skills.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class SharedIssueWorkflowSkillTests {
	private static readonly string RepositoryRoot = Path.GetFullPath(
		Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	[Test]
	[Category("Unit")]
	[TestCase("clio-issue-workflow")]
	[TestCase("claim-clio-issue")]
	[TestCase("investigate-clio-issue")]
	[TestCase("repair-clio-issue")]
	[Description("Keeps one canonical issue-workflow body under .ai while both agent wrappers resolve it by the same relative path.")]
	public void SharedSkill_ShouldRedirectBothAgentsToOneCanonicalBody_WhenRepositoryIsCheckedOut(
		string skillName) {
		// Arrange
		string canonicalPath = Path.Combine(RepositoryRoot, ".ai", "skills", skillName, "SKILL.md");
		string claudeDirectory = Path.Combine(RepositoryRoot, ".claude", "skills", skillName);
		string codexDirectory = Path.Combine(RepositoryRoot, ".codex", "skills", skillName);

		// Act
		string canonical = File.ReadAllText(canonicalPath).ReplaceLineEndings("\n");
		string claudeWrapper = File.ReadAllText(
			Path.Combine(claudeDirectory, "SKILL.md")).ReplaceLineEndings("\n");
		string codexWrapper = File.ReadAllText(
			Path.Combine(codexDirectory, "SKILL.md")).ReplaceLineEndings("\n");
		string canonicalFrontmatter = canonical.Split(
			"\n---\n", StringSplitOptions.None)[0] + "\n---\n";
		Match claudeRedirect = Regex.Match(
			claudeWrapper, "Read `(?<path>[^`]+)` completely", RegexOptions.None, RegexTimeout);
		Match codexRedirect = Regex.Match(
			codexWrapper, "Read `(?<path>[^`]+)` completely", RegexOptions.None, RegexTimeout);
		string claudeResolvedTarget = Path.GetFullPath(
			Path.Combine(claudeDirectory, claudeRedirect.Groups["path"].Value));
		string codexResolvedTarget = Path.GetFullPath(
			Path.Combine(codexDirectory, codexRedirect.Groups["path"].Value));

		// Assert
		canonical.Should().Contain($"name: {skillName}",
			because: "the canonical body must carry the trigger metadata that both wrappers reuse");
		claudeWrapper.Should().Be(codexWrapper,
			because: "Claude and Codex must share one redirect contract instead of drifting copies");
		codexWrapper.Should().StartWith(canonicalFrontmatter,
			because: "native discovery on each agent needs the canonical name and description");
		codexWrapper.Should().NotContain("\n## ",
			because: "a wrapper that reproduces workflow sections would duplicate the canonical body");
		claudeRedirect.Success.Should().BeTrue(
			because: "the Claude wrapper must declare which canonical skill to read completely");
		codexRedirect.Success.Should().BeTrue(
			because: "the Codex wrapper must declare which canonical skill to read completely");
		claudeResolvedTarget.Should().Be(canonicalPath,
			because: "the redirect declared by the Claude wrapper must resolve to the canonical body");
		codexResolvedTarget.Should().Be(canonicalPath,
			because: "the redirect declared by the Codex wrapper must resolve to the canonical body");
	}
}
