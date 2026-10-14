using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
	[Description("A version directory without its executable (quarantined or emptied by hand) is replaced by a fresh install instead of blocking every later one; leftovers of a killed install are swept.")]
	public async Task InstallAsync_Should_Repair_A_Version_Directory_Without_Its_Executable() {
		// Arrange
		byte[] archive = Zip((Executable, 0x1ED));
		(HeadlessShellInstaller installer, _) = Create(PinOf(archive), Serve(archive));
		Directory.CreateDirectory(Path.Combine(_root, "1.2.3", "chrome-headless-shell-mac-arm64"));
		string leftover = Path.Combine(_root, ".extract-old");
		Directory.CreateDirectory(leftover);
		Directory.SetLastWriteTimeUtc(leftover, DateTime.UtcNow.AddHours(-1));

		// Act
		string executable = await installer.InstallAsync(CancellationToken.None);

		// Assert
		File.Exists(executable).Should().BeTrue(because: "the broken install is replaced");
		Directory.Exists(leftover).Should().BeFalse(because: "a killed install's leftovers do not pile up");
	}

	[Test]
	[Description("A connection reset while the body streams, before any byte arrived, is reported as an install failure, not as a raw IOException.")]
	public async Task InstallAsync_Should_Report_A_Broken_Stream_As_An_Install_Failure() {
		// Arrange
		(HeadlessShellInstaller installer, _) = Create(new HeadlessShellRelease.Archive(10, "x"),
			(_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) }));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "page-smoke answers browser-download-failed"))
			.Which.Message.Should().Contain("broke off").And.Contain("nothing arrived on this call",
				because: "a transfer that broke before any byte arrived is a failure the agent reports, not progress");
	}

	private sealed class BrokenStream : Stream {
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => 0; set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection reset");
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
	[Description("An archive that matches its pin but is not a readable zip is reported as an install failure, not as a raw InvalidDataException.")]
	public async Task InstallAsync_Should_Report_A_Corrupt_Archive_As_An_Install_Failure() {
		// Arrange
		byte[] archive = System.Text.Encoding.ASCII.GetBytes("not a zip archive at all");
		(HeadlessShellInstaller installer, _) = Create(PinOf(archive), Serve(archive));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "page-smoke answers browser-download-failed"))
			.Which.Message.Should().Contain("nothing was installed", because: "the caller learns the state and that a retry is safe");
		installer.FindInstalled().Should().BeNull(because: "nothing was installed");
	}

	[Test]
	[Description("A mirror URL with user:password never puts the credentials into the error text that reaches the MCP reply.")]
	public async Task InstallAsync_Should_Not_Echo_Mirror_Credentials() {
		// Arrange
		StubHandler handler = new((_, _) => throw new HttpRequestException("No such host is known."));
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(HeadlessShellInstaller.HttpClientName).Returns(_ => new HttpClient(handler));
		ChromeForTestingHeadlessShellSource source = new(name =>
			name == HeadlessShellRelease.MirrorEnvVar ? "https://agent:s3cr3t@mirror.corp:8443/cft" : null);
		HeadlessShellInstaller installer = new(factory, source, new System.IO.Abstractions.FileSystem(), TimeProvider.System,
			_root, "win64");

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		string message = (await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "the mirror is unreachable"))
			.Which.Message;
		message.Should().Contain("https://mirror.corp:8443", because: "the caller still learns which host failed");
		message.Should().NotContain("s3cr3t").And.NotContain("agent:", because: "mirror credentials never reach the reply");
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
		File.Exists(Path.Combine(_root, "escaped")).Should().BeFalse(
			because: "'../escaped' from the extraction folder would land in the browsers root, and nothing may");
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
			.Which.Message.Should().Contain("the call's 90 s ran out; nothing arrived on this call", because: "the caller learns why");
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
	// Serves the first `served` bytes of a body, then waits until the call is cancelled - a link too slow for one call.
	private sealed class StallingStream(byte[] body, int served, TaskCompletionSource stalled) : Stream {
		private int _position;
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => _position; set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
			if (_position < served) {
				int count = Math.Min(buffer.Length, served - _position);
				body.AsMemory(_position, count).CopyTo(buffer);
				_position += count;
				return count;
			}
			stalled.TrySetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return 0;
		}
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	[Test]
	[Description("On a link too slow for one call, the part that arrived is kept when the 90 s budget ends (on the injected clock), the reply says how much, and the next call resumes it with an HTTP Range request instead of starting over.")]
	public async Task InstallAsync_Should_Keep_What_Arrived_And_Resume_It_With_Range() {
		// Arrange
		byte[] archive = Zip((Executable, 0x1ED), ("chrome-headless-shell-mac-arm64/ABOUT", 0x1A4));
		int half = archive.Length / 2;
		ManualTimeProvider time = new();
		TaskCompletionSource stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
		string secondRange = null;
		int calls = 0;
		(HeadlessShellInstaller installer, _) = Create(PinOf(archive), (request, _) => {
			if (++calls == 1) {
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
					Content = new StreamContent(new StallingStream(archive, half, stalled))
				});
			}
			secondRange = request.Headers.Range?.ToString();
			long from = request.Headers.Range!.Ranges.Single().From!.Value;
			ByteArrayContent rest = new(archive[(int)from..]);
			rest.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, archive.Length - 1, archive.Length);
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = rest });
		}, time);
		Task<string> first = installer.InstallAsync(CancellationToken.None);
		await stalled.Task.WaitAsync(TimeSpan.FromSeconds(30));

		// Act
		time.Advance(HeadlessShellInstaller.DownloadBudget);
		Func<Task> firstCall = () => first.WaitAsync(TimeSpan.FromSeconds(30));
		HeadlessShellDownloadIncompleteException incomplete =
			(await firstCall.Should().ThrowAsync<HeadlessShellDownloadIncompleteException>(because: "the link is too slow for one call")).Which;
		long keptAfterFirstCall = PartialLength();
		string executable = await installer.InstallAsync(CancellationToken.None);

		// Assert
		incomplete.DownloadedBytes.Should().Be(half, because: "the reply says how much arrived");
		incomplete.TotalBytes.Should().Be(archive.Length, because: "and how much there is in all");
		keptAfterFirstCall.Should().Be(half, because: "what arrived survives the timeout");
		secondRange.Should().Be($"bytes={half}-", because: "the next call asks only for the rest");
		File.Exists(executable).Should().BeTrue(because: "the resumed archive verifies and installs");
		Directory.GetFileSystemEntries(_root).Should().ContainSingle(because: "the partial file is removed once installed");
	}

	[Test]
	[Description("A server that ignores Range and sends the whole archive (200) restarts the kept partial file from zero, and the install still verifies.")]
	public async Task InstallAsync_Should_Start_Over_When_The_Server_Ignores_Range() {
		// Arrange
		byte[] archive = Zip((Executable, 0x1ED));
		(HeadlessShellInstaller installer, StubHandler handler) = Create(PinOf(archive), Serve(archive));
		File.WriteAllBytes(Path.Combine(_root, ".partial-1.2.3-mac-arm64.zip"), archive[..10]);

		// Act
		string executable = await installer.InstallAsync(CancellationToken.None);

		// Assert
		File.Exists(executable).Should().BeTrue(because: "the full body replaced the partial one");
	}

	[TestCase("mirror.corp/cft")]
	[TestCase("ftp://mirror.corp/cft")]
	[Description("A malformed CLIO_CHROME_FOR_TESTING_MIRROR is refused with an install failure that names the variable, not a raw UriFormatException.")]
	public void ArchiveUrl_Should_Refuse_A_Malformed_Mirror(string mirror) {
		// Arrange
		ChromeForTestingHeadlessShellSource source = new(name => name == HeadlessShellRelease.MirrorEnvVar ? mirror : null);

		// Act
		Action act = () => source.ArchiveUrl("win64");

		// Assert
		act.Should().Throw<HeadlessShellInstallException>(because: "page-smoke answers browser-download-failed")
			.WithMessage("*" + HeadlessShellRelease.MirrorEnvVar + " must be an absolute http(s) URL*",
				because: "the caller learns which variable to fix");
	}

	private long PartialLength() {
		string partial = Path.Combine(_root, ".partial-1.2.3-mac-arm64.zip");
		if (!File.Exists(partial)) {
			return -1;
		}
		using FileStream stream = new(partial, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return stream.Length;
	}

	[Test]
	[Description("Offline with a partial download kept from an earlier call: this call added nothing, so it is a failure (browser-download-failed), not progress the agent would loop on; the kept part stays for later.")]
	public async Task InstallAsync_Should_Fail_When_This_Call_Added_Nothing_To_A_Kept_Download() {
		// Arrange
		byte[] archive = Zip((Executable, 0x1ED));
		string partial = Path.Combine(_root, ".partial-1.2.3-mac-arm64.zip");
		File.WriteAllBytes(partial, archive[..10]);
		(HeadlessShellInstaller installer, _) = Create(PinOf(archive), (_, _) => throw new HttpRequestException("No such host is known."));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		HeadlessShellInstallException failure = (await act.Should().ThrowAsync<HeadlessShellInstallException>(
			because: "the network is unreachable")).Which;
		failure.Should().NotBeOfType<HeadlessShellDownloadIncompleteException>(
			because: "nothing arrived on this call, so calling again would not make progress");
		failure.Message.Should().Contain("nothing arrived on this call", because: "the agent learns why it should stop");
		new FileInfo(partial).Length.Should().Be(10, because: "the earlier progress is kept for when the network works again");
	}

	[TestCase(HttpStatusCode.RequestedRangeNotSatisfiable)]
	[TestCase(HttpStatusCode.Forbidden)]
	[Description("A refused resume (416, or any other non-success answer to the Range request) drops the kept part, so the next call starts clean.")]
	public async Task InstallAsync_Should_Drop_The_Kept_Part_When_The_Resume_Is_Refused(HttpStatusCode status) {
		// Arrange
		byte[] archive = Zip((Executable, 0x1ED));
		string partial = Path.Combine(_root, ".partial-1.2.3-mac-arm64.zip");
		File.WriteAllBytes(partial, archive[..10]);
		(HeadlessShellInstaller installer, StubHandler handler) = Create(PinOf(archive),
			(_, _) => Task.FromResult(new HttpResponseMessage(status)));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "the server refused the resume");
		(File.Exists(partial) ? new FileInfo(partial).Length : 0).Should().Be(0, because: "the next call starts from zero");
	}

	[Test]
	[Description("A proxy that asks for a login (407) is answered with a hint to put credentials into HTTPS_PROXY, since clio never sends the Windows login to a system or auto-detected proxy.")]
	public async Task InstallAsync_Should_Explain_A_Proxy_That_Asks_For_A_Login() {
		// Arrange
		(HeadlessShellInstaller installer, _) = Create(new HeadlessShellRelease.Archive(1, "x"),
			(_, _) => throw new HttpRequestException("The proxy tunnel request to proxy failed with status code '407'.", null,
				HttpStatusCode.ProxyAuthenticationRequired));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "the proxy refused the download"))
			.Which.Message.Should().Contain("HTTP 407").And.Contain("set HTTPS_PROXY to the proxy with its credentials",
				because: "the user learns how to authenticate without clio handing out the Windows login");
	}

	[TestCase(true, TestName = "net10 shape: StatusCode set")]
	[TestCase(false, TestName = "net8 shape: no StatusCode, only the message")]
	[Description("A proxy-tunnel 407 whose .NET message names the proxy URI with its user:password never puts the password into the answer, gets the login hint, and is not called offline.")]
	public async Task InstallAsync_Should_Not_Echo_A_Proxy_Password_On_407(bool withStatusCode) {
		// Arrange - the message .NET writes for a refused tunnel, with the URI it was handed.
		const string message = "The proxy tunnel request to proxy 'http://agent:s3cr3t@proxy.corp:3128/' failed with status code '407'.";
		(HeadlessShellInstaller installer, _) = Create(new HeadlessShellRelease.Archive(1, "x"), (_, _) =>
			throw (withStatusCode
				? new HttpRequestException(message, null, HttpStatusCode.ProxyAuthenticationRequired)
				: new HttpRequestException(message)));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		string answer = (await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "the proxy refused the tunnel"))
			.Which.Message;
		answer.Should().NotContain("s3cr3t").And.NotContain("agent:", because: "a proxy password never reaches the MCP reply");
		answer.Should().Contain("set HTTPS_PROXY to the proxy with its credentials", because: "the user learns how to log in");
		answer.Should().NotContain("may be offline", because: "a 407 is not an offline machine");
	}

	[Test]
	[Description("A port that merely contains 407 (:4070) is not taken for a proxy login.")]
	public async Task InstallAsync_Should_Not_Take_A_Port_4070_For_A_Proxy_Login() {
		// Arrange
		(HeadlessShellInstaller installer, _) = Create(new HeadlessShellRelease.Archive(1, "x"), (_, _) =>
			throw new HttpRequestException("Connection refused (mirror.corp:4070)"));

		// Act
		Func<Task> act = () => installer.InstallAsync(CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<HeadlessShellInstallException>(because: "the host refused the connection"))
			.Which.Message.Should().NotContain("HTTP 407").And.Contain("may be offline",
				because: "only the real 407 phrase means a proxy login");
	}
}
