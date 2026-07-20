using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Drift guard for the SHIPPED static agent guidance (the workspace/ui-project AGENTS.md templates that
/// <c>clio createw</c> stamps verbatim into every user/partner repo, plus the MCP server instructions).
/// The PR #743 lazy-schema split removed the long tail from <c>tools/list</c>, so a template that tells
/// an agent to call a long-tail tool BY NAME sends it into a dead end. The oracle is therefore
/// <b>resident-or-bridged</b>, NOT mere existence: an MCP tool named imperatively in shipped static
/// guidance must either be resident (advertised in <c>tools/list</c>) or the same line must route the
/// call through the discovery bridge (<c>clio-run</c> / <c>get-tool-contract</c> / <c>get-guidance</c>).
/// A naive "the name resolves in the registry" check would stay green on exactly the regression this
/// guards against, because the registry deliberately contains the full long tail.
/// </summary>
/// <remarks>
/// Tokenization rules (deliberately explicit so the oracle is deterministic):
/// <list type="bullet">
/// <item>Only inline-backticked kebab-case tokens (<c>`get-fsm-mode`</c>) are candidate references;
/// fenced code blocks, multi-word backticks (<c>`clio createw`</c> — a terminal command), option flags
/// (<c>`--force`</c>), paths, and camelCase identifiers never match the pattern.</item>
/// <item>A line carrying an explicit negation ("do not", "don't", "never") is a mention, not an
/// imperative, and is skipped.</item>
/// <item>A token that IS an MCP tool name (full reflection catalog or a compatibility-catalog alias)
/// is classified as an MCP reference even when a CLI verb of the same name exists — that precedence is
/// what catches the #743 regression set.</item>
/// <item>A non-MCP token that is a current CLI <c>[Verb]</c> name/alias is a terminal-command reference
/// and is allowed.</item>
/// <item>Anything else must appear in the explicit external allowlist, otherwise it fails as an
/// unresolvable reference (catches typos and future renames).</item>
/// </list>
/// Enabled guidance-article bodies are deliberately OUT of scope: they are the live channel, already
/// guarded by McpGuidanceForcingTests, and legitimately name long-tail tools for clio-run dispatch.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class WorkspaceTemplateGuidanceDriftTests {

	// Inline-backticked kebab-case token: lowercase start, at least one hyphen-separated segment.
	private static readonly Regex BacktickedKebabToken = new(
		@"`([a-z][a-z0-9]*(?:-[a-z0-9]+)+)`",
		RegexOptions.Compiled);

	// `do\W{1,4}not` tolerates markdown emphasis between the words ("Do **NOT** use").
	private static readonly Regex NegationMarker = new(
		@"\bdo\W{1,4}not\b|\bdon't\b|\bnever\b",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	// The discovery bridge: a long-tail MCP name on the same line as any of these is routed through the
	// advertised surface and is therefore valid.
	private static readonly string[] BridgeMarkers = ["clio-run", "get-tool-contract", "get-guidance"];

	// Non-tool kebab tokens the shipped templates legitimately use (build configurations, external
	// tooling concepts). Grow deliberately — an addition here must be reviewed as "definitely not a
	// tool reference".
	private static readonly HashSet<string> ExternalAllowlist = new(StringComparer.OrdinalIgnoreCase) {
		"dev-n8",
		"dev-nf",
		"net-framework",
		"net-core",
		"kebab-case",
		"error-as-value"
	};

	private static readonly Lazy<HashSet<string>> CliVerbNames = new(() => {
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		foreach (Type type in typeof(Clio.Program).Assembly.GetTypes()) {
			VerbAttribute verb = type.GetCustomAttribute<VerbAttribute>();
			if (verb is null || string.IsNullOrWhiteSpace(verb.Name)) {
				continue;
			}
			names.Add(verb.Name);
			foreach (string alias in verb.Aliases ?? []) {
				if (!string.IsNullOrWhiteSpace(alias)) {
					names.Add(alias);
				}
			}
		}
		return names;
	});

	private static readonly Lazy<HashSet<string>> McpToolNames = new(() =>
		new HashSet<string>(McpToolSchemaCatalog.RegisteredToolNames, StringComparer.OrdinalIgnoreCase));

	private static readonly Lazy<HashSet<string>> AliasNames = new(() => {
		HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);
		foreach (IReadOnlyList<string> aliasList in McpToolCompatibilityCatalog.SeedAliasesByCanonical.Values) {
			foreach (string alias in aliasList) {
				aliases.Add(alias);
			}
		}
		return aliases;
	});

	private static string TemplatePath(params string[] parts) =>
		Path.Combine([AppContext.BaseDirectory, "tpl", .. parts]);

	/// <summary>
	/// Scans one guidance text and returns every resident-or-bridged violation, formatted as
	/// "&lt;source&gt;: '&lt;token&gt;' (&lt;reason&gt;) — &lt;offending line&gt;".
	/// </summary>
	private static List<string> ScanGuidance(string sourceName, string text) {
		List<string> violations = [];
		bool insideFence = false;
		foreach (string rawLine in text.Split('\n')) {
			string line = rawLine.TrimEnd();
			if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) {
				insideFence = !insideFence;
				continue;
			}
			if (insideFence || NegationMarker.IsMatch(line)) {
				continue;
			}
			bool lineIsBridged = BridgeMarkers.Any(marker =>
				line.Contains(marker, StringComparison.OrdinalIgnoreCase));
			foreach (Match match in BacktickedKebabToken.Matches(line)) {
				string token = match.Groups[1].Value;
				string violation = ClassifyToken(token, lineIsBridged);
				if (violation is not null) {
					violations.Add($"{sourceName}: '{token}' ({violation}) — {line.Trim()}");
				}
			}
		}
		return violations;
	}

	// Returns null when the token is valid, otherwise the reason it violates the oracle.
	private static string ClassifyToken(string token, bool lineIsBridged) {
		// Bridge markers themselves are always valid references.
		if (BridgeMarkers.Contains(token, StringComparer.OrdinalIgnoreCase)) {
			return null;
		}
		bool isMcpName = McpToolNames.Value.Contains(token) || AliasNames.Value.Contains(token);
		if (isMcpName) {
			if (McpCoreToolProfile.IsResident(token) || lineIsBridged) {
				return null;
			}
			return "non-resident MCP tool named imperatively without the clio-run/get-tool-contract bridge";
		}
		if (CliVerbNames.Value.Contains(token)) {
			return null;
		}
		if (ExternalAllowlist.Contains(token)) {
			return null;
		}
		return "unresolvable reference — not an MCP tool, alias, CLI verb, or allowlisted external token";
	}

	[Test]
	[Category("Unit")]
	[Description("Every imperative tool reference in the SHIPPED workspace/ui-project AGENTS.md templates is resident or explicitly bridged through clio-run/get-tool-contract, so a freshly created workspace never steers an agent into an invocation dead end.")]
	public void ShippedTemplates_ShouldOnlyReferenceResidentOrBridgedTools_WhenNamingToolsImperatively() {
		// Arrange
		(string Name, string Path)[] templates = [
			("tpl/workspace/AGENTS.md", TemplatePath("workspace", "AGENTS.md")),
			("tpl/ui-project/AGENTS.md", TemplatePath("ui-project", "AGENTS.md")),
			("tpl/ui-project-Empty/AGENTS.md", TemplatePath("ui-project-Empty", "AGENTS.md"))
		];

		// Act
		List<string> violations = [];
		foreach ((string name, string path) in templates) {
			File.Exists(path).Should().BeTrue(
				because: $"the shipped template {name} must be present in the build output (csproj copies tpl/**)");
			violations.AddRange(ScanGuidance(name, File.ReadAllText(path)));
		}

		// Assert
		violations.Should().BeEmpty(
			because: "shipped static guidance is frozen in every user/partner repo; a long-tail tool named " +
				"imperatively without the discovery bridge dead-ends the agent (the PR #743 regression)");
	}

	[Test]
	[Category("Unit")]
	[Description("The MCP server instructions reference only resident-or-bridged tools, keeping the initialize-time guidance aligned with the advertised surface.")]
	public void McpServerInstructions_ShouldOnlyReferenceResidentOrBridgedTools_WhenNamingToolsImperatively() {
		// Arrange & Act
		List<string> violations = ScanGuidance("McpServerInstructions", McpServerInstructions.Text);

		// Assert
		violations.Should().BeEmpty(
			because: "the initialize instructions are the first guidance every agent reads and must never " +
				"point at an unreachable tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Shipped template text files carry no UTF-8 BOM, so downstream parsers (strict JSON readers, import directives) never trip over an invisible prefix.")]
	public void ShippedTemplates_ShouldHaveNoUtf8Bom_InTextFiles() {
		// Arrange
		string tplRoot = Path.Combine(AppContext.BaseDirectory, "tpl");
		string[] textExtensions = [".md", ".json", ".slnx", ".yml", ".txt", ".toml", ".gitignore"];
		byte[] bom = [0xEF, 0xBB, 0xBF];

		// Act
		List<string> bomFiles = Directory.EnumerateFiles(tplRoot, "*", SearchOption.AllDirectories)
			.Where(path => textExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
				|| Path.GetFileName(path).Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
			.Where(path => {
				using FileStream stream = File.OpenRead(path);
				byte[] head = new byte[3];
				return stream.Read(head, 0, 3) == 3 && head.SequenceEqual(bom);
			})
			.Select(path => Path.GetRelativePath(tplRoot, path))
			.ToList();

		// Assert
		bomFiles.Should().BeEmpty(
			because: "template files are stamped verbatim into user repos and must not carry a UTF-8 BOM");
	}

	[Test]
	[Category("Unit")]
	[Description("The oracle itself fails a bogus imperative reference (self-check: the scanner detects an unknown token).")]
	public void ScanGuidance_ShouldFlagBogusToken_WhenGuidanceNamesNonexistentTool() {
		// Arrange
		const string guidance = "Call `zz-not-a-real-tool` to deploy your changes.";

		// Act
		List<string> violations = ScanGuidance("fixture", guidance);

		// Assert
		violations.Should().ContainSingle(
			because: "an unresolvable token must fail the scan rather than pass silently")
			.Which.Should().Contain("zz-not-a-real-tool");
	}

	[Test]
	[Category("Unit")]
	[Description("The oracle fails a non-resident MCP tool named imperatively WITHOUT the bridge, and passes the same tool when the line routes through clio-run — the exact PR #743 regression signature.")]
	public void ScanGuidance_ShouldFlagUnbridgedLongTail_AndAcceptBridgedLongTail() {
		// Arrange — sync-schemas is a real long-tail (non-resident) MCP tool.
		const string unbridged = "Call `sync-schemas` after changing entity schemas.";
		const string bridged = "Run `sync-schemas` via clio-run after changing entity schemas.";

		// Act
		List<string> unbridgedViolations = ScanGuidance("fixture", unbridged);
		List<string> bridgedViolations = ScanGuidance("fixture", bridged);

		// Assert
		unbridgedViolations.Should().ContainSingle(
			because: "an unbridged long-tail imperative is exactly the dead end the oracle exists to catch")
			.Which.Should().Contain("sync-schemas");
		bridgedViolations.Should().BeEmpty(
			because: "the same tool routed through clio-run on the same line is a valid reference");
	}

	[Test]
	[Category("Unit")]
	[Description("The oracle accepts a resident tool named imperatively, skips negated mentions, and skips fenced code blocks — pinning the tokenization rules.")]
	public void ScanGuidance_ShouldAcceptResident_SkipNegations_AndSkipFencedBlocks() {
		// Arrange — get-guidance is resident; push-workspace is long-tail.
		const string guidance = """
			Call `get-guidance` first for every operation.
			Do NOT use `push-workspace` in file-system mode.
			```bash
			clio push-workspace -e dev
			```
			""";

		// Act
		List<string> violations = ScanGuidance("fixture", guidance);

		// Assert
		violations.Should().BeEmpty(
			because: "resident imperatives are valid, negated mentions are not imperatives, and fenced " +
				"code blocks are terminal examples outside the oracle's scope");
	}
}
