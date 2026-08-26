using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Clio.Command;

/// <summary>Describes the result of configuring package-specific NLog routing.</summary>
/// <param name="Success">Whether the configuration completed successfully.</param>
/// <param name="Changed">Whether either NLog file changed.</param>
/// <param name="LoggerName">Resolved generated logger name.</param>
/// <param name="TargetName">Deterministic NLog target name.</param>
/// <param name="LogPath">Configured NLog file path expression.</param>
/// <param name="ErrorMessage">Failure detail when <paramref name="Success"/> is false.</param>
public sealed record CustomLoggingConfigurationResult(bool Success, bool Changed, string LoggerName,
	string TargetName, string LogPath, string ErrorMessage);

/// <summary>Configures package-specific NLog routing in a local Creatio installation.</summary>
public interface ICustomLoggingConfigurator {
	/// <summary>Validates and updates both NLog files, restoring backups on failure.</summary>
	/// <param name="environmentPath">Registered local Creatio installation path.</param>
	/// <param name="packageName">Package containing the generated logger constant.</param>
	/// <param name="minLevel">Minimum NLog level.</param>
	/// <param name="fileName">Optional simple log file name.</param>
	/// <returns>The configuration result.</returns>
	CustomLoggingConfigurationResult Configure(string environmentPath, string packageName,
		string minLevel, string fileName);
}

internal sealed class CustomLoggingConfigurator(IFileSystem fileSystem) : ICustomLoggingConfigurator {
	private const string NLogConfigFileName = "nlog.config";
	private const string NLogTargetsConfigFileName = "nlog.targets.config";
	private const string NetFrameworkApplicationFolder = "Terrasoft.WebApp";
	private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
	private const string DefaultLayout = "${DefaultLayout}";
	private const string TodayLogPath = "${TodayLogPath}";
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private static readonly Regex SafePackageName = new(@"^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.Compiled,
		RegexTimeout);
	private static readonly Regex SafeFileName = new(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled,
		RegexTimeout);
	private static readonly Regex LoggerConstant = new(
		@"^\s*(?:internal\s+)?const\s+string\s+LoggerName\s*=\s*""(?<name>[A-Za-z_][A-Za-z0-9_.-]*)""\s*;\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline, RegexTimeout);
	private static readonly string[] SupportedMinLevels = ["Trace", "Debug", "Info", "Warn", "Error", "Fatal", "Off"];
	private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase) {
		"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
	};
	private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

	internal static bool IsSupportedMinLevel(string value) => SupportedMinLevels.Contains(value?.Trim(), StringComparer.OrdinalIgnoreCase);
	internal static bool IsSafePackageName(string value) => !string.IsNullOrWhiteSpace(value) && SafePackageName.IsMatch(value);
	internal static bool IsSafeFileName(string value) => string.IsNullOrWhiteSpace(value)
		|| value is not "." and not ".." && SafeFileName.IsMatch(value)
			&& !ReservedFileNames.Contains(value.Split('.')[0]);

	public CustomLoggingConfigurationResult Configure(string environmentPath, string packageName,
		string minLevel, string fileName) {
		try {
			ValidateArguments(packageName, minLevel, fileName);
			string root = ResolveApplicationRoot(environmentPath);
			string constantsPath = _fileSystem.Path.Combine(root, "Terrasoft.Configuration", "Pkg", packageName,
				"Files", "src", "cs", "Constants.cs");
			if (!_fileSystem.File.Exists(constantsPath)) {
				return Failure($"Package '{packageName}' does not contain generated logger constants at '{constantsPath}'.");
			}
			MatchCollection matches = LoggerConstant.Matches(_fileSystem.File.ReadAllText(constantsPath));
			if (matches.Count != 1) {
				return Failure($"Expected exactly one generated string constant named LoggerName in '{constantsPath}', found {matches.Count}.");
			}

			string loggerName = matches[0].Groups["name"].Value;
			string baseName = loggerName.EndsWith("App", StringComparison.Ordinal) && loggerName.Length > 3
				? loggerName[..^3]
				: loggerName;
			string targetName = char.ToLowerInvariant(baseName[0]) + baseName[1..] + "Appender";
			string effectiveFileName = NormalizeFileName(fileName, baseName);
			if (!IsSafeFileName(effectiveFileName)) {
				return Failure($"Resolved log file name '{effectiveFileName}' is reserved or unsafe.");
			}
			string level = SupportedMinLevels.Single(value => value.Equals(minLevel.Trim(), StringComparison.OrdinalIgnoreCase));
			string logPath = $"{TodayLogPath}/{effectiveFileName}";
			string rulesPath = _fileSystem.Path.Combine(root, NLogConfigFileName);
			string targetsPath = _fileSystem.Path.Combine(root, NLogTargetsConfigFileName);
			FileEdit rules = BuildRulesEdit(rulesPath, ReadTextFile(rulesPath), loggerName, targetName, level);
			FileEdit targets = BuildTargetsEdit(targetsPath, ReadTextFile(targetsPath), targetName, logPath);
			string error = ApplyWithBackups([targets, rules], () =>
				VerifySavedConfiguration(rulesPath, targetsPath, loggerName, targetName, level, logPath));
			return error is null
				? new(true, rules.Changed || targets.Changed, loggerName, targetName, logPath, null)
				: Failure(error, loggerName, targetName, logPath);
		}
		catch (Exception exception) when (IsExpectedFailure(exception)) {
			return Failure(exception.Message);
		}
	}

