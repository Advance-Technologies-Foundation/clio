using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Resolver = Creatio.ConflictResolver;

namespace Clio.Command;

/// <summary>
/// Semantically merges inline Creatio package artifacts without repository or filesystem access.
/// </summary>
public interface ICreatioArtifactMergeService {
	/// <summary>
	/// Classifies and merges one artifact from its inline Git stage contents.
	/// </summary>
	/// <param name="args">Inline merge inputs.</param>
	/// <param name="cancellationToken">Cancels the merge before resolver work starts.</param>
	/// <returns>A domain-status response whose content is safe to consider only for resolved statuses.</returns>
	Task<CreatioArtifactMergeResult> MergeAsync(
		CreatioArtifactMergeArgs args,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Default in-memory implementation of <see cref="ICreatioArtifactMergeService"/>.
/// </summary>
public sealed partial class CreatioArtifactMergeService(Resolver.IConflictResolver resolver)
		: ICreatioArtifactMergeService {

	private const int MaxConcurrentMerges = 4;
	private const int MaxArtifactPathBytes = 4096;
	private const string UnknownArtifactKind = "unknown-artifact";
	private static readonly SemaphoreSlim Capacity = new(MaxConcurrentMerges, MaxConcurrentMerges);
	private static readonly string ResolverVersion = ResolveVersion();
	private static readonly JsonSerializerOptions ResultJsonOptions = BindingsModule.CreateMcpSerializerOptions();

	/// <inheritdoc />
	public async Task<CreatioArtifactMergeResult> MergeAsync(
		CreatioArtifactMergeArgs args,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(args);

		CreatioArtifactMergeResult? validationResult = ValidateRequest(args);
		if (validationResult is not null) {
			return validationResult;
		}

		Classification? pathOnlyTerminal = ClassifyPathOnlyTerminal(args.ArtifactPath);
		if (pathOnlyTerminal is { } terminal) {
			return Result(
				terminal.TerminalStatus!,
				terminal.ArtifactKind,
				terminal.Diagnostic);
		}

		if (!await Capacity.WaitAsync(0, cancellationToken).ConfigureAwait(false)) {
			return Result(
				CreatioArtifactMergeResult.BusyStatus,
				UnknownArtifactKind,
				"Merge capacity is busy; retry.");
		}

		try {
			Classification classification = Classify(args);
			if (classification.TerminalStatus is not null) {
				return Result(
					classification.TerminalStatus,
					classification.ArtifactKind,
					classification.Diagnostic);
			}

			Resolver.MergeRequest request = new(
				classification.FileType!.Value,
				args.BaseContent,
				args.OursContent,
				args.TheirsContent,
				args.ArtifactPath,
				Resolver.MergeMode.Automerge,
				args.DescriptorContent);
			Resolver.MergeResult mergeResult = resolver.Resolve(request);
			return MapResolverResult(classification.ArtifactKind, mergeResult, args);
		}
		finally {
			Capacity.Release();
		}
	}

	private static CreatioArtifactMergeResult? ValidateRequest(CreatioArtifactMergeArgs args) {
		if (Utf8ByteCount(args.ArtifactPath) > MaxArtifactPathBytes) {
			return Result(CreatioArtifactMergeResult.InvalidInputStatus, UnknownArtifactKind, "artifact-path exceeds the 4096-byte limit.");
		}
		if (!IsSafeRelativePath(args.ArtifactPath)) {
			return Result(CreatioArtifactMergeResult.InvalidInputStatus, UnknownArtifactKind, "artifact-path must be a safe repository-relative path.");
		}

		if (string.IsNullOrWhiteSpace(args.BaseContent) ||
		    string.IsNullOrWhiteSpace(args.OursContent) ||
		    string.IsNullOrWhiteSpace(args.TheirsContent)) {
			return Result(CreatioArtifactMergeResult.InvalidInputStatus, UnknownArtifactKind, "base-content, ours-content, and theirs-content are required.");
		}

		long totalBytes = Utf8ByteCount(args.ArtifactPath) +
		                  Utf8ByteCount(args.BaseContent) +
		                  Utf8ByteCount(args.OursContent) +
		                  Utf8ByteCount(args.TheirsContent) +
		                  Utf8ByteCount(args.DescriptorContent);
		return totalBytes > CreatioArtifactMergeArgs.MaxCombinedContentBytes
			? Result(CreatioArtifactMergeResult.InvalidInputStatus, UnknownArtifactKind, "Combined merge content exceeds the 4 MiB limit.")
			: null;
	}

	private static Classification Classify(CreatioArtifactMergeArgs args) {
		if (!Resolver.MergeRequest.TryDetectFileTypeFromPath(
			    args.ArtifactPath,
			    args.DescriptorContent,
			    out Resolver.ConflictFileType fileType)) {
			return Classification.Terminal(UnknownArtifactKind, "unsupported", "The artifact path is not a supported Creatio merge shape.");
		}

		return fileType switch {
			Resolver.ConflictFileType.MetadataJson => ClassifyMetadata(args),
			Resolver.ConflictFileType.ProcessMetadataJson =>
				Classification.Terminal("process-schema-metadata", "not-implemented", NotImplemented("process-schema-metadata")),
			Resolver.ConflictFileType.DescriptorJson => ClassifyDescriptor(args),
			Resolver.ConflictFileType.PropertiesJson => Classification.Semantic("properties", fileType),
			Resolver.ConflictFileType.DataBinding when TryParseDescriptor(args.DescriptorContent, out _) =>
				Classification.Semantic("data-binding", fileType),
			Resolver.ConflictFileType.DataBinding => Classification.Terminal(
				"data-binding",
				"invalid-input",
				"A valid, marker-free descriptor-content is required for data-binding merge."),
			Resolver.ConflictFileType.ResourceXml => Classification.Semantic("resource", fileType),
			Resolver.ConflictFileType.ProcessResourceXml =>
				Classification.Terminal("process-resource", "not-implemented", NotImplemented("process-resource")),
			Resolver.ConflictFileType.ClientUnitJs => Classification.Semantic("client-unit-source", fileType),
			Resolver.ConflictFileType.SourceCode =>
				Classification.Terminal("csharp-source", "not-implemented", NotImplemented("csharp-source")),
			Resolver.ConflictFileType.SqlScript =>
				Classification.Terminal("sql-script", "not-implemented", NotImplemented("sql-script")),
			_ => Classification.Terminal(UnknownArtifactKind, "unsupported", "The artifact path is not a supported Creatio merge shape.")
		};
	}

	private static Classification ClassifyDescriptor(CreatioArtifactMergeArgs args) {
		string[] contents = [args.BaseContent, args.OursContent, args.TheirsContent];
		bool isProcessDescriptor = contents.Any(content =>
			TryParseDescriptor(content, out DescriptorIdentity identity) &&
			string.Equals(identity.ManagerName, "ProcessSchemaManager", StringComparison.Ordinal));
		return isProcessDescriptor
			? Classification.Terminal(
				"process-schema-descriptor",
				"not-implemented",
				NotImplemented("process-schema-descriptor"))
			: Classification.Semantic("descriptor", Resolver.ConflictFileType.DescriptorJson);
	}

	private static Classification ClassifyMetadata(CreatioArtifactMergeArgs args) {
		if (!TryParseDescriptor(args.DescriptorContent, out DescriptorIdentity descriptor)) {
			return Classification.Terminal("unknown-schema-metadata", "invalid-input", "A valid, marker-free descriptor-content is required for metadata merge.");
		}

		MetadataIdentity[] identities = [
			ParseMetadataIdentity(args.BaseContent),
			ParseMetadataIdentity(args.OursContent),
			ParseMetadataIdentity(args.TheirsContent)
		];
		if (identities.Any(identity => !identity.IsValid || !identity.Matches(descriptor))) {
			return Classification.Terminal("unknown-schema-metadata", "invalid-input", "Metadata identity does not match descriptor-content.");
		}

		return descriptor.ManagerName switch {
			"EntitySchemaManager" => Classification.Semantic("entity-schema-metadata", Resolver.ConflictFileType.MetadataJson),
			"ClientUnitSchemaManager" => Classification.Semantic("client-unit-metadata", Resolver.ConflictFileType.MetadataJson),
			"ServiceSchemaManager" => Classification.Semantic("service-schema-metadata", Resolver.ConflictFileType.MetadataJson),
			"ProcessSchemaManager" => Classification.Terminal("process-schema-metadata", "not-implemented", NotImplemented("process-schema-metadata")),
			"AddonSchemaManager" => ClassifyAddon(identities),
			_ => Classification.Terminal("unknown-schema-metadata", "unsupported", "Merge for unknown-schema-metadata is unsupported.")
		};
	}

	private static Classification ClassifyAddon(IReadOnlyList<MetadataIdentity> identities) {
		string? subtype = identities[0].Subtype;
		if (string.IsNullOrWhiteSpace(subtype) || identities.Any(identity => !string.Equals(identity.Subtype, subtype, StringComparison.Ordinal))) {
			return Classification.Terminal("unknown-schema-metadata", "invalid-input", "Addon metadata subtype does not match across merge inputs.");
		}

		return subtype switch {
			"AppearanceSettings" => Classification.Semantic("addon-appearance-settings-metadata", Resolver.ConflictFileType.MetadataJson),
			"BusinessRule" => Classification.Semantic("addon-business-rule-metadata", Resolver.ConflictFileType.MetadataJson),
			"RelatedPage" => Classification.Semantic("addon-related-page-metadata", Resolver.ConflictFileType.MetadataJson),
			"TimelineEntity" => Classification.Semantic("addon-timeline-entity-metadata", Resolver.ConflictFileType.MetadataJson),
			_ => Classification.Terminal("unknown-schema-metadata", "unsupported", "Merge for unknown-schema-metadata is unsupported.")
		};
	}

	private static CreatioArtifactMergeResult MapResolverResult(
		string artifactKind,
		Resolver.MergeResult mergeResult,
		CreatioArtifactMergeArgs args) {
		CreatioArtifactMergeReport report = ProjectReport(mergeResult.Report);
		string? content = mergeResult.MergedContent;
		if (string.Equals(mergeResult.ErrorCode, "ClientUnitMarkersMissing", StringComparison.Ordinal)) {
			return Result(
				CreatioArtifactMergeResult.UnsupportedStatus,
				"unsupported-client-unit-source",
				"Merge for unsupported-client-unit-source is unsupported.",
				report);
		}
		CreatioArtifactMergeResult candidate = mergeResult.Status switch {
			Resolver.MergeStatus.Resolved when report.VerificationPassed &&
			                                   !string.IsNullOrEmpty(content) &&
			                                   !ContainsAnyConflictMarker(content) =>
				Result(CreatioArtifactMergeResult.ResolvedStatus, artifactKind, null, report, content),
			Resolver.MergeStatus.AutoResolvedWithConflicts when !string.IsNullOrEmpty(content) &&
			                                                        HasConflictMarkers(content) =>
				ConflictResult(artifactKind, report, content, args),
			Resolver.MergeStatus.UnresolvedConflict when !string.IsNullOrEmpty(content) &&
			                                               HasConflictMarkers(content) =>
				ConflictResult(artifactKind, report, content, args),
			Resolver.MergeStatus.UnsupportedType =>
				Result(CreatioArtifactMergeResult.UnsupportedStatus, artifactKind, $"Merge for {artifactKind} is unsupported.", report),
			Resolver.MergeStatus.InvalidInput =>
				Result(CreatioArtifactMergeResult.InvalidInputStatus, artifactKind, "Resolver rejected the merge input.", report),
			_ => Result(CreatioArtifactMergeResult.InvalidInputStatus, artifactKind, "Resolver did not produce a safe semantic result.", report)
		};
		return IsSerializedResultWithinLimit(candidate)
			? candidate
			: Result(CreatioArtifactMergeResult.InvalidInputStatus, artifactKind, "Resolver output exceeds the 4 MiB limit.");
	}

	private static bool TryParseDescriptor(
		string? content,
		out DescriptorIdentity identity) {
		identity = default;
		if (string.IsNullOrWhiteSpace(content) || ContainsAnyConflictMarker(content)) {
			return false;
		}

		try {
			JsonNode? root = JsonNode.Parse(content.TrimStart('\uFEFF'));
			JsonObject? descriptor = root?["Descriptor"] as JsonObject;
			if (descriptor is null) {
				return false;
			}

			identity = new DescriptorIdentity(
				ReadString(descriptor, "UId"),
				ReadString(descriptor, "Name"),
				ReadString(descriptor, "ManagerName"));
			return true;
		}
		catch (JsonException) {
			return false;
		}
	}

	private static Classification? ClassifyPathOnlyTerminal(string artifactPath) {
		string normalized = artifactPath.Replace('\\', '/');
		string fileName = normalized.Split('/').Last();
		if (fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) {
			return Classification.Terminal("csharp-source", CreatioArtifactMergeResult.NotImplementedStatus, NotImplemented("csharp-source"));
		}
		if (fileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) {
			return Classification.Terminal("sql-script", CreatioArtifactMergeResult.NotImplementedStatus, NotImplemented("sql-script"));
		}

		string parent = normalized.Contains('/')
			? normalized[..normalized.LastIndexOf('/')].Split('/').Last()
			: string.Empty;
		if (parent.EndsWith(".Process", StringComparison.OrdinalIgnoreCase)) {
			if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) {
				return Classification.Terminal("process-resource", CreatioArtifactMergeResult.NotImplementedStatus, NotImplemented("process-resource"));
			}
			if (string.Equals(fileName, "metadata.json", StringComparison.OrdinalIgnoreCase)) {
				return Classification.Terminal("process-schema-metadata", CreatioArtifactMergeResult.NotImplementedStatus, NotImplemented("process-schema-metadata"));
			}
		}

		return null;
	}

	private static bool IsSerializedResultWithinLimit(CreatioArtifactMergeResult result) {
		try {
			var buffer = new CappedBufferWriter(CreatioArtifactMergeArgs.MaxCombinedContentBytes);
			using var writer = new Utf8JsonWriter(buffer);
			JsonSerializer.Serialize(writer, result, ResultJsonOptions);
			return true;
		}
		catch (SerializedResultLimitExceededException) {
			return false;
		}
	}

	private static MetadataIdentity ParseMetadataIdentity(string content) {
		try {
			if (JsonNode.Parse(content.TrimStart('\uFEFF'))?["MetaData"]?["Schema"] is JsonObject schema) {
				return new MetadataIdentity(
					ReadString(schema, "UId"),
					ReadString(schema, "A2"),
					ReadString(schema, "ManagerName"),
					ReadString(schema, "AD3"));
			}
		}
		catch (JsonException) {
			// Creatio also stores metadata in flat-diff syntax; parse its stable identity assignments below.
		}

		return new MetadataIdentity(
			ReadFlatValue(content, "UId"),
			ReadFlatValue(content, "A2"),
			ReadFlatValue(content, "ManagerName"),
			ReadFlatValue(content, "AD3"));
	}

	private static string? ReadFlatValue(string content, string propertyName) {
		Match match = FlatIdentityRegex().Match(content);
		while (match.Success) {
			if (string.Equals(match.Groups["property"].Value, propertyName, StringComparison.Ordinal)) {
				return match.Groups["value"].Value;
			}
			match = match.NextMatch();
		}
		return null;
	}

	private static string? ReadString(JsonObject source, string propertyName) {
		return source[propertyName] is JsonValue value && value.TryGetValue(out string? result)
			? result
			: null;
	}

	private static bool IsSafeRelativePath(string? path) {
		if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) {
			return false;
		}
		string normalized = path.Replace('\\', '/');
		if (normalized.StartsWith("/", StringComparison.Ordinal) ||
		    (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')) {
			return false;
		}

		string[] segments = normalized.Split('/');
		return segments.All(segment => !string.IsNullOrWhiteSpace(segment) && segment is not "." and not "..");
	}

	private static bool HasConflictMarkers(string content) {
		return content.Contains("<<<<<<<", StringComparison.Ordinal) &&
		       content.Contains("=======", StringComparison.Ordinal) &&
		       content.Contains(">>>>>>>", StringComparison.Ordinal);
	}

	private static bool ContainsAnyConflictMarker(string content) {
		return content.Contains("<<<<<<<", StringComparison.Ordinal) ||
		       content.Contains("=======", StringComparison.Ordinal) ||
		       content.Contains(">>>>>>>", StringComparison.Ordinal);
	}

	private static long Utf8ByteCount(string? content) {
		return content is null ? 0 : Encoding.UTF8.GetByteCount(content);
	}

	private static string NotImplemented(string artifactKind) {
		return $"Merge for {artifactKind} is not implemented yet.";
	}

	private static CreatioArtifactMergeReport ProjectReport(Resolver.MergeReport report) {
		return new CreatioArtifactMergeReport(
			report.ResolutionType,
			report.WinnerPolicy,
			report.VerificationPassed,
			report.LocalAdditions,
			report.RemoteAdditions,
			report.LocalDeletions,
			report.RemoteDeletions,
			report.TrueConflicts);
	}

	private static CreatioArtifactMergeResult ConflictResult(
		string artifactKind,
		CreatioArtifactMergeReport report,
		string content,
		CreatioArtifactMergeArgs args) {
		List<string> diagnostics = ["Semantic conflicts remain in marker content."];
		if (string.Equals(artifactKind, "entity-schema-metadata", StringComparison.Ordinal)) {
			foreach (Match match in EntityColumnTypeConflictRegex().Matches(content)) {
				if (TryGetTypeDisplayName(match.Groups["local"].Value, out string localType) &&
				    TryGetTypeDisplayName(match.Groups["remote"].Value, out string remoteType)) {
					diagnostics.Add(
						$"Which type should {match.Groups["column"].Value} keep: {localType} or {remoteType}?");
				}
			}

			AppendJsonEntityColumnTypeQuestions(report, args, diagnostics);
		}
		return new CreatioArtifactMergeResult(
			CreatioArtifactMergeResult.ConflictsRemainStatus,
			artifactKind,
			ResolverVersion,
			content,
			report,
			diagnostics);
	}

	private static void AppendJsonEntityColumnTypeQuestions(
		CreatioArtifactMergeReport report,
		CreatioArtifactMergeArgs args,
		ICollection<string> diagnostics) {
		if (!report.TrueConflicts.Any(static path => path.EndsWith(".S2", StringComparison.Ordinal)) ||
		    !TryReadEntityColumns(args.BaseContent, out var baseColumns) ||
		    !TryReadEntityColumns(args.OursContent, out var oursColumns) ||
		    !TryReadEntityColumns(args.TheirsContent, out var theirsColumns)) {
			return;
		}

		foreach ((string key, EntityColumn ours) in oursColumns) {
			if (!theirsColumns.TryGetValue(key, out EntityColumn theirs) ||
			    string.Equals(ours.TypeUId, theirs.TypeUId, StringComparison.OrdinalIgnoreCase) ||
			    !SafeColumnNameRegex().IsMatch(ours.Name) ||
			    !TryGetTypeDisplayName(ours.TypeUId, out string oursType) ||
			    !TryGetTypeDisplayName(theirs.TypeUId, out string theirsType)) {
				continue;
			}

			bool bothChanged = !baseColumns.TryGetValue(key, out EntityColumn baseColumn) ||
				(!string.Equals(baseColumn.TypeUId, ours.TypeUId, StringComparison.OrdinalIgnoreCase) &&
				 !string.Equals(baseColumn.TypeUId, theirs.TypeUId, StringComparison.OrdinalIgnoreCase));
			if (bothChanged) {
				string question = $"Which type should {ours.Name} keep: {oursType} or {theirsType}?";
				if (!diagnostics.Contains(question)) {
					diagnostics.Add(question);
				}
			}
		}
	}

	private static bool TryReadEntityColumns(string content, out IReadOnlyDictionary<string, EntityColumn> columns) {
		columns = new Dictionary<string, EntityColumn>(StringComparer.OrdinalIgnoreCase);
		try {
			if (JsonNode.Parse(content.TrimStart('\uFEFF'))?["MetaData"]?["Schema"]?["D2"] is not JsonArray array) {
				return false;
			}

			var result = new Dictionary<string, EntityColumn>(StringComparer.OrdinalIgnoreCase);
			foreach (JsonObject column in array.OfType<JsonObject>()) {
				string? uId = ReadString(column, "UId");
				string? name = ReadString(column, "A2");
				string? typeUId = ReadString(column, "S2");
				if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(typeUId)) {
					continue;
				}

				string key = string.IsNullOrWhiteSpace(uId) ? name : uId;
				result[key] = new EntityColumn(name, typeUId);
			}

			columns = result;
			return true;
		}
		catch (JsonException) {
			return false;
		}
	}

