using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Drift guard for <c>docs/McpCapabilityMap.md</c>, which restates each MCP tool's contract BY HAND. Nothing
/// pinned it, so it silently started lying whenever a tool grew a configuration block: the entry kept
/// describing the blocks it knew and simply never mentioned the new one, which reads as "this tool does not
/// return that" rather than as a stale document.
/// </summary>
/// <remarks>
/// <para>The oracle is deliberately narrow, because the map is prose and a prose-vs-code diff has no
/// meaningful pass condition. It checks ONE mechanical relation: a configuration BLOCK a tool's own
/// <c>[Description]</c> names must appear in that tool's entry. Blocks are what agents branch on and what the
/// map exists to enumerate, and a missing one is the failure this file was written after.</para>
/// <para>What it deliberately does NOT check: whether the prose is accurate, whether every tool has an entry
/// at all (the map documents a curated set), or wording of any kind. A tool with no entry is skipped rather
/// than failed — adding entries for the whole long tail is a documentation decision, not a drift.</para>
/// <para>Only camelCase block names are candidates (<c>readData</c>, <c>changeData</c>, <c>openEditPage</c>),
/// because a single lowercase word cannot be told apart from ordinary prose: <c>email</c> reads the same as a
/// noun and as a block name, and demanding it would fail on sentences that merely use the word. That is a
/// deliberate hole and worth knowing — verified while proving the guard red, where removing <c>email</c> from
/// an entry stayed green and removing <c>readData</c> failed with the expected message.</para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class McpCapabilityMapDriftTests {

	#region Fields: Private

	// Words that precede "block" as ordinary English rather than as a block NAME. Without this the oracle
	// would demand entries for "the same block", "that block" and friends.
	private static readonly HashSet<string> NonBlockWords = new(StringComparer.OrdinalIgnoreCase) {
		"same", "that", "this", "whole", "entire", "one", "its", "the", "a", "an", "each", "every", "another",
		"email's", "element's", "code", "text", "try", "catch", "using", "per"
	};

	// A block token as the descriptions spell them: camelCase, at least two words glued, e.g. openEditPage.
	private static readonly Regex BlockReference =
		new(@"\b([a-z][a-z0-9]*(?:[A-Z][A-Za-z0-9]*)+)\s+block\b", RegexOptions.Compiled);

	#endregion

	#region Methods: Private

	private static string RepositoryRoot() =>
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	private static string CapabilityMap() {
		string path = Path.Combine(RepositoryRoot(), "docs", "McpCapabilityMap.md");
		File.Exists(path).Should().BeTrue(
			because: $"the capability map is the published contract and must be readable at {path}");
		return File.ReadAllText(path);
	}

	/// <summary>
	/// The map entry for one tool: from its <c>- `tool-name`</c> bullet to the next top-level bullet. Null when
	/// the map does not document the tool, which is a curation decision rather than drift.
	/// </summary>
	private static string EntryFor(string capabilityMap, string toolName) {
		int start = capabilityMap.IndexOf($"- `{toolName}`", StringComparison.Ordinal);
		if (start < 0) {
			return null;
		}
		int end = capabilityMap.IndexOf("\n- `", start + 1, StringComparison.Ordinal);
		return end > start ? capabilityMap[start..end] : capabilityMap[start..];
	}

	private static IReadOnlyList<(string Name, string Description)> AdvertisedTools() {
		var tools = new List<(string, string)>();
		foreach (Type type in typeof(Clio.Program).Assembly.GetTypes()) {
			if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null) {
				continue;
			}
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance
					| BindingFlags.Static | BindingFlags.DeclaredOnly)) {
				McpServerToolAttribute tool = method.GetCustomAttribute<McpServerToolAttribute>();
				string description = method
					.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
				if (tool?.Name is null || string.IsNullOrWhiteSpace(description)) {
					continue;
				}
				tools.Add((tool.Name, description));
			}
		}
		return tools;
	}

	private static IReadOnlySet<string> BlocksNamedIn(string description) =>
		BlockReference.Matches(description)
			.Select(match => match.Groups[1].Value)
			.Where(token => !NonBlockWords.Contains(token))
			.ToHashSet(StringComparer.Ordinal);

	#endregion

	#region Methods: Tests

	[Test]
	[Description("Every configuration block a tool's own description names is also named in that tool's capability-map entry, so a tool that grows a block cannot leave the map quietly claiming it does not have one.")]
	public void CapabilityMap_ShouldName_EveryBlockTheToolDescriptionNames() {
		// Arrange
		string capabilityMap = CapabilityMap();
		var gaps = new List<string>();

		// Act
		foreach ((string name, string description) in AdvertisedTools()) {
			string entry = EntryFor(capabilityMap, name);
			if (entry is null) {
				continue;
			}
			foreach (string block in BlocksNamedIn(description)) {
				if (!entry.Contains(block, StringComparison.Ordinal)) {
					gaps.Add($"{name}: description names the '{block}' block, its map entry does not");
				}
			}
		}

		// Assert
		gaps.Should().BeEmpty(
			because: "the map is maintained by hand, so the only thing standing between it and a silent lie is "
				+ "this comparison: a block named in the shipped description and missing from the entry reads to "
				+ "an agent as a block the tool does not have. Gaps:\n" + string.Join("\n", gaps));
	}

	[Test]
	[Description("The oracle actually looks at something: at least one tool is documented in the map AND names a block, so a refactor that empties either side fails here instead of passing vacuously.")]
	public void CapabilityMapOracle_ShouldNotBeVacuous() {
		// Arrange
		string capabilityMap = CapabilityMap();

		// Act
		int covered = AdvertisedTools()
			.Count(tool => EntryFor(capabilityMap, tool.Name) is not null && BlocksNamedIn(tool.Description).Any());

		// Assert
		covered.Should().BeGreaterThan(0,
			because: "a guard that compares nothing is worse than no guard, because it reports success");
	}

	#endregion

}
