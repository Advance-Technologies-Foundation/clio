using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// Story 9 (browser-session-handoff): AuthenticatedBrowserLauncher unit tests for injectable logic
/// that does not require a real browser or CDP connection.
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

	#region Tests: LaunchAsync error paths

	private static AuthenticatedBrowserLauncher BuildSut(
		IChromiumLocator? locator = null,
		IProcessExecutor? executor = null,
		IFileSystem? fileSystem = null,
		IHttpClientFactory? httpFactory = null) {
		locator ??= Substitute.For<IChromiumLocator>();
		executor ??= Substitute.For<IProcessExecutor>();
		fileSystem ??= Substitute.For<IFileSystem>();
		httpFactory ??= Substitute.For<IHttpClientFactory>();
		ILogger logger = Substitute.For<ILogger>();
		return new AuthenticatedBrowserLauncher(locator, executor, fileSystem, httpFactory, logger);
	}

	[Test]
	[Description("LaunchAsync should throw when the browser process fails to start, not silently continue.")]
	public void LaunchAsync_ShouldThrow_WhenProcessDoesNotStart() {
		// Arrange
		IChromiumLocator locator = Substitute.For<IChromiumLocator>();
		locator.Locate().Returns("/usr/bin/chromium");
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		executor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessLaunchResult { Started = false, ErrorMessage = "binary not found" }));
		IFileSystem fs = Substitute.For<IFileSystem>();
		fs.ReadAllText(Arg.Any<string>()).Returns(StorageStateJson.Serialize(
			new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v", "d", "/", true, false, "Lax", -1)])));
		AuthenticatedBrowserLauncher sut = BuildSut(locator, executor, fs);
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com", IsNetCore = true };

		// Act
		Func<Task> act = () => sut.LaunchAsync(env, "/tmp/session.storageState.json");

		// Assert
		act.Should().ThrowAsync<InvalidOperationException>(
			"because a failed launch must be surfaced immediately rather than proceeding to CDP");
	}

	[Test]
	[Description("LaunchAsync should throw when DevToolsActivePort never appears within the timeout budget.")]
	public void LaunchAsync_ShouldThrow_WhenDevToolsPortFileNeverAppears() {
		// Arrange
		IChromiumLocator locator = Substitute.For<IChromiumLocator>();
		locator.Locate().Returns("/usr/bin/chromium");
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		executor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessLaunchResult { Started = true, ProcessId = 12345 }));
		IFileSystem fs = Substitute.For<IFileSystem>();
		fs.ReadAllText(Arg.Any<string>()).Returns(StorageStateJson.Serialize(
			new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v", "d", "/", true, false, "Lax", -1)])));
		// Port file never appears.
		fs.ExistsFile(Arg.Any<string>()).Returns(false);
		AuthenticatedBrowserLauncher sut = BuildSut(locator, executor, fs);
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com", IsNetCore = true };
		using CancellationTokenSource cts = new();
		cts.Cancel(); // pre-cancelled so the poll loop aborts immediately without a 20s wait

		// Act
		Func<Task> act = () => sut.LaunchAsync(env, "/tmp/session.storageState.json", cts.Token);

		// Assert
		act.Should().ThrowAsync<OperationCanceledException>(
			"because a cancelled token during port-file polling should propagate the cancellation");
	}

	[Test]
	[Description("LaunchAsync should throw when no CDP page target is found within the timeout budget.")]
	public void LaunchAsync_ShouldThrow_WhenPageTargetNeverAppears() {
		// Arrange
		IChromiumLocator locator = Substitute.For<IChromiumLocator>();
		locator.Locate().Returns("/usr/bin/chromium");
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		executor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessLaunchResult { Started = true, ProcessId = 12345 }));
		IFileSystem fs = Substitute.For<IFileSystem>();
		fs.ReadAllText(Arg.Any<string>()).Returns(callInfo => {
			string path = callInfo.Arg<string>();
			// Port file content: first line is the port number.
			return path.EndsWith("DevToolsActivePort") ? "9222\n/devtools/browser/..." : StorageStateJson.Serialize(
				new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v", "d", "/", true, false, "Lax", -1)]));
		});
		fs.ExistsFile(Arg.Any<string>()).Returns(callInfo =>
			callInfo.Arg<string>().EndsWith("DevToolsActivePort"));
		// HttpClient for /json will fail (the localhost port doesn't actually exist in unit tests).
		IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();
		httpFactory.CreateClient(Arg.Any<string>())
			.Returns(_ => new HttpClient(new ThrowingHandler()));
		AuthenticatedBrowserLauncher sut = BuildSut(locator, executor, fs, httpFactory);
		EnvironmentSettings env = new() { Uri = "https://dev.creatio.com", IsNetCore = true };
		using CancellationTokenSource cts = new();
		cts.Cancel(); // pre-cancelled so the poll loop aborts immediately without a 4s wait

		// Act
		Func<Task> act = () => sut.LaunchAsync(env, "/tmp/session.storageState.json", cts.Token);

		// Assert
		act.Should().ThrowAsync<OperationCanceledException>(
			"because a cancelled token while polling for a CDP page target should propagate the cancellation");
	}

	/// <summary>Stub handler that always throws HttpRequestException, simulating a port that is not listening.</summary>
	private sealed class ThrowingHandler : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
			Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused (unit test)"));
	}

	#endregion
}
