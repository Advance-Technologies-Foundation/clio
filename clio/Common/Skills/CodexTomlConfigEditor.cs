using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Clio.Common.Skills;

/// <summary>
/// Default <see cref="ICodexTomlConfigEditor"/> — a faithful C# port of the
/// table-block helpers in the toolkit's <c>install.py</c>.
/// </summary>
public sealed partial class CodexTomlConfigEditor(IFileSystem fileSystem) : ICodexTomlConfigEditor {
	private readonly IFileSystem _fileSystem = fileSystem;

	// TOML standard table headers: `[name]` and `[[name]]`. Names are dotted
	// segments; each segment is a bare key (ASCII letters/digits/_/-) or a quoted
	// string. Matches the whole stripped line — the header must be the only thing
	// on the line, optionally followed by whitespace or a comment. Ported verbatim
	// from install.py `_TOML_TABLE_HEADER_RE`.
	[GeneratedRegex(
		"^(?<header>\\[{1,2}" +
		"(?:[A-Za-z0-9_\\-]+|\"(?:[^\"\\\\]|\\\\.)*\"|'[^']*')" +
		"(?:\\.(?:[A-Za-z0-9_\\-]+|\"(?:[^\"\\\\]|\\\\.)*\"|'[^']*'))*" +
		"\\]{1,2})" +
		"\\s*(?:#.*)?$")]
	private static partial Regex TableHeaderRegex();

	/// <inheritdoc />
	public void MergeClioMcpServer(string configPath) {
		string existing = _fileSystem.ExistsFile(configPath) ? _fileSystem.ReadAllText(configPath) : string.Empty;
		if (ClioMcpServerExists(existing)) {
			return;
		}

		string block =
			"\n" +
			"# Added by clio installer.\n" +
			"[mcp_servers.clio]\n" +
			$"command = {TomlQuote("clio")}\n" +
			$"args = {TomlStringArray(["mcp-server"])}\n" +
			"enabled = true\n";
		string separator = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : "\n";
		_fileSystem.WriteAllTextToFile(configPath, existing + separator + block);
	}

	/// <inheritdoc />
	public void RemoveMarketplaceSection(string configPath, string marketplaceName) {
		RemoveTableBlock(configPath, [
			$"[marketplaces.{marketplaceName}]",
			$"[marketplaces.{TomlQuote(marketplaceName)}]"
		]);
	}

	/// <inheritdoc />
	public void RemovePluginSection(string configPath, string pluginName, string marketplaceName) {
		string pluginKey = $"{pluginName}@{marketplaceName}";
		RemoveTableBlock(configPath, [$"[plugins.{TomlQuote(pluginKey)}]"]);
	}

	/// <inheritdoc />
	public void RemoveSkillConfigOverride(string configPath, string skillName) {
		if (!_fileSystem.ExistsFile(configPath)) {
			return;
		}

		List<string> lines = SplitLinesKeepEnds(_fileSystem.ReadAllText(configPath));
		List<string> updated = [];
		int index = 0;
		bool changed = false;
		while (index < lines.Count) {
			if (TableHeaderMarker(lines[index]) != "[[skills.config]]") {
				updated.Add(lines[index]);
				index++;
				continue;
			}

			int end = index + 1;
			while (end < lines.Count && !IsTableHeader(lines[end])) {
				end++;
			}

			string blockText = string.Concat(lines.GetRange(index, end - index));
			if (blockText.Contains($"name = \"{skillName}\"", StringComparison.Ordinal)) {
				changed = true;
			}
			else {
				updated.AddRange(lines.GetRange(index, end - index));
			}

			index = end;
		}

		if (changed) {
			_fileSystem.WriteAllTextToFile(configPath, string.Concat(updated));
		}
	}

	/// <summary>
	/// Removes a top-level TOML table block whose header matches one of the markers.
	/// The block extends from the matching header to (but not including) the next
	/// valid table header line — not merely the next line starting with <c>[</c>,
	/// which would mis-handle multi-line array literals.
	/// </summary>
	private void RemoveTableBlock(string configPath, IReadOnlyCollection<string> markers) {
		if (!_fileSystem.ExistsFile(configPath)) {
			return;
		}

		List<string> lines = SplitLinesKeepEnds(_fileSystem.ReadAllText(configPath));
		List<string> updated = [];
		int index = 0;
		bool changed = false;
		while (index < lines.Count) {
			string marker = TableHeaderMarker(lines[index]);
			if (marker is not null && markers.Contains(marker)) {
				int end = index + 1;
				while (end < lines.Count && !IsTableHeader(lines[end])) {
					end++;
				}

				changed = true;
				index = end;
				continue;
			}

			updated.Add(lines[index]);
			index++;
		}

		if (changed) {
			_fileSystem.WriteAllTextToFile(configPath, string.Concat(updated));
		}
	}

	private static bool ClioMcpServerExists(string configText) =>
		configText.Contains("[mcp_servers.clio]", StringComparison.Ordinal)
		|| configText.Contains($"[mcp_servers.{TomlQuote("clio")}]", StringComparison.Ordinal);

	/// <summary>
	/// Returns the table-header marker for a header line (excluding any comment),
	/// or <c>null</c> when the line is not a table header.
	/// </summary>
	private static string TableHeaderMarker(string line) {
		Match match = TableHeaderRegex().Match(line.Trim());
		return match.Success ? match.Groups["header"].Value : null;
	}

	private static bool IsTableHeader(string line) => TableHeaderRegex().IsMatch(line.Trim());

	private static string TomlQuote(string value) {
		StringBuilder builder = new("\"");
		foreach (char ch in value) {
			builder.Append(ch switch {
				'\\' => "\\\\",
				'"' => "\\\"",
				_ => ch.ToString()
			});
		}

		return builder.Append('"').ToString();
	}

	private static string TomlStringArray(IReadOnlyList<string> values) {
		string[] quoted = new string[values.Count];
		for (int i = 0; i < values.Count; i++) {
			quoted[i] = TomlQuote(values[i]);
		}

		return "[" + string.Join(", ", quoted) + "]";
	}

	/// <summary>
	/// Splits text into lines, keeping each line's original terminator
	/// (<c>\r\n</c>, <c>\n</c>, or <c>\r</c>), mirroring Python's
	/// <c>splitlines(keepends=True)</c> so reassembly is byte-faithful.
	/// </summary>
	private static List<string> SplitLinesKeepEnds(string text) {
		List<string> lines = [];
		if (string.IsNullOrEmpty(text)) {
			return lines;
		}

		int start = 0;
		for (int i = 0; i < text.Length; i++) {
			char ch = text[i];
			if (ch == '\n') {
				lines.Add(text[start..(i + 1)]);
				start = i + 1;
			}
			else if (ch == '\r') {
				int end = i + 1 < text.Length && text[i + 1] == '\n' ? i + 2 : i + 1;
				lines.Add(text[start..end]);
				i = end - 1;
				start = end;
			}
		}

		if (start < text.Length) {
			lines.Add(text[start..]);
		}

		return lines;
	}
}
