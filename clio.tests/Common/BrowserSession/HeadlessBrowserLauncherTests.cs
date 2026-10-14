using System;
using System.Collections.Generic;
using Win32Exception = System.ComponentModel.Win32Exception;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// ENG-103032, ENG-103330: the headless launcher's command line, its failure cleanup, the page attach over the DevTools
/// pipe, and the DevTools target lookup the visible launcher still uses.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class HeadlessBrowserLauncherTests {

	private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

	[Test]
	[Description("The browser runs headless with the given throw-away profile and no debugging port, and --no-sandbox is added only when asked for.")]
	public void BuildArguments_Should_Run_Headless_With_A_Private_Profile_And_No_Port() {
		// Arrange
		string profile = Path.Combine(Path.GetTempPath(), HeadlessBrowserLauncher.ProfilePrefix + "test");

		// Act
		IReadOnlyList<string> normal = HeadlessBrowserLauncher.BuildArguments(profile, noSandbox: false);
		IReadOnlyList<string> root = HeadlessBrowserLauncher.BuildArguments(profile, noSandbox: true);

		// Assert
		normal.Should().Contain(["--headless=new", $"--user-data-dir={profile}"],
			because: "the check is headless and never touches the user's own profile");
		normal.Should().NotContain(argument => argument.StartsWith("--remote-debugging-port"),
			because: "an open DevTools port lets any local process attach and read the session cookies");
		normal.Should().Contain(["--use-mock-keychain", "--password-store=basic"],
			because: "a keychain lookup for the cookie store's key can block Network.setCookie forever");
		normal.Should().NotContain("--no-sandbox", because: "the sandbox stays on unless Chromium runs as root");
		root.Should().Contain("--no-sandbox", because: "Chromium refuses to start as root with its sandbox on");
		normal[^1].Should().Be("about:blank", because: "the page starts empty until the cookies are set");
	}

	[Test]
	[Description("A pipe spawn the OS refuses fails with the browser path, says there is no port fallback, and deletes the profile it created.")]
	public async Task LaunchAsync_Should_Fail_Cleanly_Without_A_Port_Fallback_When_The_Spawn_Is_Refused() {
		// Arrange
		IPipeBrowserSpawner spawner = Substitute.For<IPipeBrowserSpawner>();
		spawner.Start(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Throws(new Win32Exception(13, "denied"));
		IFileSystem fileSystem = FileSystemWithNoLeftovers();
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		HeadlessBrowserLauncher launcher = new(spawner, fileSystem, Substitute.For<IFileSecurityHardening>());

		// Act
		Func<Task> act = () => launcher.LaunchAsync("/opt/chrome", CancellationToken.None);

		// Assert
		string message = (await act.Should().ThrowAsync<InvalidOperationException>(because: "nothing was started"))
			.Which.Message;
		message.Should().Contain("/opt/chrome", because: "the error names the browser it tried");
		message.Should().Contain("never falls back to an open debugging port",
			because: "a refused pipe must not quietly reopen the port this change closed");
		spawner.Received(1).Start("/opt/chrome", Arg.Any<IReadOnlyList<string>>());
		fileSystem.Received(1).DeleteDirectory(
			Arg.Is<string>(path => path.Contains(HeadlessBrowserLauncher.ProfilePrefix)), true);
	}

	[Test]
	[Description("The profile is made owner-only before the browser starts.")]
	public async Task LaunchAsync_Should_Harden_The_Profile_Before_The_Browser_Starts() {
		// Arrange
		IPipeBrowserSpawner spawner = Substitute.For<IPipeBrowserSpawner>();
		spawner.Start(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Throws(new Win32Exception(2, "missing"));
		IFileSystem fileSystem = FileSystemWithNoLeftovers();
		IFileSecurityHardening hardening = Substitute.For<IFileSecurityHardening>();
		HeadlessBrowserLauncher launcher = new(spawner, fileSystem, hardening);

		// Act
		Func<Task> act = () => launcher.LaunchAsync("/opt/chrome", CancellationToken.None);
		await act.Should().ThrowAsync<InvalidOperationException>(because: "the fake spawn does not start");

		// Assert
		Received.InOrder(() => {
			fileSystem.CreateDirectory(Arg.Is<string>(path => path.Contains(HeadlessBrowserLauncher.ProfilePrefix)), true);
			hardening.HardenDirectory(Arg.Is<string>(path => path.Contains(HeadlessBrowserLauncher.ProfilePrefix)));
			spawner.Start("/opt/chrome", Arg.Any<IReadOnlyList<string>>());
		});
	}

	[Test]
	[Description("Over the pipe the launcher finds the page target, attaches to it as a flat session, and every later page command carries that session id; disposing the browser closes the pipe, waits for the process and releases it.")]
	public async Task LaunchAsync_Should_Attach_To_The_Page_Over_The_Pipe() {
		// Arrange
		using FakePipeBrowser fake = new();
		List<JsonElement> commands = [];
		Task browserSide = fake.ServeAsync(command => {
			commands.Add(command);
			string method = command.GetProperty("method").GetString();
			int id = command.GetProperty("id").GetInt32();
			return method switch {
				"Target.getTargets" when commands.Count == 1 => $$$"""{"id":{{{id}}},"result":{"targetInfos":[]}}""",
				"Target.getTargets" => $$$"""{"id":{{{id}}},"result":{"targetInfos":[{"targetId":"W1","type":"service_worker"},{"targetId":"P1","type":"page"}]}}""",
				"Target.attachToTarget" => $$$"""{"id":{{{id}}},"result":{"sessionId":"S1"}}""",
				_ => $$$"""{"id":{{{id}}},"result":{"value":7},"sessionId":"S1"}"""
			};
		});
		IFileSystem fileSystem = FileSystemWithNoLeftovers();
		HeadlessBrowserLauncher launcher = new(fake.Spawner, fileSystem, Substitute.For<IFileSecurityHardening>());

		// Act
		IHeadlessBrowser browser = await launcher.LaunchAsync("/opt/chrome", CancellationToken.None).WaitAsync(Guard);
		JsonElement answer = await browser.Page.SendAsync("Runtime.evaluate", new { expression = "7" }, CancellationToken.None)
			.WaitAsync(Guard);
		await browser.DisposeAsync();
		await browserSide.WaitAsync(Guard);

		// Assert
		answer.GetProperty("value").GetInt32().Should().Be(7, because: "the page answered the routed command");
		commands.Should().HaveCount(4, because: "two target listings (the page appears late), one attach, one page command");
		commands[2].GetProperty("params").GetProperty("targetId").GetString().Should().Be("P1",
			because: "only a page target is driven");
		commands[2].GetProperty("params").GetProperty("flatten").GetBoolean().Should().BeTrue(
			because: "a flat session is addressed by a sessionId on each message over the one pipe");
		commands[0].TryGetProperty("sessionId", out _).Should().BeFalse(because: "the target listing is a browser command");
		commands[3].GetProperty("sessionId").GetString().Should().Be("S1", because: "page commands go to the attached page");
		fake.Process.WaitedForExit.Should().BeTrue(because: "disposal waits for (and on Unix reaps) the browser");
		fake.Process.Released.Should().BeTrue(because: "the native process handle is released");
	}

	[Test]
	[Description("A browser that exits before answering over its pipe fails the launch at once with a clear error, and the profile and the process are cleaned up.")]
	public async Task LaunchAsync_Should_Fail_Fast_When_The_Browser_Closes_Its_Pipe() {
		// Arrange
		using FakePipeBrowser fake = new();
		fake.CloseFromBrowser();
		IFileSystem fileSystem = FileSystemWithNoLeftovers();
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		HeadlessBrowserLauncher launcher = new(fake.Spawner, fileSystem, Substitute.For<IFileSecurityHardening>());

		// Act
		Func<Task> act = () => launcher.LaunchAsync("/opt/chrome", CancellationToken.None).WaitAsync(Guard);

		// Assert
		(await act.Should().ThrowAsync<InvalidOperationException>(because: "the browser never answered"))
			.Which.Message.Should().Contain("exited, or closed its DevTools pipe, before it answered",
				because: "a closed pipe is reported as the browser being gone, not as a 30 s timeout");
		fileSystem.Received().DeleteDirectory(
			Arg.Is<string>(path => path.Contains(HeadlessBrowserLauncher.ProfilePrefix)), true);
		fake.Process.Released.Should().BeTrue(because: "a failed launch still releases the process");
	}

	[Test]
	[Description("The page target is the first target of type page in a Target.getTargets result; a result without one yields null.")]
	public void FindPageTargetId_Should_Pick_The_First_Page_Target() {
		// Arrange
		using JsonDocument listing = JsonDocument.Parse("""
			{"targetInfos":[{"targetId":"B","type":"browser"},{"targetId":"P","type":"page"},{"targetId":"Q","type":"page"}]}
			""");
		using JsonDocument empty = JsonDocument.Parse("""{"targetInfos":[{"targetId":"B","type":"browser"}]}""");

		// Act
		string found = HeadlessBrowserLauncher.FindPageTargetId(listing.RootElement);
		string none = HeadlessBrowserLauncher.FindPageTargetId(empty.RootElement);

		// Assert
		found.Should().Be("P", because: "only a page target can be driven");
		none.Should().BeNull(because: "the browser has no page yet");
	}

	[Test]
	[Description("The visible launcher's page target lookup: the first target of type page; a listing without one yields null.")]
	public void FindPageWebSocketUrl_Should_Pick_The_First_Page_Target() {
		// Arrange
		const string listing = """
			[{"type":"service_worker","webSocketDebuggerUrl":"ws://127.0.0.1:9/devtools/sw"},
			 {"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1:9/devtools/page/1"}]
			""";

		// Act
		string found = DevToolsEndpoint.FindPageWebSocketUrl(listing);
		string none = DevToolsEndpoint.FindPageWebSocketUrl("""[{"type":"browser"}]""");

		// Assert
		found.Should().Be("ws://127.0.0.1:9/devtools/page/1", because: "only a page target can be driven");
		none.Should().BeNull(because: "the listing has no page target yet");
	}

	[Test]
	[Description("A process is the launched browser only while its start time matches the recorded one; a reused id with a later start is a stranger and is not killed.")]
	public void IsSameProcess_Should_Match_Only_The_Recorded_Start() {
		// Arrange
		DateTimeOffset started = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

		// Act
		bool same = HeadlessBrowserLauncher.IsSameProcess(started, started.AddMilliseconds(400));
		bool reused = HeadlessBrowserLauncher.IsSameProcess(started, started.AddSeconds(40));

		// Assert
		same.Should().BeTrue(because: "the two reads of one start time differ only by their precision");
		reused.Should().BeFalse(because: "a process that took the id after the browser died started later, and killing its tree would hit a stranger");
	}

	[Test]
	[Description("A DevToolsActivePort read that fails with an IOException (Windows sharing violation while Chromium writes it) or access denied (delete-pending) is 'not yet': polling continues and the port is read on a later attempt.")]
	public async Task ReadPortAsync_Should_Keep_Polling_Through_An_IOException() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		int reads = 0;
		fileSystem.ReadAllText(Arg.Any<string>()).Returns(_ => ++reads switch {
			1 => throw new IOException("The process cannot access the file because it is being used by another process."),
			2 => throw new UnauthorizedAccessException("Access to the path is denied."),
			_ => "9222\n/devtools/browser/1"
		});

		// Act
		int port = await DevToolsEndpoint.ReadPortAsync(fileSystem, "/tmp/profile", 4, 1, CancellationToken.None);

		// Assert
		port.Should().Be(9222, because: "a sharing violation is a file not written yet, not a failed launch");
		reads.Should().Be(3, because: "the third read found the port");
	}

	private static IFileSystem FileSystemWithNoLeftovers() {
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.GetDirectories(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
		return fileSystem;
	}

	/// <summary>An in-process stand-in for a browser on the far side of a DevTools pipe pair.</summary>
	private sealed class FakePipeBrowser : IDisposable {

		private readonly AnonymousPipeServerStream _toBrowser = new(PipeDirection.Out);
		private readonly AnonymousPipeClientStream _browserReads;
		private readonly AnonymousPipeServerStream _fromBrowser = new(PipeDirection.In);
		private readonly AnonymousPipeClientStream _browserWrites;

		internal FakePipeBrowser() {
			_browserReads = new AnonymousPipeClientStream(PipeDirection.In, _toBrowser.ClientSafePipeHandle);
			_browserWrites = new AnonymousPipeClientStream(PipeDirection.Out, _fromBrowser.ClientSafePipeHandle);
			Process = new FakeProcess();
			Spawner = Substitute.For<IPipeBrowserSpawner>();
			Spawner.Start(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Returns(_ => new PipeBrowserProcess(
				-1, null, _fromBrowser, _toBrowser, Process.WaitForExit, Process.Release));
		}

		internal IPipeBrowserSpawner Spawner { get; }

		internal FakeProcess Process { get; }

		internal void CloseFromBrowser() => _browserWrites.Dispose();

		// Answers each NUL-terminated command until the client closes its command pipe.
		internal Task ServeAsync(Func<JsonElement, string> answer) => Task.Run(async () => {
			List<byte> pending = [];
			byte[] buffer = new byte[4096];
			while (true) {
				int read = await _browserReads.ReadAsync(buffer);
				if (read == 0) {
					_browserWrites.Dispose();
					return;
				}
				for (int index = 0; index < read; index++) {
					if (buffer[index] != 0) {
						pending.Add(buffer[index]);
						continue;
					}
					using JsonDocument command = JsonDocument.Parse(pending.ToArray());
					pending.Clear();
					byte[] reply = Encoding.UTF8.GetBytes(answer(command.RootElement.Clone()) + "\0");
					await _browserWrites.WriteAsync(reply);
					await _browserWrites.FlushAsync();
				}
			}
		});

		public void Dispose() {
			_browserReads.Dispose();
			_browserWrites.Dispose();
			_toBrowser.Dispose();
			_fromBrowser.Dispose();
		}
	}

	private sealed class FakeProcess {

		internal bool WaitedForExit { get; private set; }

		internal bool Released { get; private set; }

		internal bool WaitForExit(int milliseconds) {
			WaitedForExit = true;
			return true;
		}

		internal void Release() => Released = true;
	}
}
