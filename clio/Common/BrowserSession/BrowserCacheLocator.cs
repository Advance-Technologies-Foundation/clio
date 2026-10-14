using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Ms = System.IO.Abstractions;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Finds the Chromium builds other tools already downloaded - Playwright's (Playwright MCP included) and Puppeteer's
/// caches - so page-smoke can offer one before offering a download.
/// </summary>
public interface IBrowserCacheLocator {

	/// <summary>
	/// The cached browsers, headless shells first (Playwright's, then Puppeteer's), then full Chromium builds; newest
	/// first within each group.
	/// </summary>
	IReadOnlyList<InstalledBrowser> FindCached();
}

/// <inheritdoc cref="IBrowserCacheLocator" />
public sealed class BrowserCacheLocator : IBrowserCacheLocator {

	/// <summary>Playwright's override of its browser cache; <c>0</c> means "inside node_modules", which is not searched.</summary>
	public const string PlaywrightBrowsersPathEnvVar = "PLAYWRIGHT_BROWSERS_PATH";

	/// <summary>Puppeteer's override of its cache.</summary>
	public const string PuppeteerCacheDirEnvVar = "PUPPETEER_CACHE_DIR";

	private const int MaxSearchDepth = 4;

	private static readonly string[] HeadlessShellNames =
		["chrome-headless-shell", "chrome-headless-shell.exe", "headless_shell", "headless_shell.exe"];

	private static readonly string[] ChromiumNames = ["chrome", "chrome.exe", "Chromium", "Google Chrome for Testing"];

	private readonly Ms.IFileSystem _fileSystem;
	private readonly Func<string, string> _readEnvironment;
	private readonly OSPlatform _platform;

	/// <summary>Initializes the locator for this machine.</summary>
	/// <param name="fileSystem">File system the caches are read from.</param>
	public BrowserCacheLocator(Ms.IFileSystem fileSystem)
		: this(fileSystem, Environment.GetEnvironmentVariable,
			RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX
			: RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
			: OSPlatform.Linux) {
	}

	/// <summary>Initializes the locator over an explicit environment and OS layout, for tests.</summary>
	internal BrowserCacheLocator(Ms.IFileSystem fileSystem, Func<string, string> readEnvironment, OSPlatform platform) {
		_fileSystem = fileSystem;
		_readEnvironment = readEnvironment;
		_platform = platform;
	}

	/// <inheritdoc />
	public IReadOnlyList<InstalledBrowser> FindCached() {
		string playwright = PlaywrightRoot();
		string puppeteer = PuppeteerRoot();
		List<InstalledBrowser> found = [
			.. Playwright(playwright, "chromium_headless_shell-", HeadlessShellNames, "chrome-headless-shell (Playwright cache)"),
			.. Puppeteer(puppeteer, "chrome-headless-shell", HeadlessShellNames, "chrome-headless-shell (Puppeteer cache)"),
			.. Playwright(playwright, "chromium-", ChromiumNames, "Chromium (Playwright cache)"),
			.. Puppeteer(puppeteer, "chrome", ChromiumNames, "Chrome for Testing (Puppeteer cache)")
		];
		return found;
	}

	private string PlaywrightRoot() {
		string overridden = _readEnvironment(PlaywrightBrowsersPathEnvVar);
		if (!string.IsNullOrWhiteSpace(overridden)) {
			return overridden.Trim() == "0" ? null : overridden.Trim();
		}
		if (_platform == OSPlatform.Windows) {
			string localAppData = _readEnvironment("LOCALAPPDATA");
			return string.IsNullOrWhiteSpace(localAppData) ? null : Path.Combine(localAppData, "ms-playwright");
		}
		string home = _readEnvironment("HOME");
		if (string.IsNullOrWhiteSpace(home)) {
			return null;
		}
		if (_platform == OSPlatform.OSX) {
			return Path.Combine(home, "Library", "Caches", "ms-playwright");
		}
		string xdgCache = _readEnvironment("XDG_CACHE_HOME");
		return Path.Combine(string.IsNullOrWhiteSpace(xdgCache) ? Path.Combine(home, ".cache") : xdgCache, "ms-playwright");
	}

	private string PuppeteerRoot() {
		string overridden = _readEnvironment(PuppeteerCacheDirEnvVar);
		if (!string.IsNullOrWhiteSpace(overridden)) {
			return overridden.Trim();
		}
		string home = _readEnvironment(_platform == OSPlatform.Windows ? "USERPROFILE" : "HOME");
		return string.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, ".cache", "puppeteer");
	}

	// <root>/<prefix><revision>/... ; only a revision Playwright marked complete, newest revision first.
	private IEnumerable<InstalledBrowser> Playwright(string root, string prefix, string[] names, string label) {
		if (root is null || !_fileSystem.Directory.Exists(root)) {
			return [];
		}
		return SafeDirectories(root)
			.Select(dir => (Dir: dir, Revision: Revision(Path.GetFileName(dir), prefix)))
			.Where(item => item.Revision is not null
				&& _fileSystem.File.Exists(Path.Combine(item.Dir, "INSTALLATION_COMPLETE")))
			.OrderByDescending(item => item.Revision.Value)
			.Select(item => (Item: item, Executable: FindExecutable(item.Dir, names, 0)))
			.Where(found => found.Executable is not null)
			.Select(found => new InstalledBrowser(label, $"Playwright build {found.Item.Revision}", found.Executable))
			.ToList();
	}

	// <root>/<product>/<platform>-<version>/... , newest version first.
	private IEnumerable<InstalledBrowser> Puppeteer(string root, string product, string[] names, string label) {
		string productRoot = root is null ? null : Path.Combine(root, product);
		if (productRoot is null || !_fileSystem.Directory.Exists(productRoot)) {
			return [];
		}
		return SafeDirectories(productRoot)
			.Select(dir => (Dir: dir, Version: VersionOf(Path.GetFileName(dir))))
			.Where(item => item.Version is not null)
			.OrderByDescending(item => item.Version)
			.Select(item => (Item: item, Executable: FindExecutable(item.Dir, names, 0)))
			.Where(found => found.Executable is not null)
			.Select(found => new InstalledBrowser(label, found.Item.Version.ToString(), found.Executable))
			.ToList();
	}

	private static int? Revision(string name, string prefix) =>
		name.StartsWith(prefix, StringComparison.Ordinal)
		&& int.TryParse(name[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int revision)
			? revision
			: null;

	private static Version VersionOf(string name) {
		int dash = name.LastIndexOf('-');
		return dash >= 0 && System.Version.TryParse(name[(dash + 1)..], out Version version) ? version : null;
	}

	// The executable sits a few folders down (an app bundle on macOS); the search stays shallow.
	private string FindExecutable(string directory, string[] names, int depth) {
		foreach (string file in SafeFiles(directory)) {
			if (names.Contains(Path.GetFileName(file), StringComparer.Ordinal)) {
				return file;
			}
		}
		if (depth >= MaxSearchDepth) {
			return null;
		}
		foreach (string child in SafeDirectories(directory)) {
			string found = FindExecutable(child, names, depth + 1);
			if (found is not null) {
				return found;
			}
		}
		return null;
	}

	private string[] SafeDirectories(string directory) {
		try {
			return _fileSystem.Directory.GetDirectories(directory);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			return [];
		}
	}

	private string[] SafeFiles(string directory) {
		try {
			return _fileSystem.Directory.GetFiles(directory);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			return [];
		}
	}
}
