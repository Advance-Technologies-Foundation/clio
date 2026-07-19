using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Tools;

namespace Clio.Mcp.E2E.Support.Knowledge;

internal sealed class SyntheticKnowledgeNuGetFixture : IDisposable {
	internal const string PackageId = "Clio.Synthetic.Knowledge";
	internal const string LibraryId = "com.example.synthetic";
	internal const string SelectedGuideName = "synthetic-transport-guide";
	internal const string ReferenceExampleId = "example.synthetic.reference";
	internal const string ReferenceExampleRepository = "https://github.com/example/synthetic-reference";

	private readonly string _root;
	private readonly ECDsa _signingKey;

	private SyntheticKnowledgeNuGetFixture(string root, ECDsa signingKey, FakeNuGetV3Feed feed) {
		_root = root;
		_signingKey = signingKey;
		Feed = feed;
		PublicKeyPath = Path.Combine(root, "synthetic-public.pem");
		File.WriteAllText(PublicKeyPath, signingKey.ExportSubjectPublicKeyInfoPem());
		SelectedGuideUri = $"{KnowledgeResolver.NamespacedUriPrefix}{LibraryId}/{SelectedGuideName}";
	}

	internal FakeNuGetV3Feed Feed { get; }

	internal string PublicKeyPath { get; }

	internal string SelectedGuideUri { get; }

	internal string KeyId => "synthetic-nuget-test-key";