	private static void ValidateArguments(string packageName, string minLevel, string fileName) {
		if (!IsSafePackageName(packageName)) {
			throw new ArgumentException("Package name may contain only letters, digits, underscore, dot, and hyphen, and must start with a letter or underscore.");
		}
		if (!IsSupportedMinLevel(minLevel)) {
			throw new ArgumentException("Min level must be one of: Trace, Debug, Info, Warn, Error, Fatal, Off.");
		}
		if (!IsSafeFileName(fileName)) {
			throw new ArgumentException("File name must be a simple file name without directories or NLog layout expressions.");
		}
	}

	private string ResolveApplicationRoot(string environmentPath) {
		if (string.IsNullOrWhiteSpace(environmentPath)) {
			throw new InvalidDataException("The registered environment does not define EnvironmentPath.");
		}
		string root = _fileSystem.Path.GetFullPath(environmentPath);
		if (HasNLogFiles(root)) {
			return root;
		}
		string netFrameworkRoot = _fileSystem.Path.Combine(root, NetFrameworkApplicationFolder);
		if (HasNLogFiles(netFrameworkRoot)) {
			return netFrameworkRoot;
		}
		throw new FileNotFoundException($"Could not find both '{NLogConfigFileName}' and '{NLogTargetsConfigFileName}' under EnvironmentPath '{root}' or its '{NetFrameworkApplicationFolder}' folder.");
	}

	private bool HasNLogFiles(string path) => _fileSystem.File.Exists(_fileSystem.Path.Combine(path, NLogConfigFileName))
		&& _fileSystem.File.Exists(_fileSystem.Path.Combine(path, NLogTargetsConfigFileName));

	private static string NormalizeFileName(string fileName, string baseName) {
		string value = string.IsNullOrWhiteSpace(fileName) ? baseName + ".log" : fileName.Trim();
		return value.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ? value : value + ".log";
	}

	private static FileEdit BuildRulesEdit(string path, TextFile text, string loggerName, string targetName, string level) {
		XDocument document = Parse(path, text.Content);
		XElement rules = RequireSingleChild(document.Root, "rules", path);
		XElement[] loggers = rules.Elements().Where(element => element.Name.LocalName == "logger").ToArray();
		XElement defaultLogger = loggers.FirstOrDefault(element => (string)element.Attribute("name") == "*")
			?? throw new InvalidDataException($"Could not find the default logger (name='*') in '{path}'.");
		XElement[] existing = loggers.Where(element => string.Equals((string)element.Attribute("name"), loggerName,
			StringComparison.OrdinalIgnoreCase)).ToArray();
		if (existing.Length > 1 || existing.Length == 1 && !LoggerMatches(existing[0], loggerName, targetName, level)) {
			throw new InvalidDataException($"Logger '{loggerName}' already exists in '{path}' with conflicting or duplicate entries; no files were changed.");
		}
		if (existing.Length == 1) {
			if (Array.IndexOf(loggers, existing[0]) > Array.IndexOf(loggers, defaultLogger)) {
				throw new InvalidDataException($"Logger '{loggerName}' exists after the default catch-all logger in '{path}'.");
			}
			return FileEdit.Unchanged(path, text);
		}
		string element = $"<logger name=\"{loggerName}\" writeTo=\"{targetName}\" minlevel=\"{level}\" final=\"true\" />";
		return FileEdit.ChangedFile(path, text, InsertBeforeElement(text.Content, loggers[0], element));
	}

