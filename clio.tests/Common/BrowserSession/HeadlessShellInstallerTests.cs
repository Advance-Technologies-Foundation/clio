using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// ENG-103287: the pinned headless shell is downloaded only into ~/.clio/browsers/&lt;version&gt;/, verified by size and
/// SHA-256, extracted without leaving its folder, and reported clearly when the network or the archive fails. The HTTP
/// layer is faked; the file system is a real temporary directory.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class HeadlessShellInstallerTests {

	private const string Platform = "mac-arm64";
	private const string Executable = "chrome-headless-shell-mac-arm64/chrome-headless-shell";
	private string _root;

	[SetUp]
	public void SetUp() {
		_root = Path.Combine(Path.GetTempPath(), "clio-headless-shell-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, recursive: true);
		}
	}

	private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
		: HttpMessageHandler {

		public Uri RequestedUrl { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			RequestedUrl = request.RequestUri;
			return answer(request, cancellationToken);
		}
	}

	private sealed class FixedSource(HeadlessShellRelease.Archive archive) : IHeadlessShellSource {
		public string DisplayName => "test shell";
		public string Version => "1.2.3";
		public HeadlessShellRelease.Archive FindArchive(string platformKey) => platformKey == Platform ? archive : null;
		public Uri ArchiveUrl(string platformKey) => new("https://cft.example/1.2.3/" + platformKey + "/shell.zip");
		public string ExecutableRelativePath(string platformKey) => HeadlessShellRelease.ExecutableRelativePath(platformKey);
	}

	// A body sent without Content-Length, as a chunked response arrives.
	private sealed class UnsizedContent(byte[] body) : HttpContent {
		protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => stream.WriteAsync(body, 0, body.Length);

		protected override bool TryComputeLength(out long length) {
			length = 0;
			return false;
		}
	}

	private static byte[] Zip(params (string Name, int Mode)[] entries) {
		using MemoryStream buffer = new();
		using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true)) {
			foreach ((string name, int mode) in entries) {
				ZipArchiveEntry entry = zip.CreateEntry(name);
				entry.ExternalAttributes = (0x8000 | mode) << 16;
				using StreamWriter writer = new(entry.Open());
				writer.Write("#!/bin/sh\n");
			}
		}
		return buffer.ToArray();
	}

	private static HeadlessShellRelease.Archive PinOf(byte[] archive) =>
		new(archive.Length, Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant());

	private (HeadlessShellInstaller Installer, StubHandler Handler) Create(HeadlessShellRelease.Archive pin,
		Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer, TimeProvider time = null,
		string platform = Platform) {
		StubHandler handler = new(answer);
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(HeadlessShellInstaller.HttpClientName).Returns(_ => new HttpClient(handler));
		HeadlessShellInstaller installer = new(factory, new FixedSource(pin), new System.IO.Abstractions.FileSystem(),
			time ?? TimeProvider.System, _root, platform);
		return (installer, handler);
	}

	private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Serve(byte[] body,
		bool announceLength = true) =>
		(_, _) => {
			HttpContent content = announceLength ? new ByteArrayContent(body) : new UnsizedContent(body);
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
		};

	[Test]
	[Description("A verified archive is extracted into <root>/<version>/ with its executable bit, the download leaves no temporary files, and the installed executable is found afterwards.")]
	public async Task InstallAsync_Should_Install_A_Verified_Archive_Into_The_Version_Directory() {
		// Arrange
		byte[] archive = Zip((Executable, 0x1ED), ("chrome-headless-shell-mac-arm64/ABOUT", 0x1A4));
		(HeadlessShellInstaller installer, StubHandler handler) = Create(PinOf(archive), Serve(archive));

		// Act
		string executable = await installer.InstallAsync(CancellationToken.None);

		// Assert
		executable.Should().Be(Path.Combine(_root, "1.2.3", "chrome-headless-shell-mac-arm64", "chrome-headless-shell"),
			because: "the browser lands in the version directory");
		installer.FindInstalled().Should().Be(executable, because: "a later call finds it without downloading");
		handler.RequestedUrl.Should().Be(new Uri("https://cft.example/1.2.3/mac-arm64/shell.zip"),
			because: "the URL comes from the source");
		Directory.GetFileSystemEntries(_root).Should().ContainSingle(because: "the archive and the extraction folder are removed");
		if (!OperatingSystem.IsWindows()) {
			File.GetUnixFileMode(executable).Should().HaveFlag(UnixFileMode.UserExecute,
				because: "the browser could not start without its executable bit");
		}
	}

	[Test]
	[Description("An archive whose SHA-256 differs from the pin is refused and nothing is installed or left behind.")]
	public async Task InstallAsync_Should_Refuse_An_Archive_With_Another_Checksum() {
		// Arrange
		byte[] archive = Zip((Executable, 0x1ED));
		HeadlessShellRelease.Archive pin = new(archive.Length, new string('0', 64));
		(HeadlessShellInstaller installer, _) = Create(pin, Serve(archive));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "a tampered archive is never installed"))
			.Which.Message.Should().Contain("SHA-256", because: "the reason is named");
		installer.FindInstalled().Should().BeNull(because: "nothing was installed");
		Directory.GetFileSystemEntries(_root).Should().BeEmpty(because: "the rejected download is deleted");
	}

	[Test]
	[Description("A body longer than the pinned size is cut off as soon as it passes it, even when no Content-Length was sent.")]
	public async Task InstallAsync_Should_Stop_Reading_Past_The_Pinned_Size() {
		// Arrange
		byte[] archive = Zip((Executable, 0x1ED));
		HeadlessShellRelease.Archive pin = new(archive.Length - 10, PinOf(archive).Sha256);
		(HeadlessShellInstaller installer, _) = Create(pin, Serve(archive, announceLength: false));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "a wrong mirror cannot fill the disk"))
			.Which.Message.Should().Contain("larger than", because: "the reason is named");
	}

	[Test]
	[Description("An HTTP error names the status and the mirror variable.")]
	public async Task InstallAsync_Should_Name_The_Status_And_The_Mirror_On_An_Http_Error() {
		// Arrange
		(HeadlessShellInstaller installer, _) = Create(new HeadlessShellRelease.Archive(1, "x"),
			(_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "nothing could be downloaded"))
			.Which.Message.Should().Contain("HTTP 404").And.Contain(HeadlessShellRelease.MirrorEnvVar,
				because: "a corporate network needs the mirror override");
	}

	[Test]
	[Description("A machine without network gets a clear offline error naming the host and the mirror variable.")]
	public async Task InstallAsync_Should_Explain_An_Offline_Machine() {
		// Arrange
		(HeadlessShellInstaller installer, _) = Create(new HeadlessShellRelease.Archive(1, "x"),
			(_, _) => throw new HttpRequestException("No such host is known."));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "the download could not start"))
			.Which.Message.Should().Contain("https://cft.example").And.Contain("offline")
			.And.Contain(HeadlessShellRelease.MirrorEnvVar, because: "the caller learns where it tried and what to do");
	}

	[Test]
	[Description("An archive entry that would land outside the extraction folder is refused, and nothing is installed.")]
	public async Task InstallAsync_Should_Refuse_An_Entry_Outside_Its_Folder() {
		// Arrange
		byte[] archive = Zip(("../escaped", 0x1A4), (Executable, 0x1ED));
		(HeadlessShellInstaller installer, _) = Create(PinOf(archive), Serve(archive));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "a zip-slip entry is refused"))
			.Which.Message.Should().Contain("outside its folder", because: "the reason is named");
		File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escaped")).Should().BeFalse(because: "nothing escaped");
		installer.FindInstalled().Should().BeNull(because: "nothing was installed");
	}

	[Test]
	[Description("A download that stalls ends at the 90 s budget on the injected clock with a retry hint.")]
	public async Task InstallAsync_Should_Stop_At_The_Download_Budget() {
		// Arrange
		ManualTimeProvider time = new();
		TaskCompletionSource requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
		(HeadlessShellInstaller installer, _) = Create(new HeadlessShellRelease.Archive(1, "x"), async (_, token) => {
			requested.TrySetResult();
			await Task.Delay(Timeout.Infinite, token);
			return new HttpResponseMessage(HttpStatusCode.OK);
		}, time);
		Task<string> install = installer.InstallAsync(CancellationToken.None);
		await requested.Task.WaitAsync(TimeSpan.FromSeconds(30));

		// Act
		time.Advance(HeadlessShellInstaller.DownloadBudget);
		Func<Task> act = () => install.WaitAsync(TimeSpan.FromSeconds(30));

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "the call must answer inside the worker budget"))
			.Which.Message.Should().Contain("did not finish within 90 s", because: "the caller learns why");
	}

	[Test]
	[Description("A platform the source publishes no build for is refused with advice to choose an installed browser.")]
	public async Task InstallAsync_Should_Refuse_A_Platform_Without_A_Build() {
		// Arrange
		(HeadlessShellInstaller installer, StubHandler handler) = Create(new HeadlessShellRelease.Archive(1, "x"),
			Serve([]), platform: null);

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "there is nothing to download");
		handler.RequestedUrl.Should().BeNull(because: "no request is made");
	}

	[TestCase(null, "https://storage.googleapis.com/chrome-for-testing-public/" + HeadlessShellRelease.Version + "/win64/chrome-headless-shell-win64.zip")]
	[TestCase("https://mirror.corp/cft/ ", "https://mirror.corp/cft/" + HeadlessShellRelease.Version + "/win64/chrome-headless-shell-win64.zip")]
	[Description("The Chrome for Testing source downloads from Google by default and from the mirror when the variable is set.")]
	public void ArchiveUrl_Should_Honour_The_Mirror_Variable(string mirror, string expected) {
		// Arrange
		ChromeForTestingHeadlessShellSource source = new(name => name == HeadlessShellRelease.MirrorEnvVar ? mirror : null);

		// Act
		Uri url = source.ArchiveUrl("win64");

		// Assert
		url.Should().Be(new Uri(expected), because: "the mirror replaces only the base URL");
	}

	[TestCase("OSX", Architecture.Arm64, "mac-arm64")]
	[TestCase("OSX", Architecture.X64, "mac-x64")]
	[TestCase("WINDOWS", Architecture.X64, "win64")]
	[TestCase("WINDOWS", Architecture.Arm64, "win64")]
	[TestCase("WINDOWS", Architecture.X86, "win32")]
	[TestCase("LINUX", Architecture.X64, "linux64")]
	[TestCase("LINUX", Architecture.Arm64, "linux-arm64")]
	[Description("Each OS and architecture maps to the Chrome for Testing platform whose archive is pinned.")]
	public void PlatformKey_Should_Map_To_A_Pinned_Archive(string os, Architecture architecture, string expected) {
		// Act
		string key = HeadlessShellRelease.PlatformKey(OSPlatform.Create(os), architecture);

		// Assert
		key.Should().Be(expected, because: "the platform names the archive");
		HeadlessShellRelease.Archives.Should().ContainKey(key, because: "every supported platform has a pinned size and SHA-256");
	}
}
