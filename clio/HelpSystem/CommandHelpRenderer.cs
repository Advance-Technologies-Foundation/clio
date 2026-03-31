using System;
using System.Collections.Generic;
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

internal sealed record HelpFileSections(
	IReadOnlyDictionary<string, IReadOnlyList<string>> Sections,
	bool IsAliasShim);

internal sealed class CommandHelpRenderer {
	private static readonly Regex SectionHeadingRegex = new("^[A-Z][A-Z0-9 /&-]+:?$", RegexOptions.Compiled);
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
		int leftWidth = Math.Max(32, _catalog.VisibleCommands.Max(command => command.CanonicalName.Length) + 2);
		StringBuilder builder = new();
		builder.AppendLine("clio - Creatio CLI");
		builder.AppendLine();
		builder.AppendLine("Usage:");
		builder.AppendLine("  clio <command> [arguments] [options]");
		builder.AppendLine();
		builder.AppendLine("Commands:");
		foreach (HelpCommandMetadata command in _catalog.VisibleCommands.OrderBy(command => command.CanonicalName, StringComparer.OrdinalIgnoreCase)) {
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
		HelpFileSections source = LoadHelpSections(command);
		StringBuilder builder = new();
		AppendSection(builder, "NAME", [$"{command.CanonicalName} - {command.ShortDescription}"]);
		AppendSection(builder, "USAGE", GetUsageLines(command, source));
		AppendSection(builder, "DESCRIPTION", GetDescriptionLines(command, source));
		if (command.Aliases.Count > 0) {
			AppendSection(builder, "ALIASES", [string.Join(", ", command.Aliases)]);
		}
		AppendSection(builder, "EXAMPLES", GetExamples(command, source));
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
		IReadOnlyList<string> requirements = GetRequirementLines(command, source);
		if (requirements.Count > 0) {
			AppendSection(builder, "REQUIREMENTS", requirements);
		}
		IReadOnlyList<string> notes = GetNotes(command, source);
		if (notes.Count > 0) {
			AppendSection(builder, "NOTES", notes);
		}
		IReadOnlyList<string> seeAlso = GetSeeAlso(command, source);
		if (seeAlso.Count > 0) {
			AppendSection(builder, "SEE ALSO", seeAlso);
		}
		return builder.ToString();
	}

