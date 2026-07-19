using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Clio.Command.McpServer.Knowledge;

internal sealed record KnowledgeGitRepositorySnapshot(
	string LibraryId,
	string LibraryVersion,
	ulong Sequence,
	string ContentDigest,
	IReadOnlyList<KnowledgeArticle> Articles);

internal interface IKnowledgeGitRepositoryReader {
	bool TryRead(
		string repositoryPath,
		string expectedLibraryId,
		out KnowledgeGitRepositorySnapshot? snapshot,
		out string? diagnostic);
}

internal sealed class KnowledgeGitRepositoryReader : IKnowledgeGitRepositoryReader {
	private const string ManifestFileName = "bundle-source.json";
	private const string ContractVersion = "1.0.0";
	private const string SchemaReference = "./schemas/v1/knowledge-repository.schema.json";
	private const int MaxManifestBytes = 1024 * 1024;
	private const int MaxResourceBytes = 4 * 1024 * 1024;
	private const int MaxBundleResourceBytes = 32 * 1024 * 1024;
	private const int MaxResources = 1022;
	private static readonly Regex StableIdPattern = new(
		"^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));
	private static readonly Regex LibraryIdPattern = new(
		"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?)+$",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));
	private static readonly Regex VersionPattern = new(
		"^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));
	private static readonly UTF8Encoding StrictUtf8 = new(false, true);
	private static readonly IReadOnlyDictionary<string, string> SourceRootByRole =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["guidance"] = "guidance/",
			["advisory"] = "advisories/",
			["capability"] = "capabilities/",
			["reference-example"] = "catalog/"
	};
	private readonly IFileSystem _fileSystem;
	private readonly KnowledgeBundleClientCapabilities _capabilities;

	public KnowledgeGitRepositoryReader(
		IFileSystem fileSystem,
		KnowledgeBundleClientCapabilities capabilities) {
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
	}

	public bool TryRead(
		string repositoryPath,
		string expectedLibraryId,
		out KnowledgeGitRepositorySnapshot? snapshot,
		out string? diagnostic) {
		snapshot = null;
		diagnostic = null;
		try {
			string root = _fileSystem.Path.GetFullPath(repositoryPath);
			EnsureNoReparsePoints(root, root);
			string manifestPath = ResolveChild(root, ManifestFileName);
			byte[] manifestBytes = ReadBounded(root, manifestPath, MaxManifestBytes);
			RejectInvalidJson(manifestBytes);
			KnowledgeGitRepositoryManifest manifest = JsonConvert.DeserializeObject<KnowledgeGitRepositoryManifest>(
				StrictUtf8.GetString(manifestBytes),
				new JsonSerializerSettings {
					MissingMemberHandling = MissingMemberHandling.Error,
					DateParseHandling = DateParseHandling.None,
					MaxDepth = 64
				}) ?? throw new InvalidDataException("Git knowledge manifest is empty.");
			ValidateEnvelope(manifest, expectedLibraryId);
			ValidateCompatibility(manifest);
			ValidateRequirements(manifest);
			ValidateResourceSet(manifest);

			List<KnowledgeArticle> articles = [];
			long totalResourceBytes = 0;
			using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			AppendFramed(digest, manifestBytes);
			foreach (KnowledgeGitRepositoryResource resource in manifest.Resources!) {
				ValidateResourceDescriptor(manifest, resource);
				string path = ResolveRepositoryPath(root, resource.SourcePath);
				byte[] content = ReadBounded(root, path, MaxResourceBytes);
				totalResourceBytes = checked(totalResourceBytes + content.LongLength);
				if (totalResourceBytes > MaxBundleResourceBytes) {
					throw new InvalidDataException(
						$"Git knowledge resources exceed the {MaxBundleResourceBytes}-byte total size limit.");
				}
				AppendFramed(digest, content);
				articles.Add(new KnowledgeArticle(
					resource.ItemId,
					resource.Uri,
					StrictUtf8.GetString(content),
					manifest.LibraryId,
					resource.ItemId,
					resource.TopicId,
					resource.Role,
					path,
					resource.LegacyUris ?? []));
			}
			snapshot = new KnowledgeGitRepositorySnapshot(
				manifest.LibraryId,
				manifest.LibraryVersion,
				manifest.Sequence,
				Convert.ToHexString(digest.GetHashAndReset()),
				articles);
			return true;
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidDataException
				or Newtonsoft.Json.JsonException
				or DecoderFallbackException
				or ArgumentException
				or OverflowException) {
			diagnostic = exception.Message;
			return false;
		}
	}

