using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CommandLine;
using CommandLine.Text;

namespace Clio.Help;

internal enum RootHelpRenderMode {
	Runtime,
	Export
}

internal sealed record HelpSection(
	string Heading,
	string NormalizedHeading,
	IReadOnlyList<string> Lines);

internal sealed record HelpDocument(
	IReadOnlyList<HelpSection> Sections,
	bool IsAliasShim) {
	public static readonly HelpDocument Empty = new([], false);
}

internal sealed class CommandHelpRenderer {
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);
	private static readonly Regex SectionHeadingRegex = new("^[A-Z][A-Z0-9 /&-]+:?$", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex DotQualifiedNameRegex = new(@"^[A-Za-z]+(\.[A-Za-z]+)+$", RegexOptions.Compiled, RegexTimeout);

	private const string SecUsage = "USAGE";
	private const string SecDescription = "DESCRIPTION";
	private const string SecExamples = "EXAMPLES";
	private const string SecRequirements = "REQUIREMENTS";
	private const string SecNotes = "NOTES";
	private const string SecSeeAlso = "SEE ALSO";
	private readonly IFileSystem _fileSystem;
	private readonly CommandHelpCatalog _catalog;
	private readonly Func<bool> _supportsAnsi;

	public CommandHelpRenderer(IFileSystem fileSystem, CommandHelpCatalog catalog)
		: this(fileSystem, catalog, SupportsAnsiEscapeCodes) {
	}

	internal CommandHelpRenderer(IFileSystem fileSystem, CommandHelpCatalog catalog, Func<bool> supportsAnsi) {
		_fileSystem = fileSystem;
		_catalog = catalog;
		_supportsAnsi = supportsAnsi;
	}

	public string RenderRootHelp(RootHelpRenderMode mode) {
		int leftWidth = Math.Max(32, _catalog.GetVisibleCommands().Max(command => command.CanonicalName.Length) + 2);
		StringBuilder builder = new();
		builder.AppendLine("clio - Creatio CLI");
		builder.AppendLine();
		builder.AppendLine("Usage:");
		builder.AppendLine("  clio <command> [arguments] [options]");
		builder.AppendLine();
		builder.AppendLine("Commands:");
		foreach (HelpCommandMetadata command in _catalog.GetVisibleCommands().OrderBy(command => command.CanonicalName, StringComparer.OrdinalIgnoreCase)) {
			AppendRootHelpEntry(builder, command, leftWidth, mode, mode == RootHelpRenderMode.Runtime && _supportsAnsi());
		}
		builder.AppendLine();
		builder.AppendLine("Run `clio <command> --help` for command details.");
		builder.AppendLine("See `clio/Commands.md` for the grouped reference.");
		return builder.ToString();
	}

	public string TryRenderCommandHelp(string commandName) {
		if (!_catalog.TryGetCommand(commandName, out HelpCommandMetadata command)) {
			return null;
		}
		HelpDocument source = LoadHelpDocument(command);
		if (!source.IsAliasShim && source.Sections.Count > 0) {
			return RenderCommandHelp(command, source);
		}
		return RenderGeneratedCommandHelp(command);
	}

	public string RenderMarkdownDoc(HelpCommandMetadata command) {
		HelpDocument source = LoadHelpDocument(command);
		StringBuilder builder = new();
		builder.AppendLine($"# {command.CanonicalName}");
		builder.AppendLine();
		builder.AppendLine(command.ShortDescription + ".");
		builder.AppendLine();
		WriteMarkdownSection(builder, "Usage", GetUsageLines(command, source), asCodeBlock: true);
		WriteMarkdownRawSection(builder, "Description", GetDescriptionLines(command, source));
		if (command.Aliases.Count > 0) {
			builder.AppendLine();
			builder.AppendLine("## Aliases");
			builder.AppendLine();
			builder.AppendLine(string.Join(", ", command.Aliases.Select(alias => $"`{alias}`")));
		}
		WriteMarkdownSection(builder, "Examples", GetExamples(command, source), asCodeBlock: true);
		IReadOnlyList<string> arguments = GetArgumentLines(command, source);
		if (arguments.Count > 0) {
			WriteMarkdownSection(builder, "Arguments", arguments, asCodeBlock: true);
		}
		IReadOnlyList<string> options = GetOptionLines(command, source, environmentOptionsOnly: false);
		if (options.Count > 0) {
			WriteMarkdownSection(builder, "Options", NormalizeSectionLines(options), asCodeBlock: true);
		}
		IReadOnlyList<string> environmentOptions = GetOptionLines(command, source, environmentOptionsOnly: true);
		if (environmentOptions.Count > 0) {
			WriteMarkdownSection(builder, "Environment Options", NormalizeSectionLines(environmentOptions), asCodeBlock: true);
		}
		IReadOnlyList<string> requirements = GetRequirementLines(command, source);
		if (requirements.Count > 0) {
			WriteMarkdownRawSection(builder, "Requirements", NormalizeSectionLines(requirements));
		}
		IReadOnlyList<string> notes = GetNotes(source);
		if (notes.Count > 0) {
			WriteMarkdownRawSection(builder, "Notes", NormalizeSectionLines(notes));
		}
		foreach (HelpSection section in GetCustomSections(source)) {
			WriteMarkdownRawSection(builder, FormatMarkdownHeading(section.Heading), section.Lines);
		}
		IReadOnlyList<string> seeAlso = GetSeeAlso(command, source);
		if (seeAlso.Count > 0) {
			builder.AppendLine();
			builder.AppendLine("## See also");
			builder.AppendLine();
			foreach (string line in seeAlso) {
				builder.AppendLine($"- `{line}`");
			}
		}
		builder.AppendLine();
		builder.AppendLine($"- [Clio Command Reference](../../Commands.md#{command.CanonicalName})");
		return builder.ToString();
	}

	public string RenderCommandsMarkdown() {
		StringBuilder builder = new();
		builder.AppendLine("# Clio Command Reference");
		builder.AppendLine();
		builder.AppendLine("Use `clio help` for the terminal overview and `clio <command> --help` for command details.");
		foreach (HelpGroupMetadata group in CommandHelpCatalog.Groups) {
			IReadOnlyList<HelpCommandMetadata> commands = _catalog.GetCommandsForGroup(group.Id);
			if (commands.Count == 0) {
				continue;
			}
			builder.AppendLine();
			builder.AppendLine($"## {group.Title}");
			builder.AppendLine();
			foreach (HelpCommandMetadata command in commands) {
				builder.AppendLine($"<a id=\"{command.CanonicalName}\"></a>");
				foreach (string anchor in command.Aliases
					.Concat(command.LegacyNames)
					.Where(IsArtifactName)
					.Distinct(StringComparer.OrdinalIgnoreCase)) {
					builder.AppendLine($"<a id=\"{anchor}\"></a>");
				}
				builder.AppendLine($"- [`{command.CanonicalName}`](docs/commands/{command.CanonicalName}.md) - {BuildExportDescription(command, alias => $"`{alias}`")}");
			}
		}
		return builder.ToString();
	}

	public string RenderWikiAnchors() {
		StringBuilder builder = new();
		foreach (HelpCommandMetadata command in _catalog.GetVisibleCommands().OrderBy(command => command.CanonicalName, StringComparer.OrdinalIgnoreCase)) {
			IReadOnlyList<string> names = [
				command.CanonicalName,
				..command.Aliases.Where(IsArtifactName),
				..command.LegacyNames.Where(IsArtifactName)
			];
			builder.AppendLine($"{command.CanonicalName}:{string.Join(",", names.Distinct(StringComparer.OrdinalIgnoreCase))}");
		}
		return builder.ToString();
	}

	private static bool IsArtifactName(string value) =>
		!string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace);

	private static void WriteMarkdownSection(StringBuilder builder, string title, IReadOnlyList<string> lines, bool asCodeBlock = false) {
		if (lines.Count == 0) {
			return;
		}
		builder.AppendLine();
		builder.AppendLine($"## {title}");
		builder.AppendLine();
		if (asCodeBlock) {
			builder.AppendLine("```bash");
			foreach (string line in lines) {
				builder.AppendLine(line);
			}
			builder.AppendLine("```");
			return;
		}
		foreach (string line in lines) {
			builder.AppendLine($"- {line}");
		}
	}

	private static void WriteMarkdownRawSection(StringBuilder builder, string title, IReadOnlyList<string> lines) {
		if (lines.Count == 0) {
			return;
		}
		builder.AppendLine();
		builder.AppendLine($"## {title}");
		builder.AppendLine();
		foreach (string line in lines) {
			builder.AppendLine(line);
		}
	}

	private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<string> lines) {
		if (lines.Count == 0) {
			return;
		}
		if (builder.Length > 0) {
			builder.AppendLine();
		}
		builder.AppendLine(title);
		foreach (string line in lines) {
			if (string.IsNullOrWhiteSpace(line)) {
				builder.AppendLine();
				continue;
			}
			builder.AppendLine($"    {line}");
		}
	}

	private static void AppendTwoColumn(StringBuilder builder, string left, string right, int leftWidth, int lineWidth) {
		int descriptionWidth = Math.Max(30, lineWidth - leftWidth - 4);
		List<string> wrapped = Wrap(right, descriptionWidth).ToList();
		builder.Append("  ");
		if (left.Length >= leftWidth) {
			builder.AppendLine(left);
			foreach (string line in wrapped) {
				builder.Append("  ");
				builder.Append(new string(' ', leftWidth));
				builder.AppendLine(line);
			}
			return;
		}
		builder.Append(left.PadRight(leftWidth));
		builder.Append(wrapped.Count > 0 ? wrapped[0] : string.Empty);
		builder.AppendLine();
		foreach (string line in wrapped.Skip(1)) {
			builder.Append("  ");
			builder.Append(new string(' ', leftWidth));
			builder.AppendLine(line);
		}
	}

	private static string BuildExportDescription(HelpCommandMetadata command, Func<string, string> aliasFormatter) {
		if (command.Aliases.Count == 0) {
			return command.ShortDescription;
		}
		return $"{command.ShortDescription}, {string.Join(", ", command.Aliases.Select(aliasFormatter))}";
	}

	private static void AppendRootHelpEntry(StringBuilder builder, HelpCommandMetadata command, int leftWidth, RootHelpRenderMode mode, bool colorizeAliases) {
		string description = BuildRootDescription(command, mode, colorizeAliases);
		builder.Append("  ");
		builder.Append(command.CanonicalName.PadRight(leftWidth));
		builder.Append(description);
		builder.AppendLine();
	}

	private static string BuildRootDescription(HelpCommandMetadata command, RootHelpRenderMode mode, bool colorizeAliases) {
		string separator = mode == RootHelpRenderMode.Runtime && colorizeAliases ? " · " : ", ";
		string description = NormalizeWhitespace(command.ShortDescription);
		if (command.Aliases.Count == 0) {
			return description;
		}
		string aliasSuffix = $"{separator}{string.Join(", ", command.Aliases)}";
		if (!colorizeAliases) {
			return description + aliasSuffix;
		}
		return description + WrapDim(aliasSuffix);
	}

	private static string WrapDim(string value) =>
		string.IsNullOrEmpty(value) ? value : $"\x1b[90m{value}\x1b[0m";

	private static string NormalizeWhitespace(string text) =>
		string.Join(" ", text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

	private static bool SupportsAnsiEscapeCodes() =>
		!Console.IsOutputRedirected &&
		Environment.GetEnvironmentVariable("NO_COLOR") == null &&
		Environment.GetEnvironmentVariable("TERM") != null;

	private static IEnumerable<string> Wrap(string text, int width) {
		if (string.IsNullOrWhiteSpace(text)) {
			return [];
		}
		List<string> lines = [];
		StringBuilder current = new();
		foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
			if (current.Length == 0) {
				current.Append(word);
				continue;
			}
			if (current.Length + word.Length + 1 <= width) {
				current.Append(' ');
				current.Append(word);
				continue;
			}
			lines.Add(current.ToString());
			current.Clear();
			current.Append(word);
		}
		if (current.Length > 0) {
			lines.Add(current.ToString());
		}
		return lines;
	}

	private string RenderGeneratedCommandHelp(HelpCommandMetadata command) {
		StringBuilder builder = new();
		AppendSection(builder, "NAME", [$"{command.CanonicalName} - {command.ShortDescription}"]);
		AppendSection(builder, SecUsage, GetUsageLines(command, HelpDocument.Empty));
		AppendSection(builder, SecDescription, GetDescriptionLines(command, HelpDocument.Empty));
		if (command.Aliases.Count > 0) {
			AppendSection(builder, "ALIASES", [string.Join(", ", command.Aliases)]);
		}
		AppendSection(builder, SecExamples, GetExamples(command, HelpDocument.Empty));
		IReadOnlyList<string> arguments = BuildPositionalArguments(command.OptionsType);
		if (arguments.Count > 0) {
			AppendSection(builder, "ARGUMENTS", arguments);
		}
		IReadOnlyList<string> options = BuildOptions(command.OptionsType, environmentOptionsOnly: false);
		if (options.Count > 0) {
			AppendSection(builder, "OPTIONS", options);
		}
		IReadOnlyList<string> environmentOptions = BuildOptions(command.OptionsType, environmentOptionsOnly: true);
		if (environmentOptions.Count > 0) {
			AppendSection(builder, "ENVIRONMENT OPTIONS", environmentOptions);
		}
		IReadOnlyList<string> requirements = GetRequirementLines(command, HelpDocument.Empty);
		if (requirements.Count > 0) {
			AppendSection(builder, SecRequirements, requirements);
		}
		IReadOnlyList<string> seeAlso = GetSeeAlso(command, HelpDocument.Empty);
		if (seeAlso.Count > 0) {
			AppendSection(builder, SecSeeAlso, seeAlso);
		}
		return builder.ToString();
	}

	private string RenderCommandHelp(HelpCommandMetadata command, HelpDocument source) {
		StringBuilder builder = new();
		foreach (HelpSection section in GetLeadingCustomSections(source)) {
			AppendSection(builder, section.Heading, section.Lines);
		}
		AppendSection(builder, "NAME", GetNameLines(command, source));
		AppendSection(builder, SecUsage, GetUsageLines(command, source));
		AppendSection(builder, SecDescription, GetDescriptionLines(command, source));
		IReadOnlyList<string> aliases = GetAliases(command, source);
		if (aliases.Count > 0) {
			AppendSection(builder, "ALIASES", aliases);
		}
		AppendSection(builder, SecExamples, GetExamples(command, source));
		IReadOnlyList<string> arguments = GetArgumentLines(command, source);
		if (arguments.Count > 0) {
			AppendSection(builder, "ARGUMENTS", arguments);
		}
		IReadOnlyList<string> options = GetOptionLines(command, source, environmentOptionsOnly: false);
		if (options.Count > 0) {
			AppendSection(builder, "OPTIONS", options);
		}
		IReadOnlyList<string> environmentOptions = GetOptionLines(command, source, environmentOptionsOnly: true);
		if (environmentOptions.Count > 0) {
			AppendSection(builder, "ENVIRONMENT OPTIONS", environmentOptions);
		}
		IReadOnlyList<string> requirements = GetRequirementLines(command, source);
		if (requirements.Count > 0) {
			AppendSection(builder, SecRequirements, requirements);
		}
		IReadOnlyList<string> notes = GetNotes(source);
		if (notes.Count > 0) {
			AppendSection(builder, SecNotes, notes);
		}
		foreach (HelpSection section in GetTrailingCustomSections(source)) {
			AppendSection(builder, section.Heading, section.Lines);
		}
		IReadOnlyList<string> seeAlso = GetSeeAlso(command, source);
		if (seeAlso.Count > 0) {
			AppendSection(builder, SecSeeAlso, seeAlso);
		}
		return builder.ToString();
	}

	private HelpDocument LoadHelpDocument(HelpCommandMetadata command) {
		if (!TryGetManualHelpFilePath(command, out string helpFilePath)) {
			return HelpDocument.Empty;
		}
		string content = _fileSystem.File.ReadAllText(helpFilePath);
		return ParseSections(content);
	}

	private bool TryGetManualHelpFilePath(HelpCommandMetadata command, out string helpFilePath) {
		helpFilePath = string.Empty;
		string helpDirectory = Parser.Default.Settings.HelpDirectory;
		if (string.IsNullOrWhiteSpace(helpDirectory) || !_fileSystem.Directory.Exists(helpDirectory)) {
			return false;
		}
		helpFilePath = _fileSystem.Directory
			.EnumerateFiles(helpDirectory, $"{command.CanonicalName}.txt", SearchOption.AllDirectories)
			.FirstOrDefault() ?? string.Empty;
		return !string.IsNullOrWhiteSpace(helpFilePath);
	}

	private static HelpDocument ParseSections(string content) {
		List<(string Heading, string NormalizedHeading, List<string> Lines)> sections = [];
		(string Heading, string NormalizedHeading, List<string> Lines)? currentSection = null;
		foreach (string rawLine in content.Replace("\r\n", "\n").Split('\n')) {
			string line = rawLine.TrimEnd();
			if (TryParseSectionHeading(rawLine, out string heading, out string normalizedHeading)) {
				currentSection = (heading, normalizedHeading, []);
				sections.Add(currentSection.Value);
				continue;
			}
			if (currentSection == null) {
				currentSection = (SecDescription, SecDescription, []);
				sections.Add(currentSection.Value);
			}
			currentSection.Value.Lines.Add(line.Trim('\ufeff'));
			sections[^1] = currentSection.Value;
		}
		bool isAliasShim = content.Contains("alias for", StringComparison.OrdinalIgnoreCase)
			|| content.Contains("legacy heading", StringComparison.OrdinalIgnoreCase)
			|| content.Contains("exists so the legacy heading", StringComparison.OrdinalIgnoreCase);
		return new HelpDocument(
			sections
				.Select(section => new HelpSection(section.Heading, section.NormalizedHeading, TrimEmptyLines(section.Lines)))
				.Where(section => section.Lines.Count > 0)
				.ToArray(),
			isAliasShim);
	}

	private static bool TryParseSectionHeading(string rawLine, out string heading, out string normalizedHeading) {
		heading = string.Empty;
		normalizedHeading = string.Empty;
		if (string.IsNullOrWhiteSpace(rawLine) || char.IsWhiteSpace(rawLine[0])) {
			return false;
		}
		string trimmed = rawLine.Trim().Trim('\ufeff');
		if (!SectionHeadingRegex.IsMatch(trimmed)) {
			return false;
		}
		heading = trimmed.TrimEnd(':');
		normalizedHeading = NormalizeSectionHeading(heading);
		return true;
	}

	private static string NormalizeSectionHeading(string heading) =>
		heading switch {
			"SYNOPSIS" => SecUsage,
			"USAGE" => SecUsage,
			"EXAMPLE" => SecExamples,
			"EXAMPLES" => SecExamples,
			"BEHAVIOR" => SecDescription,
			"DESCRIPTION" => SecDescription,
			"PREREQUISITES" => SecRequirements,
			"REQUIRES" => SecRequirements,
			"REQUIREMENTS" => SecRequirements,
			"OUTPUT FORMAT" => SecNotes,
			"NOTES" => SecNotes,
			_ => heading
		};

	private static IReadOnlyList<string> TrimEmptyLines(IEnumerable<string> lines) {
		List<string> result = lines.ToList();
		while (result.Count > 0 && string.IsNullOrWhiteSpace(result[0])) {
			result.RemoveAt(0);
		}
		while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1])) {
			result.RemoveAt(result.Count - 1);
		}
		return result;
	}

	private static IReadOnlyList<string> GetUsageLines(HelpCommandMetadata command, HelpDocument source) {
		IReadOnlyList<string> usage = GetSectionLines(source, SecUsage);
		if (!source.IsAliasShim && usage.Count > 0) {
			return NormalizeSectionLines(CanonicalizeCommandLines(command, usage));
		}
		return [BuildUsage(command.OptionsType, command.CanonicalName)];
	}

	private static IReadOnlyList<string> GetDescriptionLines(HelpCommandMetadata command, HelpDocument source) {
		IReadOnlyList<string> description = GetSectionLines(source, SecDescription);
		if (!source.IsAliasShim && description.Count > 0) {
			return NormalizeSectionLines(description);
		}
		return [$"{command.ShortDescription}."];
	}

	private static IReadOnlyList<string> GetNameLines(HelpCommandMetadata command, HelpDocument source) {
		IReadOnlyList<string> name = GetSectionLines(source, "NAME");
		if (!source.IsAliasShim && name.Count > 0) {
			return CanonicalizeCommandLines(command, name);
		}
		return [$"{command.CanonicalName} - {command.ShortDescription}"];
	}

	private static IReadOnlyList<string> GetAliases(HelpCommandMetadata command, HelpDocument source) {
		IReadOnlyList<string> aliases = GetSectionLines(source, "ALIASES");
		if (!source.IsAliasShim && aliases.Count > 0) {
			return NormalizeSectionLines(aliases);
		}
		return command.Aliases.Count == 0
			? []
			: [string.Join(", ", command.Aliases)];
	}

	private static IReadOnlyList<string> GetExamples(HelpCommandMetadata command, HelpDocument source) {
		IReadOnlyList<string> examples = GetSectionLines(source, SecExamples);
		if (!source.IsAliasShim && examples.Count > 0) {
			return NormalizeSectionLines(CanonicalizeCommandLines(command, examples));
		}
		IReadOnlyList<string> usageExamples = GetUsageExamples(command.OptionsType);
		if (usageExamples.Count > 0) {
			return CanonicalizeCommandLines(command, usageExamples);
		}
		return [BuildFallbackExample(command.OptionsType, command.CanonicalName)];
	}

	private static IReadOnlyList<string> GetRequirementLines(HelpCommandMetadata command, HelpDocument source) {
		IReadOnlyList<string> requirements = GetSectionLines(source, SecRequirements);
		if (!source.IsAliasShim && requirements.Count > 0) {
			return NormalizeSectionLines(requirements);
		}
		return string.IsNullOrWhiteSpace(command.Requirement)
			? []
			: [command.Requirement];
	}

	private static IReadOnlyList<string> GetNotes(HelpDocument source) {
		IReadOnlyList<string> notes = GetSectionLines(source, SecNotes);
		if (!source.IsAliasShim && notes.Count > 0) {
			return NormalizeSectionLines(notes);
		}
		return [];
	}

	private static IReadOnlyList<string> GetArgumentLines(HelpCommandMetadata command, HelpDocument source) {
		IReadOnlyList<string> arguments = GetSectionLines(source, "ARGUMENTS");
		if (!source.IsAliasShim && arguments.Count > 0) {
			return arguments;
		}
		return BuildPositionalArguments(command.OptionsType);
	}

	private static IReadOnlyList<string> GetOptionLines(HelpCommandMetadata command, HelpDocument source, bool environmentOptionsOnly) {
		string heading = environmentOptionsOnly ? "ENVIRONMENT OPTIONS" : "OPTIONS";
		IReadOnlyList<string> options = GetSectionLines(source, heading);
		if (!source.IsAliasShim && options.Count > 0) {
			return options;
		}
		return BuildOptions(command.OptionsType, environmentOptionsOnly);
	}

	private IReadOnlyList<string> GetSeeAlso(HelpCommandMetadata command, HelpDocument source) {
		IReadOnlyList<string> seeAlso = GetSectionLines(source, SecSeeAlso);
		if (!source.IsAliasShim && seeAlso.Count > 0) {
			return seeAlso
				.Select(line => line.Split('-', 2)[0].Trim())
				.Where(line => !string.IsNullOrWhiteSpace(line))
				.Select(NormalizeRelatedCommand)
				.ToArray();
		}
		return command.RelatedCommands.Count == 0
			? []
			: command.RelatedCommands;
	}

	private static IReadOnlyList<string> GetSectionLines(HelpDocument source, string normalizedHeading) =>
		source.Sections
			.FirstOrDefault(section => string.Equals(section.NormalizedHeading, normalizedHeading, StringComparison.OrdinalIgnoreCase))
			?.Lines ?? [];

	private static IReadOnlyList<HelpSection> GetCustomSections(HelpDocument source) {
		return source.Sections
			.Where(section => !IsStandardSection(section.NormalizedHeading))
			.ToArray();
	}

	private static IReadOnlyList<HelpSection> GetLeadingCustomSections(HelpDocument source) =>
		source.Sections
			.TakeWhile(section => !string.Equals(section.NormalizedHeading, "NAME", StringComparison.OrdinalIgnoreCase))
			.Where(section => !IsStandardSection(section.NormalizedHeading))
			.ToArray();

	private static IReadOnlyList<HelpSection> GetTrailingCustomSections(HelpDocument source) =>
		source.Sections
			.SkipWhile(section => !string.Equals(section.NormalizedHeading, "NAME", StringComparison.OrdinalIgnoreCase))
			.Where(section => !IsStandardSection(section.NormalizedHeading))
			.ToArray();

	private static bool IsStandardSection(string normalizedHeading) =>
		normalizedHeading is "ALIASES"
			or "ARGUMENTS"
			or "ENVIRONMENT OPTIONS"
			or "NAME"
			or "OPTIONS"
			or SecDescription
			or SecExamples
			or SecNotes
			or SecRequirements
			or SecSeeAlso
			or SecUsage;

	private static string FormatMarkdownHeading(string heading) =>
		CultureInfo.InvariantCulture.TextInfo.ToTitleCase(heading.ToLowerInvariant());

	private string NormalizeRelatedCommand(string name) =>
		_catalog.TryGetCommand(name, out HelpCommandMetadata command)
			? command.CanonicalName
			: name;

	private static string BuildUsage(Type optionsType, string commandName) {
		IReadOnlyList<(int Index, string Display, bool Required, string HelpText, object DefaultValue)> arguments =
			GetPositionalArgumentDescriptors(optionsType);
		StringBuilder builder = new($"clio {commandName}");
		foreach ((int _, string display, bool required, _, _) in arguments.OrderBy(argument => argument.Index)) {
			builder.Append(' ');
			builder.Append(required ? $"<{display}>" : $"[<{display}>]");
		}
		builder.Append(" [options]");
		return builder.ToString();
	}

	private static string BuildFallbackExample(Type optionsType, string commandName) {
		if (BuildPositionalArguments(optionsType).Count == 0 && BuildOptions(optionsType, true).Count > 0) {
			return $"clio {commandName} -e dev";
		}
		return BuildUsage(optionsType, commandName);
	}

	private static IReadOnlyList<string> GetUsageExamples(Type optionsType) {
		PropertyInfo examplesProperty = optionsType
			.GetProperty("Examples", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
		if (examplesProperty?.GetValue(null) is not IEnumerable<Example> examples) {
			return [];
		}
		return examples
			.Select(example => example?.ToString())
			.Where(example => !string.IsNullOrWhiteSpace(example) && LooksLikeCommandLine(example))
			.ToArray();
	}

	private static bool LooksLikeCommandLine(string text) =>
		!DotQualifiedNameRegex.IsMatch(text.Trim());

	private static IReadOnlyList<string> BuildPositionalArguments(Type optionsType) =>
		GetPositionalArgumentDescriptors(optionsType)
			.OrderBy(argument => argument.Index)
			.SelectMany(argument => FormatParameter(argument.Display, BuildParameterDescription(argument.Required, argument.HelpText, argument.DefaultValue)))
			.ToArray();

	private static IReadOnlyList<(int Index, string Display, bool Required, string HelpText, object DefaultValue)> GetPositionalArgumentDescriptors(Type optionsType) =>
		GetProperties(optionsType)
			.Select(property => (Property: property, Attribute: property.GetCustomAttribute<ValueAttribute>(true)))
			.Where(item => item.Attribute != null)
			.Select(item => (
				item.Attribute.Index,
				string.IsNullOrWhiteSpace(item.Attribute.MetaName) ? item.Property.Name : item.Attribute.MetaName,
				item.Attribute.Required,
				item.Attribute.HelpText,
				GetDefaultValue(item.Property)))
			.ToArray();

	private static IReadOnlyList<string> BuildOptions(Type optionsType, bool environmentOptionsOnly) =>
		GetProperties(optionsType)
			.Select(property => (Property: property, Attribute: property.GetCustomAttribute<OptionAttribute>(true)))
			.Where(item => item.Attribute != null)
			.Where(item => IsEnvironmentOption(item.Property) == environmentOptionsOnly)
			.OrderBy(item => item.Property.DeclaringType == typeof(EnvironmentOptions) ? 1 : 0)
			.ThenBy(item => item.Property.MetadataToken)
			.SelectMany(item => FormatParameter(
				BuildOptionDisplay(item.Attribute, item.Property),
				BuildParameterDescription(item.Attribute.Required, item.Attribute.HelpText, item.Attribute.Default ?? GetDefaultValue(item.Property))))
			.ToArray();

	private static IEnumerable<PropertyInfo> GetProperties(Type optionsType) =>
		optionsType
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.GetMethod?.IsStatic != true)
			.OrderBy(property => property.MetadataToken);

	private static bool IsEnvironmentOption(PropertyInfo property) {
		Type declaringType = property.DeclaringType;
		return declaringType == typeof(EnvironmentOptions) || declaringType == typeof(EnvironmentNameOptions);
	}

	private static object GetDefaultValue(PropertyInfo property) =>
		property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;

	private static string BuildOptionDisplay(OptionAttribute option, PropertyInfo property) {
		List<string> names = [];
		if (!string.IsNullOrWhiteSpace(option.ShortName)) {
			names.Add($"-{option.ShortName}");
		}
		if (!string.IsNullOrWhiteSpace(option.LongName)) {
			names.Add($"--{option.LongName}");
		}
		string display = string.Join(", ", names);
		if (RequiresValue(property.PropertyType)) {
			display += $" <{GetValuePlaceholder(property)}>";
		}
		return display;
	}

	private static IReadOnlyList<string> CanonicalizeCommandLines(HelpCommandMetadata command, IEnumerable<string> lines) =>
		lines
			.Select(line => CanonicalizeCommandLine(command, line))
			.ToArray();

	private static IReadOnlyList<string> NormalizeSectionLines(IEnumerable<string> lines) =>
		lines
			.Select(line => line.TrimStart())
			.ToArray();

	private static string CanonicalizeCommandLine(HelpCommandMetadata command, string line) {
		if (string.IsNullOrWhiteSpace(line)) {
			return line;
		}
		IEnumerable<string> names = command.LegacyNames
			.Concat(command.Aliases)
			.Distinct(StringComparer.OrdinalIgnoreCase);
		string result = line;
		foreach (string name in names) {
			result = Regex.Replace(
				result,
				$@"(?<!\S)clio\s+{Regex.Escape(name)}(?=\s|$)",
				$"clio {command.CanonicalName}",
				RegexOptions.IgnoreCase,
				RegexTimeout);
			result = Regex.Replace(
				result,
				$@"^(\s*){Regex.Escape(name)}(?=\s|$)",
				$"$1{command.CanonicalName}",
				RegexOptions.IgnoreCase,
				RegexTimeout);
		}
		return result;
	}

	private static bool RequiresValue(Type propertyType) {
		Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
		return targetType != typeof(bool);
	}

	private static string GetValuePlaceholder(PropertyInfo property) {
		Type targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
		if (targetType == typeof(int) || targetType == typeof(long)) {
			return "NUMBER";
		}
		if (targetType == typeof(bool)) {
			return "true|false";
		}
		return "VALUE";
	}

	private static IReadOnlyList<string> FormatParameter(string title, string description) =>
		string.IsNullOrWhiteSpace(description)
			? [$"{title}"]
			: [$"{title}", ..Wrap(description, 78).Select(line => $"    {line}")];

	private static string BuildParameterDescription(bool required, string helpText, object defaultValue) {
		List<string> suffixes = [];
		if (required) {
			suffixes.Add("Required.");
		}
		if (defaultValue != null && defaultValue.ToString() != string.Empty && !Equals(defaultValue, false)) {
			suffixes.Add($"Default: {defaultValue}.");
		}
		string baseText = string.IsNullOrWhiteSpace(helpText) ? string.Empty : helpText.Trim().TrimEnd('.');
		if (suffixes.Count == 0) {
			return baseText;
		}
		return string.IsNullOrWhiteSpace(baseText)
			? string.Join(" ", suffixes)
			: $"{baseText}. {string.Join(" ", suffixes)}";
	}
}