	private static bool TryGetTypeDisplayName(string value, out string displayName) {
		displayName = string.Empty;
		if (!Guid.TryParse(value, out Guid uId) || !CreatioDataValueType.TryGet(uId, out CreatioDataValueTypeInfo info)) {
			return false;
		}
		displayName = info.Name switch {
			"Integer" => "Number",
			"DateTime" => "Date/Time",
			_ => info.Name
		};
		return true;
	}

	private static CreatioArtifactMergeResult Result(
		string status,
		string artifactKind,
		string? diagnostic,
		CreatioArtifactMergeReport? report = null,
		string? content = null) {
		return new CreatioArtifactMergeResult(
			status,
			artifactKind,
			ResolverVersion,
			content,
			report ?? CreatioArtifactMergeReport.Empty,
			diagnostic is null ? [] : [diagnostic]);
	}

	private static string ResolveVersion() {
		Assembly assembly = typeof(Resolver.IConflictResolver).Assembly;
		return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
		       assembly.GetName().Version?.ToString() ??
		       "unknown";
	}

	private sealed class CappedBufferWriter(int maximumBytes) : IBufferWriter<byte> {
		private byte[] _buffer = new byte[Math.Min(4096, maximumBytes)];
		private int _writtenCount;

		public void Advance(int count) {
			if (count < 0 || count > _buffer.Length || _writtenCount + count > maximumBytes) {
				throw new SerializedResultLimitExceededException();
			}
			_writtenCount += count;
		}