	private static void AppendFramed(IncrementalHash digest, ReadOnlySpan<byte> content) {
		Span<byte> length = stackalloc byte[sizeof(long)];
		BinaryPrimitives.WriteInt64LittleEndian(length, content.Length);
		digest.AppendData(length);
		digest.AppendData(content);
	}

	private static void RejectInvalidJson(ReadOnlySpan<byte> json) {
		Utf8JsonReader reader = new(json, new JsonReaderOptions {
			AllowTrailingCommas = false,
			CommentHandling = JsonCommentHandling.Disallow,
			MaxDepth = 64
		});
		Stack<HashSet<string>> objectProperties = new();
		try {
			while (reader.Read()) {
				switch (reader.TokenType) {
					case JsonTokenType.StartObject:
						objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
						break;
					case JsonTokenType.EndObject:
						objectProperties.Pop();
						break;
					case JsonTokenType.PropertyName:
						string propertyName = reader.GetString()!;
						if (!objectProperties.Peek().Add(propertyName)) {
							throw new InvalidDataException(
								$"Git knowledge manifest contains duplicate JSON property '{propertyName}'.");
						}
						break;
				}
			}
		} catch (System.Text.Json.JsonException exception) {
			throw new InvalidDataException("Git knowledge manifest is not strict JSON.", exception);
		}
	}

