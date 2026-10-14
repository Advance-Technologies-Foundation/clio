using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// ENG-103287: the browsers Playwright (Playwright MCP included) and Puppeteer already downloaded are found in each OS's
/// cache layout, headless shells first and newest first, with the tools' own cache-location variables honoured. Each case
/// builds the layout in a temporary directory.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class BrowserCacheLocatorTests {

	private string _root;

	[SetUp]
	public void SetUp() {
		_root = Path.Combine(Path.GetTempPath(), "clio-browser-cache-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, recursive: true);
		}
	}

	private string Touch(params string[] parts) {
		string path = Path.Combine([_root, .. parts]);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, string.Empty);
		return path;
	}

	// A Playwright revision folder with its completion marker and an executable at the given relative path.
	private string PlaywrightBuild(string cache, string folder, params string[] executable) {
		Touch([cache, folder, "INSTALLATION_COMPLETE"]);
		return Touch([cache, folder, .. executable]);
	}

	private BrowserCacheLocator Locator(OSPlatform platform, Dictionary<string, string> environment) =>
		new(new System.IO.Abstractions.FileSystem(), name => environment.GetValueOrDefault(name), platform);

	[Test]
	[Description("macOS: Playwright's ~/Library/Caches/ms-playwright and Puppeteer's ~/.cache/puppeteer are searched; headless shells come first, newest first, then full Chromium builds; an incomplete Playwright download is skipped.")]
	public void FindCached_Should_Order_Headless_Shells_First_And_Newest_First_On_MacOs() {
		// Arrange
		string[] playwright = ["home", "Library", "Caches", "ms-playwright"];
		string[] puppeteer = ["home", ".cache", "puppeteer"];
		string shell1208 = PlaywrightBuild(Path.Combine(playwright), "chromium_headless_shell-1208",
			"chrome-headless-shell-mac-arm64", "chrome-headless-shell");
		string shell1243 = PlaywrightBuild(Path.Combine(playwright), "chromium_headless_shell-1243",
			"chrome-headless-shell-mac-arm64", "chrome-headless-shell");
		string chromium1243 = PlaywrightBuild(Path.Combine(playwright), "chromium-1243", "chrome-mac-arm64",
			"Google Chrome for Testing.app", "Contents", "MacOS", "Google Chrome for Testing");
		Touch([.. playwright, "chromium_headless_shell-1300", "chrome-headless-shell-mac-arm64", "chrome-headless-shell"]);
		string puppeteer131 = Touch([.. puppeteer, "chrome-headless-shell", "mac_arm-131.0.6778.85",
			"chrome-headless-shell-mac-arm64", "chrome-headless-shell"]);
		string puppeteer129 = Touch([.. puppeteer, "chrome-headless-shell", "mac_arm-129.0.6668.58",
			"chrome-headless-shell-mac-arm64", "chrome-headless-shell"]);
		BrowserCacheLocator locator = Locator(OSPlatform.OSX, new() { ["HOME"] = Path.Combine(_root, "home") });

		// Act
		IReadOnlyList<InstalledBrowser> found = locator.FindCached();

		// Assert
		found.Select(browser => browser.Path).Should().Equal([shell1243, shell1208, puppeteer131, puppeteer129, chromium1243],
			because: "a cached headless shell is the recommended choice, newest first, and a download still in progress is not offered");
		found[0].Name.Should().Be("chrome-headless-shell (Playwright cache)", because: "the user learns where it came from");
		found[0].Version.Should().Be("Playwright build 1243", because: "Playwright names builds by revision");
		found[2].Version.Should().Be("131.0.6778.85", because: "Puppeteer names folders by Chrome version");
	}

	[Test]
	[Description("Linux: Playwright's cache follows XDG_CACHE_HOME, and a full Chromium build is found by its chrome executable.")]
	public void FindCached_Should_Follow_Xdg_Cache_Home_On_Linux() {
		// Arrange
		string chromium = PlaywrightBuild(Path.Combine("xdg", "ms-playwright"), "chromium-1243", "chrome-linux64", "chrome");
		PlaywrightBuild(Path.Combine("home", ".cache", "ms-playwright"), "chromium-9999", "chrome-linux64", "chrome");
		BrowserCacheLocator locator = Locator(OSPlatform.Linux, new() {
			["HOME"] = Path.Combine(_root, "home"),
			["XDG_CACHE_HOME"] = Path.Combine(_root, "xdg")
		});

		// Act
		IReadOnlyList<InstalledBrowser> found = locator.FindCached();

		// Assert
		found.Select(browser => browser.Path).Should().Equal([chromium],
			because: "XDG_CACHE_HOME moves Playwright's cache, so ~/.cache/ms-playwright is not it");
	}

	[Test]
	[Description("PLAYWRIGHT_BROWSERS_PATH and PUPPETEER_CACHE_DIR replace the default cache locations.")]
	public void FindCached_Should_Honour_The_Tools_Cache_Overrides() {
		// Arrange
		string playwright = PlaywrightBuild("pw-custom", "chromium_headless_shell-1243", "chrome-headless-shell-linux64",
			"chrome-headless-shell");
		string puppeteer = Touch("pp-custom", "chrome", "linux-131.0.6778.85", "chrome-linux64", "chrome");
		PlaywrightBuild(Path.Combine("home", ".cache", "ms-playwright"), "chromium_headless_shell-1", "x", "chrome-headless-shell");
		BrowserCacheLocator locator = Locator(OSPlatform.Linux, new() {
			["HOME"] = Path.Combine(_root, "home"),
			[BrowserCacheLocator.PlaywrightBrowsersPathEnvVar] = Path.Combine(_root, "pw-custom"),
			[BrowserCacheLocator.PuppeteerCacheDirEnvVar] = Path.Combine(_root, "pp-custom")
		});

		// Act
		IReadOnlyList<InstalledBrowser> found = locator.FindCached();

		// Assert
		found.Select(browser => browser.Path).Should().Equal([playwright, puppeteer],
			because: "each tool's own variable decides where its cache is");
	}

	[Test]
	[Description("PLAYWRIGHT_BROWSERS_PATH=0 (browsers inside node_modules) searches no Playwright cache.")]
	public void FindCached_Should_Skip_Playwright_When_Its_Browsers_Live_In_Node_Modules() {
		// Arrange
		PlaywrightBuild(Path.Combine("home", ".cache", "ms-playwright"), "chromium_headless_shell-1243", "x", "chrome-headless-shell");
		BrowserCacheLocator locator = Locator(OSPlatform.Linux, new() {
			["HOME"] = Path.Combine(_root, "home"),
			[BrowserCacheLocator.PlaywrightBrowsersPathEnvVar] = "0"
		});

		// Act
		IReadOnlyList<InstalledBrowser> found = locator.FindCached();

		// Assert
		found.Should().BeEmpty(because: "0 means the browsers are per project, not in a cache");
	}

	[Test]
	[Description("Windows: Playwright's %LOCALAPPDATA%\\ms-playwright and Puppeteer's %USERPROFILE%\\.cache\\puppeteer are searched.")]
	public void FindCached_Should_Search_The_Windows_Locations() {
		// Arrange
		string playwright = PlaywrightBuild(Path.Combine("local", "ms-playwright"), "chromium_headless_shell-1243",
			"chrome-headless-shell-win64", "chrome-headless-shell.exe");
		string puppeteer = Touch("profile", ".cache", "puppeteer", "chrome", "win64-131.0.6778.85", "chrome-win64", "chrome.exe");
		BrowserCacheLocator locator = Locator(OSPlatform.Windows, new() {
			["LOCALAPPDATA"] = Path.Combine(_root, "local"),
			["USERPROFILE"] = Path.Combine(_root, "profile")
		});

		// Act
		IReadOnlyList<InstalledBrowser> found = locator.FindCached();

		// Assert
		found.Select(browser => browser.Path).Should().Equal([playwright, puppeteer],
			because: "both tools keep their Windows caches under the user's profile");
	}

	[Test]
	[Description("No cache directory at all yields an empty list, not an error.")]
	public void FindCached_Should_Return_Nothing_Without_Caches() {
		// Arrange
		BrowserCacheLocator locator = Locator(OSPlatform.OSX, new() { ["HOME"] = Path.Combine(_root, "nobody") });

		// Act
		IReadOnlyList<InstalledBrowser> found = locator.FindCached();

		// Assert
		found.Should().BeEmpty(because: "a machine without Playwright or Puppeteer has no cache");
	}
}
