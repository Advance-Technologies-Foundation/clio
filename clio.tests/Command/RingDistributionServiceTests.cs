using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RingDistributionServiceTests {

	private string _root;

	[SetUp]
	public void Setup() {
		_root = Path.Combine(Path.GetTempPath(), "clio-ring-tests", Guid.NewGuid().ToString("N"));
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, true);
		}
	}

	[Test]
	[Description("Proves the local install, status, idempotent update, and uninstall happy path from one release fixture.")]
	public async Task ExecuteAsync_ShouldCompleteLifecycle_WhenReleaseIsValid() {
		// Arrange
		byte[] archive = CreateArchive(("clio-ring.exe", "ring"), ("actions.json", "{}"));
		string hash = Convert.ToHexString(SHA256.HashData(archive));
		string manifest = $$"""
			{"schemaVersion":1,"version":"0.1.0-preview.1","channel":"internal-preview","rid":"win-x64","assetUrl":"https://example.test/clio-ring.zip","sha256":"{{hash}}","entryPoint":"clio-ring.exe"}
			""";
		IHttpClientFactory httpFactory = CreateFactory(manifest, archive);
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		processExecutor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(new ProcessLaunchResult { Started = true, ProcessId = 42 });
		RingDistributionService sut = new(httpFactory, processExecutor, _root, ["example.test"]);

		// Act
		RingDistributionResult install = await sut.ExecuteAsync("install", "https://example.test/manifest.json", CancellationToken.None);
		RingDistributionResult status = await sut.ExecuteAsync("status", "https://example.test/manifest.json", CancellationToken.None);
		RingDistributionResult launch = await sut.ExecuteAsync("launch", "https://example.test/manifest.json", CancellationToken.None);
		RingDistributionResult update = await sut.ExecuteAsync("update", "https://example.test/manifest.json", CancellationToken.None);
		RingDistributionResult uninstall = await sut.ExecuteAsync("uninstall", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		install.Success.Should().BeTrue(because: "a checksum-verified release should install");
		status.Message.Should().Contain("0.1.0-preview.1", because: "status should identify the active version");
		launch.Success.Should().BeTrue(because: "the installed entry point should be launchable");
		await processExecutor.Received(1).FireAndForgetAsync(
			Arg.Is<ProcessExecutionOptions>(o => o.Program.EndsWith("clio-ring.exe", StringComparison.OrdinalIgnoreCase)));
		update.Message.Should().Contain("Installed", because: "same-version update should verify and repair the active release");
		uninstall.Success.Should().BeTrue(because: "the installed companion should be removable");
		Directory.Exists(_root).Should().BeFalse(because: "uninstall should remove only the isolated Ring root");
	}

	[Test]
	[Description("Rejects a release when its downloaded bytes do not match the manifest checksum.")]
	public async Task ExecuteAsync_ShouldRejectRelease_WhenChecksumDoesNotMatch() {
		// Arrange
		byte[] archive = CreateArchive(("clio-ring.exe", "ring"));
		string manifest = """
			{"schemaVersion":1,"version":"0.1.0-preview.1","channel":"internal-preview","rid":"win-x64","assetUrl":"https://example.test/clio-ring.zip","sha256":"00","entryPoint":"clio-ring.exe"}
			""";
		RingDistributionService sut = new(CreateFactory(manifest, archive), Substitute.For<IProcessExecutor>(), _root,
			["example.test"]);

		// Act
		Func<Task> action = () => sut.ExecuteAsync("install", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		await action.Should().ThrowAsync<InvalidDataException>(because: "unverified release bytes must never be installed");
	}

	[Test]
	[Description("Rejects a ZIP entry that would escape the version staging directory.")]
	public async Task ExecuteAsync_ShouldRejectRelease_WhenArchiveTraversesPath() {
		// Arrange
		byte[] archive = CreateArchive(("../escape.exe", "bad"));
		string hash = Convert.ToHexString(SHA256.HashData(archive));
		string manifest = $$"""
			{"schemaVersion":1,"version":"0.1.0-preview.1","channel":"internal-preview","rid":"win-x64","assetUrl":"https://example.test/clio-ring.zip","sha256":"{{hash}}","entryPoint":"escape.exe"}
			""";
		RingDistributionService sut = new(CreateFactory(manifest, archive), Substitute.For<IProcessExecutor>(), _root,
			["example.test"]);

		// Act
		Func<Task> action = () => sut.ExecuteAsync("install", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		await action.Should().ThrowAsync<InvalidDataException>(because: "archive extraction must remain inside the isolated install root");
	}

	[Test]
	[Description("Repairs a same-version installation when the active entry point is missing.")]
	public async Task ExecuteAsync_ShouldRepairInstallation_WhenCurrentEntryPointIsMissing() {
		// Arrange
		byte[] archive = CreateArchive(("clio-ring.exe", "ring"));
		string hash = Convert.ToHexString(SHA256.HashData(archive));
		string manifest = CreateManifest("0.2.0-preview.1", hash);
		RingDistributionService sut = new(CreateFactory(manifest, archive), Substitute.For<IProcessExecutor>(), _root,
			["example.test"]);
		await sut.ExecuteAsync("install", "https://example.test/manifest.json", CancellationToken.None);
		string entryPoint = Path.Combine(_root, "versions", "0.2.0-preview.1", "clio-ring.exe");
		File.Delete(entryPoint);

		// Act
		RingDistributionResult result = await sut.ExecuteAsync("update", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		result.Success.Should().BeTrue(because: "same-version update should repair an incomplete installation");
		File.Exists(entryPoint).Should().BeTrue(because: "repair should restore the missing entry point");
	}

	[Test]
	[Description("Repairs a same-version installation when a non-entrypoint release file is missing.")]
	public async Task ExecuteAsync_ShouldRepairInstallation_WhenSupportingFileIsMissing() {
		// Arrange
		byte[] archive = CreateArchive(("clio-ring.exe", "ring"), ("actions.json", "{}"));
		string hash = Convert.ToHexString(SHA256.HashData(archive));
		string manifest = CreateManifest("0.2.0-preview.1", hash);
		RingDistributionService sut = new(CreateFactory(manifest, archive), Substitute.For<IProcessExecutor>(), _root,
			["example.test"]);
		await sut.ExecuteAsync("install", "https://example.test/manifest.json", CancellationToken.None);
		string supportingFile = Path.Combine(_root, "versions", "0.2.0-preview.1", "actions.json");
		File.Delete(supportingFile);

		// Act
		RingDistributionResult result = await sut.ExecuteAsync("update", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		result.Success.Should().BeTrue(because: "same-version update is also an explicit repair operation");
		File.Exists(supportingFile).Should().BeTrue(because: "repair should restore every release file, not only the entry point");
	}

	[Test]
	[Description("Restores the complete active same-version installation when replacing it with the verified repair staging directory fails.")]
	public async Task ExecuteAsync_ShouldRestoreActiveInstallation_WhenSameVersionSwapFails() {
		// Arrange
		byte[] originalArchive = CreateArchive(("clio-ring.exe", "original"), ("actions.json", "original-actions"));
		string originalHash = Convert.ToHexString(SHA256.HashData(originalArchive));
		string manifest = CreateManifest("0.2.0-preview.1", originalHash);
		RingDistributionService installer = new(CreateFactory(manifest, originalArchive),
			Substitute.For<IProcessExecutor>(), _root, ["example.test"]);
		await installer.ExecuteAsync("install", "https://example.test/manifest.json", CancellationToken.None);

		byte[] repairArchive = CreateArchive(("clio-ring.exe", "repair"), ("actions.json", "repair-actions"));
		string repairManifest = CreateManifest("0.2.0-preview.1", Convert.ToHexString(SHA256.HashData(repairArchive)));
		bool replacementMoveFailed = false;
		void MoveWithInjectedFailure(string source, string destination) {
			if (!replacementMoveFailed && source.Contains(".staging-", StringComparison.Ordinal)) {
				replacementMoveFailed = true;
				throw new IOException("Injected replacement move failure.");
			}
			Directory.Move(source, destination);
		}
		RingDistributionService sut = new(CreateFactory(repairManifest, repairArchive),
			Substitute.For<IProcessExecutor>(), _root, ["example.test"], moveDirectory: MoveWithInjectedFailure);

		// Act
		Func<Task> action = () => sut.ExecuteAsync("update", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		await action.Should().ThrowAsync<IOException>(because: "the injected replacement failure must reach the caller");
		File.ReadAllText(Path.Combine(_root, "versions", "0.2.0-preview.1", "clio-ring.exe"))
			.Should().Be("original", because: "rollback must preserve the previously launchable entry point");
		File.ReadAllText(Path.Combine(_root, "versions", "0.2.0-preview.1", "actions.json"))
			.Should().Be("original-actions", because: "rollback must preserve every supporting file in the active release");
	}

	[Test]
	[Description("Keeps the committed same-version replacement complete when cleanup of the old backup is blocked.")]
	public async Task ExecuteAsync_ShouldKeepReplacement_WhenCommittedBackupCleanupFails() {
		// Arrange
		byte[] originalArchive = CreateArchive(("clio-ring.exe", "original"), ("actions.json", "original-actions"));
		string manifest = CreateManifest("0.2.0-preview.1", Convert.ToHexString(SHA256.HashData(originalArchive)));
		RingDistributionService installer = new(CreateFactory(manifest, originalArchive),
			Substitute.For<IProcessExecutor>(), _root, ["example.test"]);
		await installer.ExecuteAsync("install", "https://example.test/manifest.json", CancellationToken.None);

		byte[] repairArchive = CreateArchive(("clio-ring.exe", "repair"), ("actions.json", "repair-actions"));
		string repairManifest = CreateManifest("0.2.0-preview.1", Convert.ToHexString(SHA256.HashData(repairArchive)));
		void DeleteWithInjectedBackupFailure(string path, bool recursive) {
			if (path.Contains(".backup-", StringComparison.Ordinal)) {
				throw new IOException("Injected locked backup failure.");
			}
			Directory.Delete(path, recursive);
		}
		RingDistributionService sut = new(CreateFactory(repairManifest, repairArchive),
			Substitute.For<IProcessExecutor>(), _root, ["example.test"],
			deleteDirectory: DeleteWithInjectedBackupFailure);

		// Act
		RingDistributionResult result = await sut.ExecuteAsync("update", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		result.Success.Should().BeTrue(because: "old-backup cleanup occurs after the verified replacement is committed");
		File.ReadAllText(Path.Combine(_root, "versions", "0.2.0-preview.1", "clio-ring.exe"))
			.Should().Be("repair", because: "cleanup failure must not discard the complete committed entry point");
		File.ReadAllText(Path.Combine(_root, "versions", "0.2.0-preview.1", "actions.json"))
			.Should().Be("repair-actions", because: "cleanup failure must not discard committed supporting files");
		File.ReadAllText(Path.Combine(_root, "current.json"))
			.Should().Contain("0.2.0-preview.1", because: "the pointer must continue to identify the committed replacement");
	}

	[Test]
	[Description("Refuses an older manifest so a stale stable pointer cannot silently downgrade Ring.")]
	public async Task ExecuteAsync_ShouldRefuseDowngrade_WhenManifestVersionIsOlder() {
		// Arrange
		string versionRoot = Path.Combine(_root, "versions", "0.3.0-preview.1");
		Directory.CreateDirectory(versionRoot);
		File.WriteAllText(Path.Combine(versionRoot, "clio-ring.exe"), "ring");
		Directory.CreateDirectory(_root);
		File.WriteAllText(Path.Combine(_root, "current.json"),
			"{\"version\":\"0.3.0-preview.1\",\"entryPoint\":\"clio-ring.exe\"}");
		byte[] archive = CreateArchive(("clio-ring.exe", "old"));
		string manifest = CreateManifest("0.2.0-preview.1", Convert.ToHexString(SHA256.HashData(archive)));
		RingDistributionService sut = new(CreateFactory(manifest, archive), Substitute.For<IProcessExecutor>(), _root,
			["example.test"]);

		// Act
		RingDistributionResult result = await sut.ExecuteAsync("update", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "normal updates must never roll back the active version");
		result.Message.Should().Contain("Refusing to downgrade", because: "the refusal should be actionable");
	}

	[Test]
	[Description("Leaves the installation intact when the active executable is locked by a running process.")]
	public async Task ExecuteAsync_ShouldNotPartiallyUninstall_WhenEntryPointIsLocked() {
		// Arrange
		string versionRoot = Path.Combine(_root, "versions", "0.2.0-preview.1");
		Directory.CreateDirectory(versionRoot);
		string entryPoint = Path.Combine(versionRoot, "clio-ring.exe");
		File.WriteAllText(entryPoint, "ring");
		File.WriteAllText(Path.Combine(_root, "current.json"),
			"{\"version\":\"0.2.0-preview.1\",\"entryPoint\":\"clio-ring.exe\"}");
		RingDistributionService sut = new(CreateFactory("{}", []), Substitute.For<IProcessExecutor>(), _root,
			["example.test"]);
		using FileStream lockStream = File.Open(entryPoint, FileMode.Open, FileAccess.Read, FileShare.Read);

		// Act
		RingDistributionResult result = await sut.ExecuteAsync("uninstall", "https://example.test/manifest.json", CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "a running Ring must be closed before destructive cleanup");
		Directory.Exists(_root).Should().BeTrue(because: "the preflight must avoid a partial recursive deletion");
	}

	private static byte[] CreateArchive(params (string Path, string Content)[] files) {
		using MemoryStream stream = new();
		using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true)) {
			foreach ((string path, string content) in files) {
				using StreamWriter writer = new(zip.CreateEntry(path).Open(), Encoding.UTF8);
				writer.Write(content);
			}
		}
		return stream.ToArray();
	}

	private static string CreateManifest(string version, string hash) => $$"""
		{"schemaVersion":1,"version":"{{version}}","channel":"internal-preview","rid":"win-x64","assetUrl":"https://example.test/clio-ring.zip","sha256":"{{hash}}","entryPoint":"clio-ring.exe"}
		""";

	private static IHttpClientFactory CreateFactory(string manifest, byte[] archive) {
		HttpClient client = new(new FixtureHandler(manifest, archive));
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		return factory;
	}

	private sealed class FixtureHandler(string manifest, byte[] archive) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			HttpContent content = request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.Ordinal)
				? new StringContent(manifest, Encoding.UTF8, "application/json")
				: new ByteArrayContent(archive);
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
		}
	}
}