	private static void ValidateEnvelope(
		KnowledgeGitRepositoryManifest manifest,
		string expectedLibraryId) {
		if (!string.Equals(manifest.Schema, SchemaReference, StringComparison.Ordinal)
				|| !string.Equals(manifest.ContractVersion, ContractVersion, StringComparison.Ordinal)
				|| !string.Equals(manifest.BundleSchemaVersion, ContractVersion, StringComparison.Ordinal)
				|| !ValidLibraryId(manifest.LibraryId)
				|| !string.Equals(manifest.LibraryId, expectedLibraryId, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(manifest.LibraryVersion)
				|| manifest.LibraryVersion.Length > 128
				|| manifest.Sequence == 0
				|| manifest.Compatibility is null
				|| manifest.Requirements is null
				|| manifest.Resources is null
				|| manifest.Resources.Count == 0
				|| manifest.Resources.Count > MaxResources
				|| manifest.Resources.Any(resource => resource is null)) {
			throw new InvalidDataException(
				"Git knowledge manifest identity or required envelope is invalid or does not match the configured library.");
		}
	}

	private void ValidateCompatibility(KnowledgeGitRepositoryManifest manifest) {
		if (manifest.Compatibility!.Clio is null
				|| manifest.Compatibility.McpToolContract is null
				|| !IsCompatible(manifest.Compatibility.Clio, _capabilities.ClioVersion)
				|| !IsCompatible(manifest.Compatibility.McpToolContract, _capabilities.McpToolContractVersion)) {
			throw new InvalidDataException("Git knowledge compatibility ranges do not include this Clio runtime.");
		}
	}

	private void ValidateRequirements(KnowledgeGitRepositoryManifest manifest) {
		KnowledgeGitRepositoryRequirements requirements = manifest.Requirements!;
		if (requirements.Tools is null
				|| requirements.ItemIds is null
				|| requirements.ResourceUris is null) {
			throw new InvalidDataException("Git knowledge manifest requirements are incomplete.");
		}
		EnsureUnique(requirements.Tools, "required tool");
		EnsureUnique(requirements.ItemIds, "required item id");
		EnsureUnique(requirements.ResourceUris, "required resource URI");
		if (requirements.Tools.Any(tool => !_capabilities.Tools.Contains(tool))) {
			throw new InvalidDataException("Git knowledge repository requires an unavailable MCP tool capability.");
		}
		if (requirements.ItemIds.Any(itemId => !ValidStableId(itemId))
				|| requirements.ResourceUris.Any(uri => !uri.StartsWith("docs://knowledge/", StringComparison.Ordinal))) {
			throw new InvalidDataException("Git knowledge manifest requirements contain an invalid item id or resource URI.");
		}
	}

	private static void ValidateResourceSet(KnowledgeGitRepositoryManifest manifest) {
		IReadOnlyList<KnowledgeGitRepositoryResource> resources = manifest.Resources!;
		EnsureUnique(resources.Select(resource => resource.ItemId), "resource item id");
		EnsureUnique(resources.Select(resource => resource.Uri), "resource URI");
		EnsureUnique(resources.Select(resource => resource.SourcePath), "resource source path");
		EnsureUnique(resources.Select(resource => resource.BundlePath), "resource bundle path");
		EnsureUnique(resources.Select(resource => $"{resource.TopicId}\0{resource.Role}"), "resource topic and role");
		string[] legacyUris = resources.SelectMany(resource => resource.LegacyUris ?? []).ToArray();
		EnsureUnique(legacyUris, "legacy resource URI");
		HashSet<string> itemIds = resources.Select(resource => resource.ItemId).ToHashSet(StringComparer.Ordinal);
		HashSet<string> uris = resources.Select(resource => resource.Uri).ToHashSet(StringComparer.Ordinal);
		HashSet<string> canonicalUris = new(uris, StringComparer.Ordinal);
		if (!itemIds.SetEquals(manifest.Requirements!.ItemIds!)
				|| !uris.SetEquals(manifest.Requirements.ResourceUris!)
				|| legacyUris.Any(canonicalUris.Contains)) {
			throw new InvalidDataException(
				"Declared requirements, canonical URIs, legacy URIs, and Git resources are inconsistent.");
		}
	}

	private static void ValidateResourceDescriptor(
		KnowledgeGitRepositoryManifest manifest,
		KnowledgeGitRepositoryResource resource) {
		string canonicalUri = $"docs://knowledge/{manifest.LibraryId}/{resource.ItemId}";
		if (!ValidStableId(resource.ItemId)
				|| !ValidStableId(resource.TopicId)
				|| !ValidRoleSourcePath(resource.Role, resource.SourcePath)
				|| !string.Equals(resource.Uri, canonicalUri, StringComparison.Ordinal)
				|| !ValidBundlePath(resource.BundlePath)
				|| string.IsNullOrWhiteSpace(resource.MediaType)
				|| !resource.MediaType.StartsWith("text/", StringComparison.Ordinal)
				|| (resource.LegacyUris?.Any(uri => string.IsNullOrWhiteSpace(uri)
					|| !uri.StartsWith("docs://", StringComparison.Ordinal)) ?? false)) {
			throw new InvalidDataException($"Git knowledge resource '{resource.ItemId}' has an invalid descriptor.");
		}
	}

	private static bool ValidStableId(string value) =>
		value is not null && value.Length is >= 1 and <= 160 && StableIdPattern.IsMatch(value);

	private static bool ValidLibraryId(string value) =>
		value is not null && value.Length is >= 3 and <= 255 && LibraryIdPattern.IsMatch(value);

	private static bool ValidRoleSourcePath(string role, string sourcePath) =>
		role is not null
		&& SourceRootByRole.TryGetValue(role, out string? expectedRoot)
		&& ValidRepositoryPath(sourcePath)
		&& sourcePath.StartsWith(expectedRoot, StringComparison.Ordinal);

	private static bool ValidBundlePath(string value) =>
		ValidRepositoryPath(value) && value.StartsWith("resources/", StringComparison.Ordinal);

	private static bool ValidRepositoryPath(string value) =>
		!string.IsNullOrWhiteSpace(value)
		&& value.Length <= 512
		&& !value.Contains("..", StringComparison.Ordinal)
		&& !value.Contains('\\')
		&& !value.StartsWith("/", StringComparison.Ordinal);

	private static bool IsCompatible(KnowledgeGitRepositoryVersionRange range, Version current) {
		if (!TryParseExactVersion(range.Min, out Version? min)
				|| !TryParseExactVersion(range.Max, out Version? max)
				|| min > max) {
			return false;
		}
		Version normalizedCurrent = new(current.Major, current.Minor, Math.Max(current.Build, 0));
		return normalizedCurrent >= min && normalizedCurrent <= max;
	}

	private static bool TryParseExactVersion(string value, out Version? version) {
		version = null;
		return value is not null
			&& value.Length <= 32
			&& VersionPattern.IsMatch(value)
			&& Version.TryParse(value, out version);
	}

	private static void EnsureUnique(IEnumerable<string> values, string label) {
		HashSet<string> unique = new(StringComparer.Ordinal);
		foreach (string value in values) {
			if (string.IsNullOrWhiteSpace(value) || !unique.Add(value)) {
				throw new InvalidDataException($"Every Git knowledge {label} must be non-empty and unique.");
			}
		}
	}

	private byte[] ReadBounded(string root, string path, int maximumBytes) {
		EnsureNoReparsePoints(root, path);
		using Stream input = _fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length <= 0 || input.Length > maximumBytes) {
			throw new InvalidDataException($"Git knowledge file '{_fileSystem.Path.GetFileName(path)}' is outside supported bounds.");
		}
		byte[] bytes = new byte[checked((int)input.Length)];
		input.ReadExactly(bytes);
		return bytes;
	}

