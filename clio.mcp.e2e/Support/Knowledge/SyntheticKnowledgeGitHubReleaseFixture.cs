using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clio.Command.McpServer.Knowledge;
using Clio.Mcp.E2E.Support;

namespace Clio.Mcp.E2E.Support.Knowledge;

/// <summary>
/// Hosts a hermetic GitHub Releases API on loopback and publishes signed synthetic bundles to it.
/// </summary>
/// <remarks>
/// No test may depend on live GitHub availability, so the whole discovery, redirect, and download
/// path is served locally. The server can also be taken offline mid-test, which is how the
/// warm-restart-without-network guarantee is proven rather than assumed.
/// </remarks>
internal sealed class SyntheticKnowledgeGitHubReleaseFixture : IDisposable {
	internal const string LibraryId = "com.example.synthetic.release";
	internal const string RepositoryOwner = "Example-Publisher";
	internal const string RepositoryName = "synthetic-knowledge";
	internal const string AssetName = "synthetic-knowledge-bundle.zip";
	internal const string SelectedGuideName = "synthetic-release-guide";
	internal const string SelectedReferenceName = "synthetic-release-guide-details";
	internal const string SelectedGuideLegacyUri = "docs://mcp/guides/synthetic-release-guide";

	private static readonly string[] GuideNames = [
		SelectedGuideName,
		"synthetic-release-lifecycle"
	];

	private readonly string _root;
	private readonly ECDsa _signingKey;

	private SyntheticKnowledgeGitHubReleaseFixture(string root, ECDsa signingKey, FakeGitHubReleasesApi api) {
		_root = root;
		_signingKey = signingKey;
		Api = api;
		PublicKeyPath = Path.Combine(root, "synthetic-release-public.pem");
		File.WriteAllText(PublicKeyPath, signingKey.ExportSubjectPublicKeyInfoPem());
		SelectedGuideUri = $"{KnowledgeResolver.NamespacedUriPrefix}{LibraryId}/{SelectedGuideName}";
	}

	internal FakeGitHubReleasesApi Api { get; }

	internal string PublicKeyPath { get; }

	internal string SelectedGuideUri { get; }

	internal string KeyId => "synthetic-github-release-test-key";

	internal static SyntheticKnowledgeGitHubReleaseFixture Create() {
		string root = Path.Combine(PhysicalPath.Resolve(Path.GetTempPath()), $"clio-knowledge-release-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		FakeGitHubReleasesApi api = new(RepositoryOwner, RepositoryName, AssetName);
		return new SyntheticKnowledgeGitHubReleaseFixture(root, key, api);
	}

	/// <summary>Publishes a correctly signed release the consumer must accept.</summary>
	internal SyntheticReleaseEvidence PublishValid(string tag, ulong sequence, string revision) =>
		Publish(tag, sequence, revision, corruptSignature: false, corruptDigest: false);

	/// <summary>Publishes a newer release whose signature does not verify.</summary>
	internal SyntheticReleaseEvidence PublishInvalidSignature(string tag, ulong sequence, string revision) =>
		Publish(tag, sequence, revision, corruptSignature: true, corruptDigest: false);

	/// <summary>Publishes a release whose advertised digest does not match its asset bytes.</summary>
	internal SyntheticReleaseEvidence PublishMismatchedDigest(string tag, ulong sequence, string revision) =>
		Publish(tag, sequence, revision, corruptSignature: false, corruptDigest: true);

	public void Dispose() {
		Api.Dispose();
		_signingKey.Dispose();
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, recursive: true);
		}
	}

	private SyntheticReleaseEvidence Publish(
		string tag,
		ulong sequence,
		string revision,
		bool corruptSignature,
		bool corruptDigest) {
		SyntheticBundle bundle = SyntheticKnowledgeBundleFactory.Create(new SyntheticBundleRequest(
			LibraryId,
			tag,
			sequence,
			revision,
			KeyId,
			_signingKey,
			SelectedGuideName,
			GuideNames,
			"synthetic-github-release-fixture",
			SelectedReferenceName,
			new Dictionary<string, string>(StringComparer.Ordinal) {
				[SelectedGuideName] = SelectedGuideLegacyUri
			},
			corruptSignature));
		string digest = corruptDigest
			? new string('0', 64)
			: Convert.ToHexString(SHA256.HashData(bundle.Bytes)).ToLowerInvariant();
		Api.Publish(tag, bundle.Bytes, digest);
		return new SyntheticReleaseEvidence(tag, sequence, bundle.SelectedGuideDigest, bundle.SourceCommit);
	}
}

/// <summary>The identity of one published synthetic release.</summary>
internal sealed record SyntheticReleaseEvidence(
	string Tag,
	ulong Sequence,
	string SelectedGuideDigest,
	string SourceCommit);