		public Memory<byte> GetMemory(int sizeHint = 0) {
			EnsureCapacity(sizeHint);
			return _buffer;
		}

		public Span<byte> GetSpan(int sizeHint = 0) {
			EnsureCapacity(sizeHint);
			return _buffer;
		}

		private void EnsureCapacity(int sizeHint) {
			int requested = Math.Max(1, sizeHint);
			if (_writtenCount + requested > maximumBytes) {
				throw new SerializedResultLimitExceededException();
			}
			if (requested > _buffer.Length) {
				Array.Resize(ref _buffer, requested);
			}
		}
	}

	private sealed class SerializedResultLimitExceededException : Exception;

	[GeneratedRegex(
		"^\\s*[=+~]\\s+MetaData\\.Schema\\.(?<property>UId|A2|ManagerName|AD3)\\s+\"(?<value>[^\"]+)\"\\s*$",
		RegexOptions.CultureInvariant | RegexOptions.Multiline)]
	private static partial Regex FlatIdentityRegex();

	[GeneratedRegex(
		"\\+\\s+MetaData\\.Schema\\.D2\\s+\\{[^}]*\"A2\"\\s*:\\s*\"(?<column>[A-Za-z_][A-Za-z0-9_]{0,127})\"[^}]*<<<<<<< Local\\s*\"S2\"\\s*:\\s*\"(?<local>[0-9a-fA-F-]{36})\",\\s*=======\\s*\"S2\"\\s*:\\s*\"(?<remote>[0-9a-fA-F-]{36})\",\\s*>>>>>>> Remote",
		RegexOptions.CultureInvariant)]
	private static partial Regex EntityColumnTypeConflictRegex();