	internal static SyntheticKnowledgeNuGetFixture Create() {
		string root = Path.Combine(Path.GetTempPath(), $"clio-knowledge-nuget-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		FakeNuGetV3Feed feed = new(PackageId);
		return new SyntheticKnowledgeNuGetFixture(root, key, feed);
	}

	internal SyntheticPackageEvidence PublishValid(string packageVersion, ulong sequence, string revision) =>
		Publish(packageVersion, sequence, revision, corruptSignature: false);

	internal SyntheticPackageEvidence PublishInvalidSignature(
		string packageVersion,
		ulong sequence,
		string revision) => Publish(packageVersion, sequence, revision, corruptSignature: true);

	public void Dispose() {
		Feed.Dispose();
		_signingKey.Dispose();
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, recursive: true);
		}
	}

	private SyntheticPackageEvidence Publish(
		string packageVersion,
		ulong sequence,
		string revision,
		bool corruptSignature) {
		string sourceCommit = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision)))
			.ToLowerInvariant()[..40];
		string[] itemIds = [SelectedGuideName, "native-library-lifecycle", "smoke-testing"];
		List<SyntheticResource> resources = itemIds
			.Select((itemId, index) => {
				byte[] bytes = new UTF8Encoding(false, true).GetBytes(
					$"synthetic::{revision}::{itemId}::sequence={sequence}\n");
				return new SyntheticResource(
					itemId,
					itemId,
					"guidance",
					$"{KnowledgeResolver.NamespacedUriPrefix}{LibraryId}/{itemId}",
					$"resources/synthetic-{index}.txt",
					"text/plain",
					bytes,
					Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
			})
			.ToList();
		byte[] exampleBytes = new UTF8Encoding(false, true).GetBytes($$"""
			schemaVersion: 0
			id: {{ReferenceExampleId}}
			title: Synthetic reference example
			status: published
			primaryUseCase:
			  id: synthetic-integration
			  summary: Demonstrate a hermetic reference-example catalog entry.
			source:
			  repository: {{ReferenceExampleRepository}}
			  revision: {{sourceCommit}}
			  defaultBranch: main
			entryPoints:
			  overview: README.md
			  package: packages/SyntheticReference
			supportingCapabilities:
			  - native-library-lifecycle
			  - synthetic-testing
			compatibility:
			  status: example-declared
			  details: Synthetic E2E fixture only.
			trust:
			  publisher: Synthetic publisher
			  level: published
			notes:
			  - This is generated mechanics-only test data.
			""");
		resources.Add(new SyntheticResource(
			"reference-example-synthetic",
			ReferenceExampleId,
			KnowledgeReferenceExampleService.ReferenceExampleRole,
			$"{KnowledgeResolver.NamespacedUriPrefix}{LibraryId}/reference-example-synthetic",
			"resources/reference-example-synthetic.yaml",
			"text/yaml",
			exampleBytes,
			Convert.ToHexString(SHA256.HashData(exampleBytes)).ToLowerInvariant()));
		byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new {
			contractVersion = "1.0.0",
			bundleSchemaVersion = "1.0.0",
			libraryId = LibraryId,
			libraryVersion = packageVersion,
			sequence,
			source = new {
				repository = "synthetic-nuget-fixture",
				commit = sourceCommit
			},
			compatibility = new {
				clio = new { min = "0.0.0", max = "99.99.99" },
				mcpToolContract = new { min = "1.0.0", max = "1.1.0" }
			},
			requirements = new {
				tools = new[] { GuidanceGetTool.ToolName, KnowledgeManagementTools.ListKnowledgeExamplesToolName },
				itemIds = resources.Select(resource => resource.Name).ToArray(),
				resourceUris = resources.Select(resource => resource.Uri).ToArray()
			},
			digestAlg = "SHA-256",
			signature = new { algorithm = "ECDSA-P256-SHA256", keyId = KeyId },
			resources = resources.Select(resource => new {
				itemId = resource.Name,
				topicId = resource.TopicId,
				role = resource.Role,
				uri = resource.Uri,
				legacyUris = resource.Name == SelectedGuideName
					? new[] { $"docs://mcp/guides/{SelectedGuideName}" }
					: Array.Empty<string>(),
				path = resource.Path,
				mediaType = resource.MediaType,
				length = resource.Bytes.LongLength,
				digest = resource.Digest
			})
		});
		byte[] signature = _signingKey.SignData(manifest, HashAlgorithmName.SHA256);
		if (corruptSignature) {
			signature[0] ^= 0x01;
		}
		byte[] bundle = CreateArchive(new Dictionary<string, byte[]>(StringComparer.Ordinal) {
			["manifest.json"] = manifest,
			["manifest.sig"] = signature
		}.Concat(resources.Select(resource =>
			new KeyValuePair<string, byte[]>(resource.Path, resource.Bytes))));
		byte[] package = CreateNuGetPackage(packageVersion, bundle);
		Feed.Publish(packageVersion, package);
		SyntheticResource selected = resources.Single(resource => resource.Name == SelectedGuideName);
		return new SyntheticPackageEvidence(packageVersion, sequence, selected.Digest, sourceCommit);
	}

	private static byte[] CreateNuGetPackage(string packageVersion, byte[] bundle) {
		byte[] nuspec = Encoding.UTF8.GetBytes($$"""
			<?xml version="1.0" encoding="utf-8"?>
			<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
			  <metadata>
			    <id>{{PackageId}}</id>
			    <version>{{packageVersion}}</version>
			    <authors>synthetic-test</authors>
			    <description>Hermetic synthetic MCP mechanics fixture.</description>
			  </metadata>
			</package>
			""");
		return CreateArchive(new Dictionary<string, byte[]>(StringComparer.Ordinal) {
			[$"{PackageId}.nuspec"] = nuspec,
			[KnowledgeBundleNuGetClient.InnerBundlePath] = bundle
		});
	}

	private static byte[] CreateArchive(IEnumerable<KeyValuePair<string, byte[]>> entries) {
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

internal sealed record SyntheticPackageEvidence(
	string PackageVersion,
	ulong Sequence,
	string SelectedGuideDigest,
	string ReferenceExampleRevision);

internal sealed class FakeNuGetV3Feed : IDisposable {
	private readonly string _normalizedPackageId;
	private readonly TcpListener _listener;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly ConcurrentDictionary<string, byte[]> _packages = new(StringComparer.Ordinal);
	private readonly ConcurrentQueue<string> _requests = new();
	private readonly ConcurrentQueue<string> _completedRequests = new();
	private readonly Task _serverLoop;

	internal FakeNuGetV3Feed(string packageId) {
		_normalizedPackageId = packageId.ToLowerInvariant();
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();
		int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		BaseUri = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
		_serverLoop = ServeAsync(_cancellation.Token);
	}

	internal Uri BaseUri { get; }

	internal Uri ServiceIndexUri => new(BaseUri, "v3/index.json");

	internal IReadOnlyCollection<string> Requests => _requests.ToArray();

	internal IReadOnlyCollection<string> CompletedRequests => _completedRequests.ToArray();

	internal bool RedirectServiceIndex { get; set; }

	internal void Publish(string version, byte[] packageBytes) {
		_packages[version] = packageBytes.ToArray();
	}

	public void Dispose() {
		_cancellation.Cancel();
		_listener.Stop();
		try {
			_serverLoop.GetAwaiter().GetResult();
		} catch (OperationCanceledException) {
		} catch (SocketException) {
		}
		_cancellation.Dispose();
	}

	private async Task ServeAsync(CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			TcpClient client;
			try {
				client = await _listener.AcceptTcpClientAsync(cancellationToken);
			} catch (OperationCanceledException) {
				break;
			} catch (SocketException) when (cancellationToken.IsCancellationRequested) {
				break;
			}
			_ = ProcessClientAsync(client, cancellationToken);
		}
	}

	private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken) {
		using (client) {
			try {
				using NetworkStream stream = client.GetStream();
				using StreamReader reader = new(
					stream,
					Encoding.ASCII,
					detectEncodingFromByteOrderMarks: false,
					leaveOpen: true);
				string? requestLine = await reader.ReadLineAsync(cancellationToken);
				if (string.IsNullOrWhiteSpace(requestLine)) {
					return;
				}
				string[] parts = requestLine.Split(' ');
				if (parts.Length < 2) {
					return;
				}
				string path = new Uri(BaseUri, parts[1]).AbsolutePath;
				_requests.Enqueue(path);
				while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken))) {
				}
				(HttpStatusCode status, string contentType, byte[] body) = Resolve(path);
				string redirectHeader = status == HttpStatusCode.Redirect
					? $"Location: {new Uri(BaseUri, "redirect-target").AbsoluteUri}\r\n"
					: string.Empty;
				string headers = $"HTTP/1.1 {(int)status} {status}\r\n"
					+ $"Content-Type: {contentType}\r\n"
					+ $"Content-Length: {body.Length}\r\n"
					+ redirectHeader
					+ "Connection: close\r\n\r\n";
				await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
				await stream.WriteAsync(body, cancellationToken);
				_completedRequests.Enqueue(path);
			} catch (Exception exception) when (exception is IOException
					or OperationCanceledException
					or SocketException
					or UriFormatException) {
			}
		}
	}

	private (HttpStatusCode Status, string ContentType, byte[] Body) Resolve(string path) {
		if (string.Equals(path, "/v3/index.json", StringComparison.Ordinal)) {
			if (RedirectServiceIndex) {
				return (HttpStatusCode.Redirect, "text/plain", Encoding.UTF8.GetBytes("redirect"));
			}
			return Json(new {
				version = "3.0.0",
				resources = new[] {
					new Dictionary<string, string>(StringComparer.Ordinal) {
						["@id"] = new Uri(BaseUri, "flat/").AbsoluteUri,
						["@type"] = "PackageBaseAddress/3.0.0"
					}
				}
			});
		}
		if (string.Equals(path, $"/flat/{_normalizedPackageId}/index.json", StringComparison.Ordinal)) {
			return Json(new { versions = _packages.Keys.OrderBy(version => version, StringComparer.Ordinal).ToArray() });
		}
		string prefix = $"/flat/{_normalizedPackageId}/";
		if (path.StartsWith(prefix, StringComparison.Ordinal)) {
			string[] segments = path[prefix.Length..].Split('/');
			if (segments.Length == 2
					&& _packages.TryGetValue(segments[0], out byte[]? package)
					&& string.Equals(
						segments[1],
						$"{_normalizedPackageId}.{segments[0]}.nupkg",
						StringComparison.Ordinal)) {
				return (HttpStatusCode.OK, "application/octet-stream", package);
			}
		}
		return (HttpStatusCode.NotFound, "text/plain", Encoding.UTF8.GetBytes("not found"));
	}

	private static (HttpStatusCode Status, string ContentType, byte[] Body) Json<T>(T value) =>
		(HttpStatusCode.OK, "application/json", JsonSerializer.SerializeToUtf8Bytes(value));
}