/// <summary>
/// A minimal loopback stand-in for the GitHub Releases REST API and its asset download redirect.
/// </summary>
internal sealed class FakeGitHubReleasesApi : IDisposable {
	private readonly string _latestPath;
	private readonly string _tagsPathPrefix;
	private readonly string _assetsPathPrefix;
	private readonly string _assetName;
	private readonly TcpListener _listener;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly ConcurrentDictionary<string, PublishedRelease> _releases = new(StringComparer.Ordinal);
	private readonly ConcurrentQueue<string> _requests = new();
	private readonly Task _serverLoop;
	private volatile string? _latestTag;

	internal FakeGitHubReleasesApi(string owner, string repository, string assetName) {
		_assetName = assetName;
		_latestPath = $"/repos/{owner}/{repository}/releases/latest";
		_tagsPathPrefix = $"/repos/{owner}/{repository}/releases/tags/";
		_assetsPathPrefix = $"/repos/{owner}/{repository}/releases/assets/";
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();
		int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		BaseUri = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
		_serverLoop = ServeAsync(_cancellation.Token);
	}

	internal Uri BaseUri { get; }

	/// <summary>
	/// Stops answering requests, modelling a GitHub outage or a machine with no network.
	/// </summary>
	/// <remarks>
	/// Requests are still recorded while offline, so a test can prove not merely that a warm start
	/// succeeded but that it never reached for the network in the first place.
	/// </remarks>
	internal bool Offline { get; set; }

	internal IReadOnlyCollection<string> Requests => _requests.ToArray();

	internal void ResetRequests() {
		_requests.Clear();
	}

	internal void Publish(string tag, byte[] assetBytes, string digest) {
		_releases[tag] = new PublishedRelease(tag, assetBytes.ToArray(), digest);
		_latestTag = tag;
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
				if (Offline) {
					return;
				}
				await WriteResponseAsync(stream, path, cancellationToken);
			} catch (Exception exception) when (exception is IOException
					or OperationCanceledException
					or SocketException
					or UriFormatException) {
			}
		}
	}

	private async Task WriteResponseAsync(Stream stream, string path, CancellationToken cancellationToken) {
		(HttpStatusCode status, string contentType, byte[] body, string? location) = Resolve(path);
		string locationHeader = location is null ? string.Empty : $"Location: {location}\r\n";
		string headers = $"HTTP/1.1 {(int)status} {status}\r\n"
			+ $"Content-Type: {contentType}\r\n"
			+ $"Content-Length: {body.Length}\r\n"
			+ locationHeader
			+ "Connection: close\r\n\r\n";
		await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
		await stream.WriteAsync(body, cancellationToken);
	}

	private (HttpStatusCode Status, string ContentType, byte[] Body, string? Location) Resolve(string path) {
		if (string.Equals(path, _latestPath, StringComparison.Ordinal)) {
			return _latestTag is not null && _releases.TryGetValue(_latestTag, out PublishedRelease? latest)
				? Json(CreateReleaseDocument(latest))
				: NotFound();
		}
		if (path.StartsWith(_tagsPathPrefix, StringComparison.Ordinal)) {
			string tag = Uri.UnescapeDataString(path[_tagsPathPrefix.Length..]);
			return _releases.TryGetValue(tag, out PublishedRelease? tagged)
				? Json(CreateReleaseDocument(tagged))
				: NotFound();
		}
		if (path.StartsWith(_assetsPathPrefix, StringComparison.Ordinal)) {
			string tag = Uri.UnescapeDataString(path[_assetsPathPrefix.Length..]);
			return _releases.ContainsKey(tag)
				? (HttpStatusCode.Found, "text/plain", [], new Uri(BaseUri, $"download/{tag}").AbsoluteUri)
				: NotFound();
		}
		if (path.StartsWith("/download/", StringComparison.Ordinal)) {
			string tag = Uri.UnescapeDataString(path["/download/".Length..]);
			return _releases.TryGetValue(tag, out PublishedRelease? release)
				? (HttpStatusCode.OK, "application/zip", release.AssetBytes, null)
				: NotFound();
		}
		return NotFound();
	}

	// The asset ID is the tag rather than a number: the consumer treats the asset URL as opaque, and
	// keying by tag keeps the fake's routing table trivial.
	private object CreateReleaseDocument(PublishedRelease release) => new {
		tag_name = release.Tag,
		draft = false,
		prerelease = false,
		immutable = true,
		assets = new[] {
			new {
				name = _assetName,
				state = "uploaded",
				content_type = "application/zip",
				size = release.AssetBytes.LongLength,
				digest = $"sha256:{release.Digest}",
				id = release.Tag,
				url = new Uri(
						BaseUri,
						_assetsPathPrefix.TrimStart('/') + Uri.EscapeDataString(release.Tag))
					.AbsoluteUri
			}
		}
	};

	private static (HttpStatusCode Status, string ContentType, byte[] Body, string? Location) NotFound() =>
		(HttpStatusCode.NotFound, "text/plain", Encoding.UTF8.GetBytes("not found"), null);

	private static (HttpStatusCode Status, string ContentType, byte[] Body, string? Location) Json<T>(T value) =>
		(HttpStatusCode.OK, "application/json", JsonSerializer.SerializeToUtf8Bytes(value), null);

	private sealed record PublishedRelease(string Tag, byte[] AssetBytes, string Digest);
}
