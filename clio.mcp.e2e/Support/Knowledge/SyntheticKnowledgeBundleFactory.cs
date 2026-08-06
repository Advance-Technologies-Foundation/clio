using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support;

namespace Clio.Mcp.E2E.Support.Knowledge;

/// <summary>
/// Builds signed synthetic knowledge bundles for hermetic transport fixtures.
/// </summary>
/// <remarks>
/// The bundle format is the delivery contract every transport shares, so it is produced in one place
/// rather than once per transport fixture. Content is deliberately meaningless: these fixtures prove
/// delivery mechanics, and article text is owned and tested by <c>clio-knowledge</c>.
/// </remarks>
internal static class SyntheticKnowledgeBundleFactory {

	/// <summary>The role a reference-example catalog entry declares.</summary>
	private static readonly UTF8Encoding StrictUtf8 = new(false, true);

	/// <summary>
	/// Builds one signed bundle.
	/// </summary>
	/// <param name="request">The identity, content, and signing inputs for this generation.</param>
	/// <returns>The bundle bytes together with the evidence a test asserts against.</returns>
	internal static SyntheticBundle Create(SyntheticBundleRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		string sourceCommit = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Revision)))
			.ToLowerInvariant()[..40];
		List<SyntheticResource> resources = BuildResources(request, sourceCommit);
		byte[] manifest = CreateManifest(request, sourceCommit, resources);
		byte[] signature = request.SigningKey.SignData(manifest, HashAlgorithmName.SHA256);
		if (request.CorruptSignature) {
			signature[0] ^= 0x01;
		}
		byte[] bundle = CreateArchive(new Dictionary<string, byte[]>(StringComparer.Ordinal) {
			["manifest.json"] = manifest,
			["manifest.sig"] = signature
		}.Concat(resources.Select(resource =>
			new KeyValuePair<string, byte[]>(resource.Path, resource.Bytes))));
		SyntheticResource selected = resources.Single(resource => resource.Name == request.SelectedGuideName);
		return new SyntheticBundle(bundle, selected.Digest, sourceCommit);
	}

	/// <summary>
	/// Packs arbitrary entries into an uncompressed ZIP archive.
	/// </summary>
	/// <param name="entries">The entry paths and bytes, in the order they should be written.</param>
	/// <returns>The archive bytes.</returns>
	internal static byte[] CreateArchive(IEnumerable<KeyValuePair<string, byte[]>> entries) {
		using MemoryStream output = new();
		using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true)) {
			foreach ((string path, byte[] bytes) in entries) {
				ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
				using Stream stream = entry.Open();
				stream.Write(bytes);
			}
		}
		return output.ToArray();
	}

	private static List<SyntheticResource> BuildResources(SyntheticBundleRequest request, string sourceCommit) {
		List<SyntheticResource> resources = request.GuideNames
			.Select((itemId, index) => CreateTextResource(
				request,
				itemId,
				$"synthetic.{itemId}",
				"guidance",
				$"resources/synthetic-{index}.txt",
				"text/plain",
				$"synthetic::{request.Revision}::{itemId}::sequence={request.Sequence}\n"))
			.ToList();
		if (request.ReferenceName is not null) {
			resources.Add(CreateTextResource(
				request,
				request.ReferenceName,
				"synthetic.transport-guide.details",
				"reference",
				"resources/synthetic-reference.txt",
				"text/plain",
				$"synthetic::{request.Revision}::reference-details::sequence={request.Sequence}\n"));
		}
		return resources;
	}

	private static SyntheticResource CreateTextResource(
		SyntheticBundleRequest request,
		string itemId,
		string topicId,
		string role,
		string path,
		string mediaType,
		string text) {
		byte[] bytes = StrictUtf8.GetBytes(text);
		return new SyntheticResource(
			itemId,
			topicId,
			role,
			$"{KnowledgeResolver.NamespacedUriPrefix}{request.LibraryId}/{itemId}",
			path,
			mediaType,
			bytes,
			Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
	}

	private static byte[] CreateManifest(
		SyntheticBundleRequest request,
		string sourceCommit,
		IReadOnlyList<SyntheticResource> resources) => JsonSerializer.SerializeToUtf8Bytes(new {
		contractVersion = "1.0.0",
		bundleSchemaVersion = "1.0.0",
		libraryId = request.LibraryId,
		libraryVersion = request.LibraryVersion,
		sequence = request.Sequence,
		source = new {
			repository = request.SourceRepository,
			commit = sourceCommit
		},
		compatibility = new {
			clio = new { min = "0.0.0", max = "99.99.99" },
			mcpToolContract = new { min = "1.0.0", max = "1.1.0" }
		},
		requirements = new {
			tools = new[] { GuidanceGetTool.ToolName },
			itemIds = resources.Select(resource => resource.Name).ToArray(),
			resourceUris = resources.Select(resource => resource.Uri).ToArray()
		},
		digestAlg = "SHA-256",
		signature = new { algorithm = "ECDSA-P256-SHA256", keyId = request.KeyId },
		resources = resources.Select(resource => new {
			itemId = resource.Name,
			topicId = resource.TopicId,
			role = resource.Role,
			title = $"Synthetic {resource.Name}",
			description = $"Synthetic discovery metadata for {resource.Name}.",
			uri = resource.Uri,
			legacyUris = request.LegacyUriByItemId.TryGetValue(resource.Name, out string? legacyUri)
				? new[] { legacyUri }
				: Array.Empty<string>(),
			requiredFeatures = (string[]?)null,
			path = resource.Path,
			mediaType = resource.MediaType,
			length = resource.Bytes.LongLength,
			digest = resource.Digest
		})
	});

	private sealed record SyntheticResource(
		string Name,
		string TopicId,
		string Role,
		string Uri,
		string Path,
		string MediaType,
		byte[] Bytes,
		string Digest);
}

/// <summary>Describes one synthetic generation to build.</summary>
internal sealed record SyntheticBundleRequest(
	string LibraryId,
	string LibraryVersion,
	ulong Sequence,
	string Revision,
	string KeyId,
	ECDsa SigningKey,
	string SelectedGuideName,
	IReadOnlyList<string> GuideNames,
	string SourceRepository,
	string? ReferenceName = null,
	IReadOnlyDictionary<string, string>? LegacyUris = null,
	bool CorruptSignature = false) {

	/// <summary>Legacy URI aliases keyed by item ID, never null.</summary>
	internal IReadOnlyDictionary<string, string> LegacyUriByItemId =>
		LegacyUris ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>The bytes of one built generation plus what a test needs to assert about it.</summary>
internal sealed record SyntheticBundle(byte[] Bytes, string SelectedGuideDigest, string SourceCommit);
