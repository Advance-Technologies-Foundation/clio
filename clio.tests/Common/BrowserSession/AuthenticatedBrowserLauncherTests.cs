using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// AuthenticatedBrowserLauncher unit tests for injectable logic that does not require a real browser.
/// After Story 1 (ai-business-process-generation) the CDP plumbing lives in <see cref="ICdpSession"/>,
/// so the launcher is exercised here with a mocked session — behavior must stay identical to Mode A.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class AuthenticatedBrowserLauncherTests {

	#region Tests: BuildShellUrl

	[Test]
	[Description("BuildShellUrl should append /Shell/ for NetCore environments to honour the session cookie.")]
	public void BuildShellUrl_ShouldUseShellPathWithoutPrefix_WhenNetCore() {
		// Arrange
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com", IsNetCore = true };

		// Act
		string result = AuthenticatedBrowserLauncher.BuildShellUrl(env);

		// Assert
		result.Should().Be("https://dev.creatio.com/Shell/",
			"because NetCore Creatio does not use the 0/ WebApp alias");
	}

	[Test]
	[Description("BuildShellUrl should append /0/Shell/ for NetFramework environments so the WebApp alias is honoured.")]
	public void BuildShellUrl_ShouldUseShellPathWithPrefix_WhenNetFramework() {
		// Arrange
		EnvironmentSettings env = new() { Uri = "https://on-prem.example.com", IsNetCore = false };

		// Act
		string result = AuthenticatedBrowserLauncher.BuildShellUrl(env);

		// Assert
		result.Should().Be("https://on-prem.example.com/0/Shell/",
			"because .NET Framework Creatio requires the 0/ WebApp alias prefix");
	}

	[Test]
	[Description("BuildShellUrl should strip a trailing slash from env.Uri to avoid double slashes.")]
	public void BuildShellUrl_ShouldStripTrailingSlash_WhenUriHasTrailingSlash() {
		// Arrange
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com/", IsNetCore = true };

		// Act
		string result = AuthenticatedBrowserLauncher.BuildShellUrl(env);

		// Assert
		result.Should().Be("https://dev.creatio.com/Shell/",
			"because a double slash would produce a malformed URL");
	}

	#endregion

	#region Tests: BuildSetCookieParams

	[Test]
	[Description("BuildSetCookieParams should use the cookie domain when one is present.")]
	public void BuildSetCookieParams_ShouldUseDomain_WhenCookieHasDomain() {
		// Arrange
		BrowserCookie cookie = new(".ASPXAUTH", "token", "dev.creatio.com", "/", true, false, "Lax", -1);

		// Act
		Dictionary<string, object> result = AuthenticatedBrowserLauncher.BuildSetCookieParams(cookie, "https://fallback.url");

		// Assert
		result.Should().ContainKey("domain", "because a cookie with a domain must be set on that domain")
			.WhoseValue.Should().Be("dev.creatio.com");
		result.Should().NotContainKey("url", "because url is only the fallback when domain is absent");
	}

	[Test]
	[Description("BuildSetCookieParams should fall back to the URL when the cookie has no domain.")]
	public void BuildSetCookieParams_ShouldUseUrl_WhenCookieHasNoDomain() {
		// Arrange
		BrowserCookie cookie = new("SessionCookie", "val", "", "/", false, false, "None", -1);

		// Act
		Dictionary<string, object> result = AuthenticatedBrowserLauncher.BuildSetCookieParams(cookie, "https://dev.creatio.com");

		// Assert
		result.Should().ContainKey("url", "because a cookie without a domain must be anchored to a URL")
			.WhoseValue.Should().Be("https://dev.creatio.com");
		result.Should().NotContainKey("domain", "because no domain was provided");
	}

	[Test]
	[Description("BuildSetCookieParams should propagate httpOnly=true so CDP preserves the HttpOnly flag.")]
	public void BuildSetCookieParams_ShouldSetHttpOnly_WhenCookieIsHttpOnly() {
		// Arrange
		BrowserCookie cookie = new(".ASPXAUTH", "token", "dev.creatio.com", "/", HttpOnly: true, Secure: false, "Lax", -1);

		// Act
		Dictionary<string, object> result = AuthenticatedBrowserLauncher.BuildSetCookieParams(cookie, "https://fallback");

		// Assert
		result.Should().ContainKey("httpOnly", "because HttpOnly cookies cannot be set via document.cookie — that is the whole point")
			.WhoseValue.Should().Be(true);
	}

	#endregion

	#region Tests: LaunchAndKeepOpenAsync

	private static IFileSystem FsWithPortAndCookies(string port = "9222") {
		IFileSystem fs = Substitute.For<IFileSystem>();
		fs.ReadAllText(Arg.Any<string>()).Returns(callInfo => {
			string path = callInfo.Arg<string>();
			return path.EndsWith("DevToolsActivePort")
				? $"{port}\n/devtools/browser/abc"
				: StorageStateJson.Serialize(
					new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v", "d", "/", true, false, "Lax", -1)]));
		});
		fs.ExistsFile(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>().EndsWith("DevToolsActivePort"));
		return fs;
	}

	private static IProcessExecutor StartedExecutor() {
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		executor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessLaunchResult { Started = true, ProcessId = 12345 }));
		return executor;
	}

	private static AuthenticatedBrowserLauncher BuildSut(
		IChromiumLocator locator = null,
		IProcessExecutor executor = null,
		IFileSystem fileSystem = null,
		ICdpSession cdpSession = null) {
		locator ??= Substitute.For<IChromiumLocator>();
		locator.Locate().Returns("/usr/bin/chromium");
		executor ??= Substitute.For<IProcessExecutor>();
		fileSystem ??= Substitute.For<IFileSystem>();
		cdpSession ??= Substitute.For<ICdpSession>();
		ILogger logger = Substitute.For<ILogger>();
		return new AuthenticatedBrowserLauncher(locator, executor, fileSystem, cdpSession, logger);
	}

	[Test]
	[Description("LaunchAndKeepOpenAsync should throw when the browser process fails to start, not silently continue.")]
	public async Task LaunchAndKeepOpenAsync_ShouldThrow_WhenProcessDoesNotStart() {
		// Arrange
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		executor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessLaunchResult { Started = false, ErrorMessage = "binary not found" }));
		AuthenticatedBrowserLauncher sut = BuildSut(executor: executor, fileSystem: FsWithPortAndCookies());
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com", IsNetCore = true };

		// Act
		Func<Task> act = () => sut.LaunchAndKeepOpenAsync(env, "/tmp/session.storageState.json");

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>(
			"because a failed launch must be surfaced immediately rather than proceeding to CDP");
	}

	[Test]
	[Description("LaunchAndKeepOpenAsync should propagate cancellation when DevToolsActivePort never appears.")]
	public async Task LaunchAndKeepOpenAsync_ShouldThrow_WhenDevToolsPortFileNeverAppears() {
		// Arrange
		IFileSystem fs = Substitute.For<IFileSystem>();
		fs.ReadAllText(Arg.Any<string>()).Returns(StorageStateJson.Serialize(
			new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v", "d", "/", true, false, "Lax", -1)])));
		fs.ExistsFile(Arg.Any<string>()).Returns(false); // Port file never appears.
		AuthenticatedBrowserLauncher sut = BuildSut(executor: StartedExecutor(), fileSystem: fs);
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com", IsNetCore = true };
		using CancellationTokenSource cts = new();
		cts.Cancel(); // pre-cancelled so the poll loop aborts immediately without a 20s wait

		// Act
		Func<Task> act = () => sut.LaunchAndKeepOpenAsync(env, "/tmp/session.storageState.json", cts.Token);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			"because a cancelled token during port-file polling should propagate the cancellation");
	}

	[Test]
	[Description("LaunchAndKeepOpenAsync should rethrow when the CDP session fails to connect (and not leave a stale browser unhandled).")]
	public async Task LaunchAndKeepOpenAsync_ShouldThrow_WhenCdpSessionConnectFails() {
		// Arrange
		ICdpSession cdpSession = Substitute.For<ICdpSession>();
		cdpSession.ConnectAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("could not obtain a CDP page target")));
		AuthenticatedBrowserLauncher sut = BuildSut(
			executor: StartedExecutor(), fileSystem: FsWithPortAndCookies(), cdpSession: cdpSession);
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com", IsNetCore = true };

		// Act
		Func<Task> act = () => sut.LaunchAndKeepOpenAsync(env, "/tmp/session.storageState.json");

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>(
			"because a CDP connect failure must surface rather than reporting a successful authenticated launch");
	}

	[Test]
	[Description("LaunchAndKeepOpenAsync should connect, inject cookies, navigate, and return the chosen DevTools port.")]
	public async Task LaunchAndKeepOpenAsync_ShouldInjectCookiesNavigateAndReturnPort_WhenLaunchSucceeds() {
		// Arrange
		ICdpSession cdpSession = Substitute.For<ICdpSession>();
		AuthenticatedBrowserLauncher sut = BuildSut(
			executor: StartedExecutor(), fileSystem: FsWithPortAndCookies("9222"), cdpSession: cdpSession);
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com", IsNetCore = true };

		// Act
		LaunchResult result = await sut.LaunchAndKeepOpenAsync(env, "/tmp/session.storageState.json");

		// Assert
		result.DevToolsPort.Should().Be(9222,
			because: "the launcher must expose the port it read from DevToolsActivePort so a driver can re-attach");
		await cdpSession.Received(1).ConnectAsync(9222, Arg.Any<CancellationToken>());
		await cdpSession.Received().SendAsync("Network.setCookie", Arg.Any<object>(), Arg.Any<CancellationToken>());
		await cdpSession.Received().SendAsync("Page.navigate", Arg.Any<object>(), Arg.Any<CancellationToken>());
	}

	#endregion
}