	private string ResolveRepositoryPath(string root, string? relativePath) {
		if (!ValidRepositoryPath(relativePath!)) {
			throw new InvalidDataException("Git knowledge resource path must be repository relative.");
		}
		return ResolveChild(root, relativePath!);
	}

	private string ResolveChild(string root, string relativePath) {
		string fullPath = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(root, relativePath));
		string prefix = root.TrimEnd(_fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar)
			+ _fileSystem.Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(prefix, PathComparison)) {
			throw new InvalidDataException("Git knowledge path escapes the repository root.");
		}
		return fullPath;
	}

	private void EnsureNoReparsePoints(string root, string target) {
		string relative = _fileSystem.Path.GetRelativePath(root, target);
		if (relative.StartsWith("..", StringComparison.Ordinal)
				|| _fileSystem.Path.IsPathRooted(relative)) {
			throw new InvalidDataException("Git knowledge path escapes the repository root.");
		}
		RejectReparsePoint(root);
		if (relative == ".") {
			return;
		}
		string current = root;
		foreach (string segment in relative.Split(
				[_fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar],
				StringSplitOptions.RemoveEmptyEntries)) {
			current = _fileSystem.Path.Combine(current, segment);
			RejectReparsePoint(current);
		}
	}

	private void RejectReparsePoint(string path) {
		FileAttributes attributes = _fileSystem.File.GetAttributes(path);
		if ((attributes & FileAttributes.ReparsePoint) != 0) {
			throw new InvalidDataException(
				$"Git knowledge path '{_fileSystem.Path.GetFileName(path)}' must not contain a symbolic link or reparse point.");
		}
	}

	private static StringComparison PathComparison => OperatingSystem.IsWindows()
		? StringComparison.OrdinalIgnoreCase
		: StringComparison.Ordinal;
}

internal sealed class KnowledgeGitRepositoryManifest {
	[JsonProperty("$schema")]
	public string Schema { get; init; } = string.Empty;

	[JsonProperty("contractVersion")]
	public string ContractVersion { get; init; } = string.Empty;

	[JsonProperty("bundleSchemaVersion")]
	public string BundleSchemaVersion { get; init; } = string.Empty;

	[JsonProperty("libraryId")]
	public string LibraryId { get; init; } = string.Empty;

	[JsonProperty("libraryVersion")]
	public string LibraryVersion { get; init; } = string.Empty;

	[JsonProperty("sequence")]
	public ulong Sequence { get; init; }

	[JsonProperty("compatibility")]
	public KnowledgeGitRepositoryCompatibility? Compatibility { get; init; }

	[JsonProperty("requirements")]
	public KnowledgeGitRepositoryRequirements? Requirements { get; init; }

	[JsonProperty("resources")]
	public List<KnowledgeGitRepositoryResource>? Resources { get; init; }
}

internal sealed class KnowledgeGitRepositoryCompatibility {
	[JsonProperty("clio")]
	public KnowledgeGitRepositoryVersionRange? Clio { get; init; }

	[JsonProperty("mcpToolContract")]
	public KnowledgeGitRepositoryVersionRange? McpToolContract { get; init; }
}

internal sealed class KnowledgeGitRepositoryVersionRange {
	[JsonProperty("min")]
	public string Min { get; init; } = string.Empty;

	[JsonProperty("max")]
	public string Max { get; init; } = string.Empty;
}

internal sealed class KnowledgeGitRepositoryRequirements {
	[JsonProperty("tools")]
	public List<string>? Tools { get; init; }

	[JsonProperty("itemIds")]
	public List<string>? ItemIds { get; init; }

	[JsonProperty("resourceUris")]
	public List<string>? ResourceUris { get; init; }
}

internal sealed class KnowledgeGitRepositoryResource {
	[JsonProperty("itemId")]
	public string ItemId { get; init; } = string.Empty;

	[JsonProperty("topicId")]
	public string TopicId { get; init; } = string.Empty;

	[JsonProperty("role")]
	public string Role { get; init; } = string.Empty;

	[JsonProperty("uri")]
	public string Uri { get; init; } = string.Empty;

	[JsonProperty("legacyUris")]
	public string[]? LegacyUris { get; init; }

	[JsonProperty("sourcePath")]
	public string SourcePath { get; init; } = string.Empty;

	[JsonProperty("bundlePath")]
	public string BundlePath { get; init; } = string.Empty;

	[JsonProperty("mediaType")]
	public string MediaType { get; init; } = string.Empty;
}
