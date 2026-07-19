using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Knowledge;

internal sealed record KnowledgeBundleManifestDto(
	string ContractVersion,
	string BundleSchemaVersion,
	string? LibraryId,
	string? LibraryVersion,
	ulong Sequence,
	string? BundleVersion,
	DateTimeOffset IssuedAt,
	KnowledgeBundleSourceDto Source,
	KnowledgeBundleCompatibilityDto Compatibility,
	KnowledgeBundleRequirementsDto Requirements,
	string DigestAlg,
	KnowledgeBundleSignatureDto Signature,
	IReadOnlyList<KnowledgeBundleResourceDto> Resources);

internal sealed record KnowledgeBundleSourceDto(string Repository, string Commit);

internal sealed record KnowledgeBundleCompatibilityDto(
	KnowledgeBundleVersionRangeDto Clio,
	KnowledgeBundleVersionRangeDto McpToolContract);

internal sealed record KnowledgeBundleVersionRangeDto(string Min, string Max);

internal sealed record KnowledgeBundleRequirementsDto(
	IReadOnlyList<string> Tools,
	IReadOnlyList<string>? GuidanceIds,
	IReadOnlyList<string>? ItemIds,
	IReadOnlyList<string> ResourceUris);

internal sealed record KnowledgeBundleSignatureDto(string Algorithm, string KeyId);

internal sealed record KnowledgeBundleResourceDto(
	string? Id,
	string? ItemId,
	string? TopicId,
	string? Role,
	string Uri,
	IReadOnlyList<string>? LegacyUris,
	string Path,
	string MediaType,
	long Length,
	string Digest);

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(KnowledgeBundleManifestDto))]
internal sealed partial class KnowledgeBundleJsonContext : JsonSerializerContext;
