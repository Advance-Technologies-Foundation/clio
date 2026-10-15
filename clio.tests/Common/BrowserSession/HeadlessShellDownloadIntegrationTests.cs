using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ClioFileSystem = Clio.Common.FileSystem;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// ENG-103287: the REAL download of the pinned chrome-headless-shell from Chrome for Testing (about 100-125 MB), its
/// verification, and a launch of what was installed. Explicit: it downloads from Google, so it runs only on request.
/// </summary>
[TestFixture]
[Category("Integration")]
[Property("Module", "Common")]
[Explicit("Downloads about 100-125 MB from Google's Chrome for Testing.")]
[NonParallelizable]
public sealed class HeadlessShellDownloadIntegrationTests {

	[Test]
	[Description("The pinned archive for this machine downloads, verifies against the pinned size and SHA-256, installs into <root>/<version>/, and starts headless with a working DevTools session over its pipe.")]
	public async Task InstallAsync_Should_Download_Verify_And_Launch_The_Pinned_Headless_Shell() {
		// Arrange
		string root = Path.Combine(Path.GetTempPath(), "clio-headless-shell-download-" + Guid.NewGuid().ToString("N"));
		IHttpClientFactory httpClientFactory = new ServiceCollection().AddHttpClient().BuildServiceProvider()
			.GetRequiredService<IHttpClientFactory>();
		HeadlessShellInstaller installer = new(httpClientFactory, new ChromeForTestingHeadlessShellSource(),
			new System.IO.Abstractions.FileSystem(), TimeProvider.System, root, HeadlessShellRelease.CurrentPlatformKey());
		ClioFileSystem fileSystem = new(new System.IO.Abstractions.FileSystem());
		HeadlessBrowserLauncher launcher = new(new PipeBrowserSpawner(), fileSystem, new FileSecurityHardening());
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
		try {
			// Act
			string executable = await installer.InstallAsync(timeout.Token);
			TestContext.Progress.WriteLine($"installed: {executable}");
			await using IHeadlessBrowser browser = await launcher.LaunchAsync(executable, timeout.Token);
			JsonElement answer = await browser.Page.SendAsync("Runtime.evaluate",
				new { expression = "navigator.userAgent", returnByValue = true }, timeout.Token);

			// Assert
			executable.Should().StartWith(Path.Combine(root, HeadlessShellRelease.Version),
				because: "the browser is installed into the version directory");
			answer.GetProperty("result").GetProperty("value").GetString().Should().Contain("HeadlessChrome",
				because: "the installed shell starts and answers over DevTools");
		}
		finally {
			if (Directory.Exists(root)) {
				Directory.Delete(root, recursive: true);
			}
		}
	}
}
