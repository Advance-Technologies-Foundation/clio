using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using CommandLine;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
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

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	// Inline-backticked kebab-case token: lowercase start, at least one hyphen-separated segment.
	private static readonly Regex BacktickedKebabToken = new(
		@"`([a-z][a-z0-9]*(?:-[a-z0-9]+)+)`",
		RegexOptions.Compiled, RegexTimeout);

	// Prose that tells a user to enable a feature flag: `clio experimental --name <key> --enable`.
	private static readonly Regex ExperimentalFeatureKeyReference = new(
		@"experimental\s+--name\s+([a-z][a-z0-9]*(?:-[a-z0-9]+)*)",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static readonly Regex GuidanceNameReference = new(
		@"name=([a-z][a-z0-9]*(?:-[a-z0-9]+)*)",
		RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

	// `do\W{1,4}not` tolerates markdown emphasis between the words ("Do **NOT** use").
	private static readonly Regex NegationMarker = new(
		@"\bdo\W{1,4}not\b|\bdon't\b|\bnever\b",
		RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

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
	[Description("Every get-guidance article named in a SHIPPED template resolves against the curated knowledge catalog.")]
	public void ShippedTemplates_ShouldReferenceRegisteredGuidance_WhenNamingGuidanceArticles() {
		// Arrange
		(string Name, string Path)[] templates = [
			("tpl/workspace/AGENTS.md", TemplatePath("workspace", "AGENTS.md")),
			("tpl/ui-project/AGENTS.md", TemplatePath("ui-project", "AGENTS.md")),
			("tpl/ui-project-Empty/AGENTS.md", TemplatePath("ui-project-Empty", "AGENTS.md"))
		];
		IReadOnlySet<string> curatedNames = CuratedKnowledgeNames("availableNames");
		IReadOnlySet<string> featureGatedNames = CuratedKnowledgeNames("featureGatedNames");

		// Act
		Dictionary<string, HashSet<string>> referencesByTemplate = new(StringComparer.OrdinalIgnoreCase);
		List<string> unresolved = [];
		foreach ((string name, string path) in templates) {
			string text = File.ReadAllText(path);
			HashSet<string> references = new(StringComparer.OrdinalIgnoreCase);
			referencesByTemplate[name] = references;
			foreach (string line in text.Split('\n')) {
				if (!line.Contains("get-guidance", StringComparison.OrdinalIgnoreCase)) {
					continue;
				}
				foreach (Match match in GuidanceNameReference.Matches(line)) {
					string guidanceName = match.Groups[1].Value;
					references.Add(guidanceName);
					if (featureGatedNames.Contains(guidanceName)) {
						unresolved.Add($"{name}: '{guidanceName}' (feature-gated)");
					} else if (!curatedNames.Contains(guidanceName)) {
						unresolved.Add($"{name}: '{guidanceName}'");
					}
				}
			}
		}

		// Assert
		referencesByTemplate["tpl/workspace/AGENTS.md"].Should().Contain(
			["core-rules", "routing", "configuration-webservice", "configuration-webservice-tests"],
			because: "the workspace template must retain its mandatory core/routing guidance and route " +
				"configuration web-service implementation and tests to their canonical live articles");
		unresolved.Should().BeEmpty(
			because: "shipped templates are frozen into user workspaces, so every guidance name they use must " +
				"resolve in the curated knowledge catalog with the default feature-toggle state — a name that " +
				"is feature-gated dead-ends the agent on every environment where the feature is off");
	}

	[Test]
	[Category("Unit")]
	[Description("The curated guidance fixture contains unique, non-overlapping default and feature-gated names.")]
	public void CuratedKnowledgeFixture_ShouldContainUniqueNonOverlappingNames_WhenLoaded() {
		// Arrange
		using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(CuratedKnowledgeFixturePath()));

		// Act
		string[] availableNames = document.RootElement.GetProperty("availableNames")
			.EnumerateArray()
			.Select(name => name.GetString()!)
			.ToArray();
		string[] featureGatedNames = document.RootElement.GetProperty("featureGatedNames")
			.EnumerateArray()
			.Select(name => name.GetString()!)
			.ToArray();

		// Assert
		availableNames.Should().OnlyHaveUniqueItems(name => name.ToUpperInvariant(),
			because: "duplicate catalog names make a hand-merged fixture look complete after it is converted to a set");
		featureGatedNames.Should().OnlyHaveUniqueItems(name => name.ToUpperInvariant(),
			because: "each feature-gated guidance name must have one canonical fixture entry");
		availableNames.Intersect(featureGatedNames, StringComparer.OrdinalIgnoreCase).Should().BeEmpty(
			because: "one guidance name cannot be both available by default and feature-gated");
		availableNames.Should().BeInAscendingOrder(StringComparer.Ordinal,
			because: "the fixture must preserve the deterministic order emitted by the guidance resolver");
		featureGatedNames.Should().BeInAscendingOrder(StringComparer.Ordinal,
			because: "feature-gated names must use the same deterministic order as default names");
	}

	[Test]
	[Category("Unit")]
	[Description("The curated guidance fixture version and sequence identify the same published generation.")]
	public void CuratedKnowledgeFixture_ShouldHaveConsistentGeneration_WhenLoaded() {
		// Arrange
		using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(CuratedKnowledgeFixturePath()));

		// Act
		string libraryVersion = document.RootElement.GetProperty("libraryVersion").GetString()!;
		bool hasValidVersionFormat = Regex.IsMatch(
			libraryVersion,
			"^[0-9]{1,7}(?:\\.[0-9]{1,3}){0,3}$",
			RegexOptions.CultureInvariant);
		string[] versionComponents = libraryVersion.Split('.');
		ulong sequence = document.RootElement.GetProperty("sequence").GetUInt64();
		ulong expectedSequence = Enumerable.Range(0, 4).Aggregate(
			0UL,
			(current, index) => (current * 1_000UL) + (index < versionComponents.Length
				? ulong.Parse(versionComponents[index], CultureInfo.InvariantCulture)
				: 0UL));

		// Assert
		hasValidVersionFormat.Should().BeTrue(
			because: "the fixture version must satisfy the publisher's sequence derivation contract");
		versionComponents.Length.Should().BeInRange(1, 4,
			because: "the publisher accepts one to four version components for its four sequence slots");
		expectedSequence.Should().BeGreaterThan(0UL,
			because: "the publisher rejects a version that derives the reserved zero sequence");
		sequence.Should().Be(expectedSequence,
			because: "the fixture must identify one internally consistent curated-library generation");
	}

	/// <summary>
	/// Guidance names the curated knowledge library publishes, read from the named fixture array:
	/// <c>availableNames</c> resolve with the default feature-toggle state, <c>featureGatedNames</c>
	/// resolve only where their <c>requiredFeatures</c> are enabled.
	/// </summary>
	/// <remarks>
	/// Each array holds item IDs and topic IDs for guidance-role articles only —
	/// <see cref="Clio.Command.McpServer.Knowledge.KnowledgeResolver"/> returns only those values from
	/// <c>GetNames</c>. Legacy aliases resolve only as complete <c>docs://</c> URIs and are not bare
	/// guidance names; reference articles are likewise reachable by URI only.
	/// Guidance content lives in clio-knowledge, so this fixture — not a compiled catalog — is what a
	/// unit test can check shipped templates against without network access. Regenerate it from that
	/// repository's <c>bundle-source.json</c> whenever the curated library publishes a new generation.
	/// The fixture's own <c>libraryVersion</c> and <c>sequence</c> fields record the checked generation.
	/// A generation that only edits article
	/// bodies leaves the name arrays untouched; refresh the recorded version and sequence anyway, so
	/// the fixture states which generation it was checked against.
	/// </remarks>
	private static IReadOnlySet<string> CuratedKnowledgeNames(string arrayProperty) {
		using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(CuratedKnowledgeFixturePath()));
		return document.RootElement.GetProperty(arrayProperty)
			.EnumerateArray()
			.Select(name => name.GetString()!)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}

	private static string CuratedKnowledgeFixturePath() {
		return Path.Combine(
			TestContext.CurrentContext.TestDirectory,
			"Command", "McpServer", "Fixtures", "curated-knowledge-names.json");
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
	/// <summary>
	/// Collects every feature key the SHIPPED templates name, from the two shapes a template can carry
	/// one in: a JSON <c>features</c> node's <c>examples</c> entries, and prose telling a user to run
	/// <c>clio experimental --name &lt;key&gt;</c>. Keyed by feature name, valued by where it was found.
	/// </summary>
	private static Dictionary<string, string> FeatureKeysNamedByShippedTemplates() {
		Dictionary<string, string> keysBySource = new(StringComparer.OrdinalIgnoreCase);
		string templateRoot = Path.Combine(AppContext.BaseDirectory, "tpl");
		foreach (string file in Directory.EnumerateFiles(templateRoot, "*", SearchOption.AllDirectories)) {
			string text;
			try {
				text = File.ReadAllText(file);
			} catch (IOException) {
				continue;
			}
			string relativePath = Path.GetRelativePath(templateRoot, file).Replace('\\', '/');
			foreach (Match match in ExperimentalFeatureKeyReference.Matches(text)) {
				keysBySource[match.Groups[1].Value] = "tpl/" + relativePath + " (experimental --name)";
			}
			// A template that is not JSON simply contributes no example keys; never fail the scan on it.
			try {
				using JsonDocument document = JsonDocument.Parse(text);
				foreach (string key in CollectFeatureExampleKeys(document.RootElement)) {
					keysBySource[key] = "tpl/" + relativePath + " (features examples)";
				}
			} catch (JsonException) {
			}
		}
		return keysBySource;
	}

	/// <summary>
	/// Walks a template's JSON for any <c>features</c> node carrying an <c>examples</c> array and returns
	/// the property names of those example objects - the feature keys the template advertises.
	/// </summary>
	private static IEnumerable<string> CollectFeatureExampleKeys(JsonElement element) {
		if (element.ValueKind == JsonValueKind.Object) {
			foreach (JsonProperty property in element.EnumerateObject()) {
				if (string.Equals(property.Name, "features", StringComparison.OrdinalIgnoreCase)
					&& property.Value.ValueKind == JsonValueKind.Object
					&& property.Value.TryGetProperty("examples", out JsonElement examples)
					&& examples.ValueKind == JsonValueKind.Array) {
					foreach (JsonElement example in examples.EnumerateArray()) {
						if (example.ValueKind != JsonValueKind.Object) {
							continue;
						}
						foreach (JsonProperty exampleKey in example.EnumerateObject()) {
							yield return exampleKey.Name;
						}
					}
				}
				foreach (string nested in CollectFeatureExampleKeys(property.Value)) {
					yield return nested;
				}
			}
		} else if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				foreach (string nested in CollectFeatureExampleKeys(item)) {
					yield return nested;
				}
			}
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Every feature key named by a shipped template still exists as a live [FeatureToggle] in the product, so retiring a flag cannot leave a template telling users to enable something that is gone.")]
	public void ShippedTemplates_ShouldNameOnlyLiveFeatureKeys_WhenAdvertisingFeatureToggles() {
		// Arrange
		HashSet<string> liveFeatureKeys = typeof(McpCoreToolProfile).Assembly.GetTypes()
			.Select(type => type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false))
			.Where(attribute => attribute is not null)
			.Select(attribute => attribute!.FeatureName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		// Act
		Dictionary<string, string> namedKeys = FeatureKeysNamedByShippedTemplates();
		string[] retiredKeys = namedKeys
			.Where(pair => !liveFeatureKeys.Contains(pair.Key))
			.Select(pair => pair.Value + ": '" + pair.Key + "'")
			.OrderBy(entry => entry, StringComparer.Ordinal)
			.ToArray();

		// Assert
		liveFeatureKeys.Should().NotBeEmpty(
			because: "the product still gates several features, so an empty live set would mean the "
				+ "reflection lookup broke and the assertion below became vacuous");
		namedKeys.Should().NotBeEmpty(
			because: "at least one shipped template advertises the features map by example, and a scan "
				+ "that extracts nothing would stay green while the templates drifted freely");
		retiredKeys.Should().BeEmpty(
			because: "a template is stamped verbatim into every user repo, so a feature key it names must "
				+ "still gate something. This is the ENG-96132 go-live regression made mechanical: the "
				+ "features example named 'process-designer' until that flag was retired, and nothing but "
				+ "review would have caught a template still telling users to enable a flag the product "
				+ "no longer has");
	}

	[Test]
	[Category("Unit")]
	[Description("An MCP tool that ships without a [FeatureToggle] must not name a feature-gated guidance article as mandatory reading, so a tool go-live cannot outrun the guidance library that documents it.")]
	public void UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead() {
		// Arrange
		IReadOnlySet<string> featureGatedNames = CuratedKnowledgeNames("featureGatedNames");
		IReadOnlySet<string> availableNames = CuratedKnowledgeNames("availableNames");
		Type[] ungatedToolTypes = McpFeatureToggleFilter
			.GetAttributedTypes(typeof(McpCoreToolProfile).Assembly, typeof(McpServerToolTypeAttribute))
			.Where(type => type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false) is null)
			.ToArray();

		// Act
		List<string> violations = [];
		List<string> resolvedReferences = [];
		foreach (Type toolType in ungatedToolTypes) {
			foreach (MethodInfo method in toolType.GetMethods()) {
				string description = method
					.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(inherit: false)?.Description;
				if (string.IsNullOrWhiteSpace(description)) {
					continue;
				}
				foreach (Match match in GuidanceNameReference.Matches(description)) {
					string guidanceName = match.Groups[1].Value;
					if (featureGatedNames.Contains(guidanceName)) {
						violations.Add(toolType.Name + "." + method.Name + " -> get-guidance name=" + guidanceName);
					} else if (availableNames.Contains(guidanceName)) {
						resolvedReferences.Add(toolType.Name + " -> " + guidanceName);
					}
				}
			}
		}

		// Assert
		resolvedReferences.Should().NotBeEmpty(
			because: "at least one un-gated tool description points at a curated guide (create-business-process "
				+ "names process-modeling), so an empty resolved set would mean the scan stopped matching and "
				+ "the violation check below became vacuous");
		violations.Should().BeEmpty(
			because: "the tool gate and the guidance-article gate key off the same feature name but ship in "
				+ "two independently released artifacts - clio and the clio-knowledge library. Un-gating a "
				+ "tool while its mandatory guide stays gated is the out-of-order publish this pins "
				+ "(ENG-96132): the tool would be reachable by default while get-guidance reports its guide "
				+ "as unavailable, indistinguishable from never published. This fails CI instead, because "
				+ "the pinned fixture only moves to a generation the library actually published");
	}
}
