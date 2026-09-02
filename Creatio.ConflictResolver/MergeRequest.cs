using System.Text.RegularExpressions;

namespace Creatio.ConflictResolver;

/// <summary>Describes one in-memory three-way merge request.</summary>
/// <param name="FileType">Detected Creatio artifact type.</param>
/// <param name="Base">Common ancestor content from Git stage 1.</param>
/// <param name="Local">Local content from Git stage 2.</param>
/// <param name="Remote">Remote content from Git stage 3.</param>
/// <param name="FilePath">Optional path used for diagnostics and artifact context.</param>
/// <param name="Mode">Conflict formatting mode.</param>
/// <param name="DescriptorContent">Optional resolved sibling descriptor content.</param>
public sealed record MergeRequest(
	ConflictFileType FileType,
	string Base,
	string Local,
	string Remote,
	string? FilePath = null,
	MergeMode Mode = MergeMode.Default,
	string? DescriptorContent = null)
{
	/// <summary>Creates a request whose artifact type is detected from its file path.</summary>
	/// <param name="Base">Common ancestor content from Git stage 1.</param>
	/// <param name="Local">Local content from Git stage 2.</param>
	/// <param name="Remote">Remote content from Git stage 3.</param>
	/// <param name="FilePath">Artifact path used for classification.</param>
	public MergeRequest(
		string Base,
		string Local,
		string Remote,
		string FilePath)
		: this(ParseFileTypeFromPath(FilePath), Base, Local, Remote, FilePath, MergeMode.Default)
	{
	}

	private static readonly Regex ProcessSchemaManagerRegex = new(
		"\"ManagerName\"\\s*:\\s*\"ProcessSchemaManager\"",
		RegexOptions.CultureInvariant | RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	private static readonly Func<string, string?> DefaultSiblingFileReader =
		static path => File.Exists(path) ? File.ReadAllText(path) : null;

	/// <summary>Detects an artifact type from a path, reading a sibling descriptor when required.</summary>
	/// <param name="filePath">Artifact path to classify.</param>
	/// <param name="fileType">Detected artifact type when successful.</param>
	/// <returns><c>true</c> when the path is a recognized Creatio artifact.</returns>
	public static bool TryDetectFileTypeFromPath(string? filePath, out ConflictFileType fileType)
	{
		return TryDetectFileTypeFromPath(filePath, DefaultSiblingFileReader, out fileType);
	}

	/// <summary>
	/// Detects an artifact type from its path while using caller-supplied sibling descriptor content.
	/// </summary>
	/// <param name="filePath">Artifact path used for classification only.</param>
	/// <param name="descriptorContent">Optional inline sibling descriptor content.</param>
	/// <param name="fileType">Detected artifact type when the method returns <c>true</c>.</param>
	/// <returns><c>true</c> when the path represents a recognized Creatio artifact.</returns>
	public static bool TryDetectFileTypeFromPath(
		string? filePath,
		string? descriptorContent,
		out ConflictFileType fileType)
	{
		return TryDetectFileTypeFromPath(filePath, _ => descriptorContent, out fileType);
	}

	internal static bool TryDetectFileTypeFromPath(
		string? filePath,
		Func<string, string?> siblingFileReader,
		out ConflictFileType fileType)
	{
		fileType = default;
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return false;
		}

		var path = filePath!;

		var fileName = Path.GetFileName(path);
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}

		if (IsDataBindingPath(fileName))
		{
			fileType = ConflictFileType.DataBinding;
			return true;
		}

		if (string.Equals(fileName, "properties.json", StringComparison.OrdinalIgnoreCase))
		{
			fileType = ConflictFileType.PropertiesJson;
			return true;
		}

		if (string.Equals(fileName, "descriptor.json", StringComparison.OrdinalIgnoreCase))
		{
			fileType = ConflictFileType.DescriptorJson;
			return true;
		}

		if (string.Equals(fileName, "metadata.json", StringComparison.OrdinalIgnoreCase))
		{
			fileType = IsProcessSchemaFolder(path, siblingFileReader)
				? ConflictFileType.ProcessMetadataJson
				: ConflictFileType.MetadataJson;
			return true;
		}

		if (IsResourcePath(fileName))
		{
			fileType = IsProcessResourceFolder(path)
				? ConflictFileType.ProcessResourceXml
				: ConflictFileType.ResourceXml;
			return true;
		}

		if (IsClientUnitPath(path, fileName))
		{
			fileType = ConflictFileType.ClientUnitJs;
			return true;
		}

		if (IsSqlScriptPath(fileName))
		{
			fileType = ConflictFileType.SqlScript;
			return true;
		}

		if (IsSourceCodePath(fileName))
		{
			fileType = ConflictFileType.SourceCode;
			return true;
		}

		return false;
	}

	private static ConflictFileType ParseFileTypeFromPath(string filePath)
	{
		if (TryDetectFileTypeFromPath(filePath, out var fileType))
		{
			return fileType;
		}

		throw new ArgumentException(
			$"Cannot detect file type from path '{filePath}'.",
			nameof(filePath));
	}

	private static bool IsDataBindingPath(string fileName)
	{
		const string prefix = "data.";
		const string suffix = ".json";

		if (string.Equals(fileName, "data.json", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
			!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var culturePartLength = fileName.Length - prefix.Length - suffix.Length;
		if (culturePartLength <= 0)
		{
			return false;
		}

		var culturePart = fileName.Substring(prefix.Length, culturePartLength);
		return !string.IsNullOrWhiteSpace(culturePart);
	}

	private static bool IsProcessSchemaFolder(string filePath, Func<string, string?> siblingFileReader)
	{
		var directory = GetDirectoryPortion(filePath);
		if (directory is null)
		{
			return false;
		}

		var descriptorPath = directory + "descriptor.json";
		var descriptorContent = siblingFileReader(descriptorPath);
		return !string.IsNullOrEmpty(descriptorContent) &&
		       ProcessSchemaManagerRegex.IsMatch(descriptorContent);
	}

	private static bool IsProcessResourceFolder(string filePath)
	{
		var folderName = GetParentFolderName(filePath);
		return folderName?.EndsWith(".Process", StringComparison.OrdinalIgnoreCase) == true;
	}

	private static string? GetDirectoryPortion(string filePath)
	{
		var normalizedPath = filePath.Replace('\\', '/');
		var lastSlash = normalizedPath.LastIndexOf('/');
		return lastSlash < 0 ? null : normalizedPath.Substring(0, lastSlash + 1);
	}

	private static string? GetParentFolderName(string filePath)
	{
		var normalizedPath = filePath.Replace('\\', '/');
		var lastSlash = normalizedPath.LastIndexOf('/');
		if (lastSlash <= 0)
		{
			return null;
		}

		var directory = normalizedPath.Substring(0, lastSlash);
		var parentSlash = directory.LastIndexOf('/');
		var folderName = parentSlash < 0 ? directory : directory.Substring(parentSlash + 1);
		return string.IsNullOrEmpty(folderName) ? null : folderName;
	}

	private static bool IsResourcePath(string fileName)
	{
		if (!fileName.StartsWith("resource.", StringComparison.OrdinalIgnoreCase) ||
			!fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var culturePartLength = fileName.Length - "resource.".Length - ".xml".Length;
		return culturePartLength > 0;
	}

	private static bool IsClientUnitPath(string filePath, string fileName)
	{
		if (!fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var normalizedPath = filePath.Replace('\\', '/');
		return normalizedPath.IndexOf("/Schemas/", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       normalizedPath.StartsWith("Schemas/", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsSqlScriptPath(string fileName)
	{
		return fileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsSourceCodePath(string fileName)
	{
		return fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
	}
}