	private static FileEdit BuildTargetsEdit(string path, TextFile text, string targetName, string logPath) {
		XDocument document = Parse(path, text.Content);
		RequireVariable(document.Root, "DefaultLayout", path);
		RequireVariable(document.Root, "TodayLogPath", path);
		XElement targets = RequireSingleChild(document.Root, "targets", path);
		XElement[] existing = targets.Elements().Where(element => element.Name.LocalName == "target"
			&& string.Equals((string)element.Attribute("name"), targetName, StringComparison.OrdinalIgnoreCase)).ToArray();
		if (existing.Length > 1 || existing.Length == 1 && !TargetMatches(existing[0], targetName, logPath)) {
			throw new InvalidDataException($"Target '{targetName}' already exists in '{path}' with conflicting or duplicate entries; no files were changed.");
		}
		if (existing.Length == 1) {
			return FileEdit.Unchanged(path, text);
		}
		XElement anchor = targets.Elements().FirstOrDefault(element => element.Name.LocalName == "target")
			?? throw new InvalidDataException($"Could not find an existing target in '{path}'.");
		string prefix = document.Root.GetPrefixOfNamespace(XNamespace.Get(XsiNamespace));
		string declaration = string.IsNullOrWhiteSpace(prefix) ? $" xmlns:xsi=\"{XsiNamespace}\"" : string.Empty;
		prefix = string.IsNullOrWhiteSpace(prefix) ? "xsi" : prefix;
		string element = $"<target name=\"{targetName}\"{declaration} {prefix}:type=\"File\" layout=\"{DefaultLayout}\" fileName=\"{logPath}\" />";
		return FileEdit.ChangedFile(path, text, InsertBeforeElement(text.Content, anchor, element));
	}

	private string ApplyWithBackups(IReadOnlyList<FileEdit> edits, Action verify) {
		FileEdit[] changed = edits.Where(edit => edit.Changed).ToArray();
		if (changed.Length == 0) {
			return null;
		}
		List<Backup> backups = [];
		bool verified = false;
		try {
			CreateBackups(changed, backups);
			foreach (FileEdit edit in changed) {
				_fileSystem.File.WriteAllText(edit.Path, edit.UpdatedContent, edit.Encoding);
			}
			verify();
			verified = true;
			return null;
		}
		catch (Exception exception) when (IsExpectedFailure(exception)) {
			List<string> errors = RestoreBackups(backups);
			return errors.Count == 0
				? $"Failed to update NLog configuration: {exception.Message}. Original files were restored."
				: $"Failed to update NLog configuration: {exception.Message}. Rollback also failed: {string.Join("; ", errors)}";
		}
		finally {
			if (verified) {
				DeleteBackups(backups);
			}
		}
	}

	private void CreateBackups(IEnumerable<FileEdit> edits, ICollection<Backup> backups) {
		foreach (string path in edits.Select(edit => edit.Path)) {
			string backupPath = path + $".clio-{Guid.NewGuid():N}.bak";
			_fileSystem.File.Copy(path, backupPath);
			backups.Add(new(path, backupPath));
		}
	}

	private List<string> RestoreBackups(IEnumerable<Backup> backups) {
		List<string> errors = [];
		foreach (Backup backup in backups.Reverse()) {
			try {
				_fileSystem.File.WriteAllBytes(backup.OriginalPath, _fileSystem.File.ReadAllBytes(backup.BackupPath));
				TryDeleteBackup(backup.BackupPath);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
				errors.Add($"Could not restore '{backup.OriginalPath}' from '{backup.BackupPath}': {exception.Message}");
			}
		}
		return errors;
	}

	private void DeleteBackups(IEnumerable<Backup> backups) {
		foreach (Backup backup in backups) {
			TryDeleteBackup(backup.BackupPath);
		}
	}

	private void TryDeleteBackup(string path) {
		try {
			_fileSystem.File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// The configuration is already verified or restored; a stale backup is safer than reporting failure.
		}
	}