	public string RenderMarkdownDoc(HelpCommandMetadata command) {
		HelpFileSections source = LoadHelpSections(command);
		StringBuilder builder = new();
		builder.AppendLine($"# {command.CanonicalName}");
		builder.AppendLine();
		builder.AppendLine(command.ShortDescription + ".");
		builder.AppendLine();
		builder.AppendLine("## Usage");
		builder.AppendLine();
		builder.AppendLine("```bash");
		foreach (string line in GetUsageLines(command, source)) {
			builder.AppendLine(line);
		}
		builder.AppendLine("```");
		builder.AppendLine();
		builder.AppendLine("## Description");
		builder.AppendLine();
		foreach (string line in GetDescriptionLines(command, source)) {
			builder.AppendLine(line);
		}
		if (command.Aliases.Count > 0) {
			builder.AppendLine();
			builder.AppendLine("## Aliases");
			builder.AppendLine();
			builder.AppendLine(string.Join(", ", command.Aliases.Select(alias => $"`{alias}`")));
		}
		WriteMarkdownSection(builder, "Examples", GetExamples(command, source), asCodeBlock: true);
		WriteMarkdownSection(builder, "Arguments", BuildPositionalArguments(command.OptionsType), asCodeBlock: true);
		WriteMarkdownSection(builder, "Options", BuildOptions(command.OptionsType, environmentOptionsOnly: false), asCodeBlock: true);
		WriteMarkdownSection(builder, "Environment Options", BuildOptions(command.OptionsType, environmentOptionsOnly: true), asCodeBlock: true);
		WriteMarkdownSection(builder, "Requirements", GetRequirementLines(command, source));
		WriteMarkdownSection(builder, "Notes", GetNotes(command, source));
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
		foreach (HelpGroupMetadata group in _catalog.Groups) {
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
		foreach (HelpCommandMetadata command in _catalog.VisibleCommands.OrderBy(command => command.CanonicalName, StringComparer.OrdinalIgnoreCase)) {
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

	private HelpFileSections LoadHelpSections(HelpCommandMetadata command) {
		string helpDirectory = Parser.Default.Settings.HelpDirectory;
		if (string.IsNullOrWhiteSpace(helpDirectory) || !_fileSystem.Directory.Exists(helpDirectory)) {
			return new HelpFileSections(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase), false);
		}
		foreach (string candidate in GetHelpFileCandidates(command)) {
			string? file = _fileSystem.Directory
				.EnumerateFiles(helpDirectory, $"{candidate}.txt", SearchOption.AllDirectories)
				.FirstOrDefault();
			if (file == null) {
				continue;
			}
			string content = _fileSystem.File.ReadAllText(file);
			return ParseSections(content);
		}
		return new HelpFileSections(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase), false);
	}

	private static IEnumerable<string> GetHelpFileCandidates(HelpCommandMetadata command) =>
		[command.CanonicalName, ..command.LegacyNames, ..command.Aliases];

	private static HelpFileSections ParseSections(string content) {
		Dictionary<string, List<string>> sections = new(StringComparer.OrdinalIgnoreCase);
		string currentSection = "DESCRIPTION";
		sections[currentSection] = [];
		foreach (string rawLine in content.Replace("\r\n", "\n").Split('\n')) {
			string line = rawLine.TrimEnd();
			string heading = NormalizeSectionHeading(line);
			if (!string.IsNullOrEmpty(heading)) {
				currentSection = heading;
				sections.TryAdd(currentSection, []);
				continue;
			}
			sections[currentSection].Add(line.Trim('\ufeff'));
		}
		bool isAliasShim = content.Contains("alias for", StringComparison.OrdinalIgnoreCase)
			|| content.Contains("legacy heading", StringComparison.OrdinalIgnoreCase)
			|| content.Contains("exists so the legacy heading", StringComparison.OrdinalIgnoreCase);
		return new HelpFileSections(
			sections.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)TrimEmptyLines(pair.Value), StringComparer.OrdinalIgnoreCase),
			isAliasShim);
	}

	private static string NormalizeSectionHeading(string line) {
		string trimmed = line.Trim().Trim('\ufeff');
		if (string.IsNullOrWhiteSpace(trimmed) || !SectionHeadingRegex.IsMatch(trimmed)) {
			return string.Empty;
		}
		string heading = trimmed.TrimEnd(':');
		return heading switch {
			"SYNOPSIS" => "USAGE",
			"USAGE" => "USAGE",
			"EXAMPLE" => "EXAMPLES",
			"EXAMPLES" => "EXAMPLES",
			"BEHAVIOR" => "DESCRIPTION",
			"DESCRIPTION" => "DESCRIPTION",
			"PREREQUISITES" => "REQUIREMENTS",
			"REQUIRES" => "REQUIREMENTS",
			"REQUIREMENTS" => "REQUIREMENTS",
			"OUTPUT FORMAT" => "NOTES",
			"NOTES" => "NOTES",
			"ALIASES" => "ALIASES",
			"ARGUMENTS" => "ARGUMENTS",
			"OPTIONS" => "OPTIONS",
			"SEE ALSO" => "SEE ALSO",
			"REPORTING BUGS" => "REPORTING BUGS",
			"NAME" => "NAME",
			"COMMAND TYPE" => "COMMAND TYPE",
			_ => string.Empty
		};
	}

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

	private static IReadOnlyList<string> GetUsageLines(HelpCommandMetadata command, HelpFileSections source) {
		if (!source.IsAliasShim && source.Sections.TryGetValue("USAGE", out IReadOnlyList<string> usage) && usage.Count > 0) {
			return NormalizeSectionLines(CanonicalizeCommandLines(command, usage));
		}
		return [BuildUsage(command.OptionsType, command.CanonicalName)];
	}

	private static IReadOnlyList<string> GetDescriptionLines(HelpCommandMetadata command, HelpFileSections source) {
		if (!source.IsAliasShim && source.Sections.TryGetValue("DESCRIPTION", out IReadOnlyList<string> description) && description.Count > 0) {
			return NormalizeSectionLines(description);
		}
		return [$"{command.ShortDescription}."];
	}

	private static IReadOnlyList<string> GetExamples(HelpCommandMetadata command, HelpFileSections source) {
		if (!source.IsAliasShim && source.Sections.TryGetValue("EXAMPLES", out IReadOnlyList<string> examples) && examples.Count > 0) {
			return NormalizeSectionLines(CanonicalizeCommandLines(command, examples));
		}
		IReadOnlyList<string> usageExamples = GetUsageExamples(command.OptionsType);
		if (usageExamples.Count > 0) {
			return CanonicalizeCommandLines(command, usageExamples);
		}
		return [BuildFallbackExample(command.OptionsType, command.CanonicalName)];
	}

	private static IReadOnlyList<string> GetRequirementLines(HelpCommandMetadata command, HelpFileSections source) {
		if (!source.IsAliasShim && source.Sections.TryGetValue("REQUIREMENTS", out IReadOnlyList<string> requirements) && requirements.Count > 0) {
			return NormalizeSectionLines(requirements);
		}
		return string.IsNullOrWhiteSpace(command.Requirement)
			? []
			: [command.Requirement];
	}

	private static IReadOnlyList<string> GetNotes(HelpCommandMetadata command, HelpFileSections source) {
		if (!source.IsAliasShim && source.Sections.TryGetValue("NOTES", out IReadOnlyList<string> notes) && notes.Count > 0) {
			return NormalizeSectionLines(notes);
		}
		return [];
	}

	private IReadOnlyList<string> GetSeeAlso(HelpCommandMetadata command, HelpFileSections source) {
		if (!source.IsAliasShim && source.Sections.TryGetValue("SEE ALSO", out IReadOnlyList<string> seeAlso) && seeAlso.Count > 0) {
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
			.Where(example => !string.IsNullOrWhiteSpace(example))
			.ToArray();
	}

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
				RegexOptions.IgnoreCase);
			result = Regex.Replace(
				result,
				$@"^(\s*){Regex.Escape(name)}(?=\s|$)",
				$"$1{command.CanonicalName}",
				RegexOptions.IgnoreCase);
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