	[GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
	private static partial Regex SafeColumnNameRegex();

	private readonly record struct DescriptorIdentity(string? UId, string? Name, string? ManagerName);

	private readonly record struct MetadataIdentity(
		string? UId,
		string? Name,
		string? ManagerName,
		string? Subtype) {

		public bool IsValid => !string.IsNullOrWhiteSpace(UId) && !string.IsNullOrWhiteSpace(Name);

		public bool Matches(DescriptorIdentity descriptor) {
			return string.Equals(UId, descriptor.UId, StringComparison.OrdinalIgnoreCase) &&
			       string.Equals(Name, descriptor.Name, StringComparison.Ordinal) &&
			       (string.IsNullOrWhiteSpace(ManagerName) ||
			        string.Equals(ManagerName, descriptor.ManagerName, StringComparison.Ordinal));
		}
	}

	private readonly record struct EntityColumn(string Name, string TypeUId);

	private readonly record struct Classification(
		string ArtifactKind,
		Resolver.ConflictFileType? FileType,
		string? TerminalStatus,
		string? Diagnostic) {

		public static Classification Semantic(string kind, Resolver.ConflictFileType fileType) {
			return new Classification(kind, fileType, null, null);
		}

		public static Classification Terminal(string kind, string status, string diagnostic) {
			return new Classification(kind, null, status, diagnostic);
		}
	}
}
