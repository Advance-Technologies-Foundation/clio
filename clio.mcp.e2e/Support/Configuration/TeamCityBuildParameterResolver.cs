using System.Globalization;
using System.Text;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class TeamCityBuildParameterResolver {
	private const string BuildPropertiesEnvironmentVariable = "TEAMCITY_BUILD_PROPERTIES_FILE";
	private const string ConfigurationPropertiesFileKey = "teamcity.configuration.properties.file";

	internal static string? Resolve(string parameterName) {
		string? buildPropertiesPath = Environment.GetEnvironmentVariable(BuildPropertiesEnvironmentVariable);
		return Resolve(parameterName, buildPropertiesPath, File.Exists, File.ReadAllLines);
	}

	internal static string? Resolve(string parameterName, string? buildPropertiesPath,
		Func<string, bool> fileExists, Func<string, string[]> readAllLines) {
		if (string.IsNullOrWhiteSpace(parameterName) || string.IsNullOrWhiteSpace(buildPropertiesPath)
			|| !fileExists(buildPropertiesPath)) {
			return null;
		}

		IReadOnlyDictionary<string, string> buildProperties = Parse(readAllLines(buildPropertiesPath));
		if (buildProperties.TryGetValue(parameterName, out string? directValue)
			&& !string.IsNullOrWhiteSpace(directValue)) {
			return directValue;
		}
		if (!buildProperties.TryGetValue(ConfigurationPropertiesFileKey, out string? configurationPath)
			|| string.IsNullOrWhiteSpace(configurationPath) || !fileExists(configurationPath)) {
			return null;
		}

		IReadOnlyDictionary<string, string> configurationProperties = Parse(readAllLines(configurationPath));
		return configurationProperties.TryGetValue(parameterName, out string? value)
			&& !string.IsNullOrWhiteSpace(value)
			? value
			: null;
	}

	private static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines) {
		Dictionary<string, string> properties = new(StringComparer.Ordinal);
		foreach (string rawLine in JoinContinuations(lines)) {
			string line = rawLine.TrimStart();
			if (line.Length == 0 || line[0] is '#' or '!') {
				continue;
			}

			int separator = FindSeparator(line);
			string rawKey = separator < 0 ? line : line[..separator];
			int valueStart = separator < 0 ? line.Length : SkipValueSeparator(line, separator);
			properties[Unescape(rawKey.TrimEnd())] = Unescape(line[valueStart..]);
		}
		return properties;
	}

	private static IEnumerable<string> JoinContinuations(IEnumerable<string> lines) {
		StringBuilder? current = null;
		foreach (string line in lines) {
			current ??= new StringBuilder();
			current.Append(line);
			int trailingBackslashes = line.Reverse().TakeWhile(character => character == '\\').Count();
			if (trailingBackslashes % 2 == 1) {
				current.Length--;
				continue;
			}
			yield return current.ToString();
			current = null;
		}
		if (current is not null) {
			yield return current.ToString();
		}
	}

	private static int FindSeparator(string line) {
		bool escaped = false;
		for (int index = 0; index < line.Length; index++) {
			char character = line[index];
			if (!escaped && (character is '=' or ':' || char.IsWhiteSpace(character))) {
				return index;
			}
			escaped = !escaped && character == '\\';
			if (character != '\\') {
				escaped = false;
			}
		}
		return -1;
	}

	private static int SkipValueSeparator(string line, int separator) {
		int index = separator;
		while (index < line.Length && char.IsWhiteSpace(line[index])) {
			index++;
		}
		if (index < line.Length && line[index] is '=' or ':') {
			index++;
		}
		while (index < line.Length && char.IsWhiteSpace(line[index])) {
			index++;
		}
		return index;
	}

	private static string Unescape(string value) {
		StringBuilder result = new(value.Length);
		for (int index = 0; index < value.Length; index++) {
			if (value[index] != '\\' || index == value.Length - 1) {
				result.Append(value[index]);
				continue;
			}

			char escaped = value[++index];
			switch (escaped) {
				case 't': result.Append('\t'); break;
				case 'r': result.Append('\r'); break;
				case 'n': result.Append('\n'); break;
				case 'f': result.Append('\f'); break;
				case 'u' when index + 4 < value.Length
					&& int.TryParse(value.AsSpan(index + 1, 4), NumberStyles.HexNumber,
						CultureInfo.InvariantCulture, out int codePoint):
					result.Append((char)codePoint);
					index += 4;
					break;
				default: result.Append(escaped); break;
			}
		}
		return result.ToString();
	}
}