	private void VerifySavedConfiguration(string rulesPath, string targetsPath, string loggerName,
		string targetName, string level, string logPath) {
		XElement[] loggers = RequireSingleChild(Parse(rulesPath, _fileSystem.File.ReadAllText(rulesPath)).Root, "rules", rulesPath)
			.Elements().Where(element => element.Name.LocalName == "logger" && (string)element.Attribute("name") == loggerName).ToArray();
		XElement[] targets = RequireSingleChild(Parse(targetsPath, _fileSystem.File.ReadAllText(targetsPath)).Root, "targets", targetsPath)
			.Elements().Where(element => element.Name.LocalName == "target" && (string)element.Attribute("name") == targetName).ToArray();
		if (loggers.Length != 1 || !LoggerMatches(loggers[0], loggerName, targetName, level)
			|| targets.Length != 1 || !TargetMatches(targets[0], targetName, logPath)) {
			throw new InvalidDataException("The saved NLog configuration does not contain the expected logger and target.");
		}
	}

	private TextFile ReadTextFile(string path) {
		byte[] bytes = _fileSystem.File.ReadAllBytes(path);
		bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
		Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom, throwOnInvalidBytes: true);
		string content = encoding.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
		return new(content, encoding);
	}

	private static XDocument Parse(string path, string content) {
		XDocument document = XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
		return document.Root?.Name.LocalName == "nlog" ? document
			: throw new InvalidDataException($"'{path}' does not contain an nlog root element.");
	}

	private static XElement RequireSingleChild(XElement root, string name, string path) {
		XElement[] matches = root.Elements().Where(element => element.Name.LocalName == name).ToArray();
		return matches.Length == 1 ? matches[0]
			: throw new InvalidDataException($"Expected one '{name}' element in '{path}', found {matches.Length}.");
	}

	private static void RequireVariable(XElement root, string name, string path) {
		XElement[] variables = root.Elements().Where(element => element.Name.LocalName == "variable"
			&& (string)element.Attribute("name") == name).ToArray();
		if (variables.Length != 1 || string.IsNullOrWhiteSpace((string)variables[0].Attribute("value"))) {
			throw new InvalidDataException($"Expected one non-empty '{name}' variable in '{path}'.");
		}
	}

	private static bool LoggerMatches(XElement element, string loggerName, string targetName, string level) =>
		(string)element.Attribute("name") == loggerName && (string)element.Attribute("writeTo") == targetName
		&& string.Equals((string)element.Attribute("minlevel"), level, StringComparison.OrdinalIgnoreCase)
		&& string.Equals((string)element.Attribute("final"), "true", StringComparison.OrdinalIgnoreCase);

	private static bool TargetMatches(XElement element, string targetName, string logPath) =>
		(string)element.Attribute("name") == targetName
		&& string.Equals((string)element.Attribute(XName.Get("type", XsiNamespace)), "File", StringComparison.OrdinalIgnoreCase)
		&& (string)element.Attribute("layout") == DefaultLayout && (string)element.Attribute("fileName") == logPath;

	private static string InsertBeforeElement(string content, XElement anchor, string element) {
		if (anchor is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo()) {
			throw new InvalidDataException("Could not determine the XML insertion position.");
		}
		int lineStart = 0;
		for (int line = 1; line < lineInfo.LineNumber; line++) {
			int next = content.IndexOf('\n', lineStart);
			if (next < 0) { throw new InvalidDataException("XML line information does not match the source content."); }
			lineStart = next + 1;
		}
		int index = lineStart + lineInfo.LinePosition - 2;
		string indentation = content[lineStart..index];
		string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		string suffix = indentation.All(char.IsWhiteSpace) ? newline + indentation : string.Empty;
		string updated = content.Insert(index, element + suffix);
		Parse("updated NLog configuration", updated);
		return updated;
	}

	private static CustomLoggingConfigurationResult Failure(string message, string loggerName = null,
		string targetName = null, string logPath = null) => new(false, false, loggerName, targetName, logPath, message);
	private static bool IsExpectedFailure(Exception exception) => exception is IOException
		or UnauthorizedAccessException or XmlException or InvalidDataException or InvalidOperationException
		or ArgumentException or NotSupportedException or RegexMatchTimeoutException
		or System.Security.SecurityException;
	private sealed record TextFile(string Content, Encoding Encoding);
	private sealed record FileEdit(string Path, string UpdatedContent, Encoding Encoding, bool Changed) {
		public static FileEdit Unchanged(string path, TextFile text) => new(path, text.Content, text.Encoding, false);
		public static FileEdit ChangedFile(string path, TextFile text, string updated) => new(path, updated, text.Encoding, true);
	}
	private sealed record Backup(string OriginalPath, string BackupPath);
}
