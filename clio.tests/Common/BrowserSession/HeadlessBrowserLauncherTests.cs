using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>ENG-103032: the headless launcher's command line, its failure cleanup, and the DevTools target lookup.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class HeadlessBrowserLauncherTests {

	[Test]
	[Description("The browser runs headless on a loopback DevTools port with the given throw-away profile, and --no-sandbox is added only when asked for.")]
	public void BuildArguments_Should_Run_Headless_With_A_Private_Profile() {
		// Arrange
		string profile = Path.Combine(Path.GetTempPath(), HeadlessBrowserLauncher.ProfilePrefix + "test");

		// Act
		var normal = HeadlessBrowserLauncher.BuildArguments(profile, noSandbox: false);
		var root = HeadlessBrowserLauncher.BuildArguments(profile, noSandbox: true);

		// Assert
		normal.Should().Contain(["--headless=new", "--remote-debugging-port=0", $"--user-data-dir={profile}"],
			because: "the check is headless, picks a free port, and never touches the user's own profile");
		normal.Should().Contain(["--use-mock-keychain", "--password-store=basic"],
			because: "a keychain lookup for the cookie store's key can block Network.setCookie forever");
		normal.Should().NotContain("--no-sandbox", because: "the sandbox stays on unless Chromium runs as root");
		root.Should().Contain("--no-sandbox", because: "Chromium refuses to start as root with its sandbox on");
		normal[^1].Should().Be("about:blank", because: "the page starts empty until the cookies are set");
	}

	[Test]
	[Description("A launch the OS refuses fails with the browser path and deletes the profile it created.")]
	public async Task LaunchAsync_Should_Delete_The_Profile_When_The_Browser_Does_Not_Start() {
		// Arrange
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		executor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(new ProcessLaunchResult { Started = false, ErrorMessage = "denied" });
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.GetDirectories(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		HeadlessBrowserLauncher launcher = new(executor, fileSystem, Substitute.For<IFileSecurityHardening>(),
			Substitute.For<IHttpClientFactory>());

		// Act
		Func<Task> act = () => launcher.LaunchAsync("/opt/chrome", CancellationToken.None);

		// Assert
		(await act.Should().ThrowAsync<InvalidOperationException>(because: "nothing was started"))
			.Which.Message.Should().Contain("/opt/chrome", because: "the error names the browser it tried");
		fileSystem.Received(1).DeleteDirectory(
			Arg.Is<string>(path => path.Contains(HeadlessBrowserLauncher.ProfilePrefix)), true);
	}

	[Test]
	[Description("The page target's WebSocket URL is the first target of type page; a listing without one yields null.")]
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
	[Description("A DevTools session is opened only on a loopback address.")]
	public async Task ConnectAsync_Should_Refuse_A_Non_Loopback_Address() {
		// Arrange
		CdpSessionFactory factory = new();

		// Act
		Func<Task> act = () => factory.ConnectAsync(new Uri("ws://example.com:9222/devtools/page/1"), CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>(
			because: "the DevTools endpoint is unauthenticated, so only the browser clio started may be driven");
	}

	[Test]
	[Description("The profile is made owner-only before the browser starts, and the browser's stdout/stderr are discarded rather than inherited into the MCP pipe.")]
	public async Task LaunchAsync_Should_Harden_The_Profile_And_Discard_The_Browser_Output() {
		// Arrange
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		executor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(new ProcessLaunchResult { Started = false, ErrorMessage = "denied" });
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.GetDirectories(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
		IFileSecurityHardening hardening = Substitute.For<IFileSecurityHardening>();
		HeadlessBrowserLauncher launcher = new(executor, fileSystem, hardening, Substitute.For<IHttpClientFactory>());

		// Act
		Func<Task> act = () => launcher.LaunchAsync("/opt/chrome", CancellationToken.None);
		await act.Should().ThrowAsync<InvalidOperationException>(because: "the fake launch does not start");

		// Assert
		Received.InOrder(() => {
			fileSystem.CreateDirectory(Arg.Is<string>(path => path.Contains(HeadlessBrowserLauncher.ProfilePrefix)), true);
			hardening.HardenDirectory(Arg.Is<string>(path => path.Contains(HeadlessBrowserLauncher.ProfilePrefix)));
			executor.FireAndForgetAsync(Arg.Is<ProcessExecutionOptions>(options => options.DiscardOutput));
		});
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
}
