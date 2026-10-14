using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="IChromiumLocator" />
public sealed class ChromiumLocator : IChromiumLocator {
	private const string ChromePathEnvVar = "CHROME_PATH";

	private static readonly Regex EnvironmentReference = new("%([^%]+)%", RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	private static readonly Regex BundleVersion = new(
		@"<key>CFBundleShortVersionString</key>\s*<string>([^<]+)</string>", RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	private readonly IFileSystem _fileSystem;
	private readonly Func<string, string> _readEnvironment;
	private readonly OSPlatform _platform;

	/// <summary>Initializes the locator over the file-system abstraction used to probe candidate paths.</summary>
	/// <param name="fileSystem">File-system abstraction (existence checks only).</param>
	public ChromiumLocator(IFileSystem fileSystem)
		: this(fileSystem, Environment.GetEnvironmentVariable, CurrentPlatform()) {
	}

	/// <summary>
	/// Initializes the locator over an explicit environment and platform, so a test can state the environment a
	/// process actually sees - for example the cleared environment an MCP worker is launched with - and the Windows
	/// candidate paths, on any OS. Internal so the container keeps using the public constructor.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction (existence checks only).</param>
	/// <param name="readEnvironment">Reads one environment variable; null when it is not set.</param>
	/// <param name="platform">Which OS's install locations to probe.</param>
	internal ChromiumLocator(IFileSystem fileSystem, Func<string, string> readEnvironment, OSPlatform platform) {
		_fileSystem = fileSystem;
		_readEnvironment = readEnvironment;
		_platform = platform;
	}

	/// <inheritdoc />
	public string Locate() {
		string fromEnv = _readEnvironment(ChromePathEnvVar);
		if (!string.IsNullOrWhiteSpace(fromEnv) && _fileSystem.ExistsFile(fromEnv)) {
			return fromEnv;
		}

		return CandidatePaths()
			.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && _fileSystem.ExistsFile(c))
			?? throw new ChromiumNotFoundException(
				"Error: Chromium binary not found — ensure a Chromium-based browser is installed " +
				$"(or set the {ChromePathEnvVar} environment variable to its executable).");
	}

	/// <inheritdoc />
	public string ChromePathOverride() {
		string fromEnv = _readEnvironment(ChromePathEnvVar);
		return !string.IsNullOrWhiteSpace(fromEnv) && _fileSystem.ExistsFile(fromEnv) ? fromEnv : null;
	}

	/// <inheritdoc />
	public IReadOnlyList<InstalledBrowser> FindInstalled() =>
		CandidatePaths()
			.Where(c => !string.IsNullOrWhiteSpace(c) && _fileSystem.ExistsFile(c))
			.Distinct(_platform == OSPlatform.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
			.Select(path => new InstalledBrowser(NameOf(path), ReadVersion(path), path))
			.ToList();

	private static string NameOf(string path) {
		string file = path.Replace('\\', '/');
		return file.Contains("Edge", StringComparison.OrdinalIgnoreCase) || file.Contains("msedge", StringComparison.OrdinalIgnoreCase)
			? "Microsoft Edge"
			: file.Contains("Brave", StringComparison.OrdinalIgnoreCase) ? "Brave"
			: file.Contains("chromium", StringComparison.OrdinalIgnoreCase) ? "Chromium"
			: "Google Chrome";
	}

	// The version a user recognises: the app bundle's on macOS, the executable's product version on Windows. On Linux
	// it would take starting the browser, so it is left out there.
	private string ReadVersion(string path) {
		try {
			if (_platform == OSPlatform.OSX) {
				int app = path.IndexOf(".app/", StringComparison.Ordinal);
				if (app < 0) {
					return null;
				}
				string plist = path[..(app + 4)] + "/Contents/Info.plist";
				Match match = _fileSystem.ExistsFile(plist)
					? BundleVersion.Match(_fileSystem.ReadAllText(plist))
					: Match.Empty;
				return match.Success ? match.Groups[1].Value.Trim() : null;
			}
			if (_platform == OSPlatform.Windows && OperatingSystem.IsWindows()) {
				return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion;
			}
		}
		catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException
			or RegexMatchTimeoutException) {
			// The version is a courtesy in the question; the path is what matters.
		}
		return null;
	}

	// Standard install locations per OS. The macOS `open` indirection cannot be used here because it
	// returns no CDP handle, so a concrete executable path is required (Story 9 / ADR Decision 6).
	private IEnumerable<string> CandidatePaths() {
		if (_platform == OSPlatform.OSX) {
			return [
				"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
				"/Applications/Chromium.app/Contents/MacOS/Chromium",
				"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
				"/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"
			];
		}

		if (_platform == OSPlatform.Windows) {
			return [
				Expand(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
				Expand(@"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"),
				Expand(@"%LocalAppData%\Google\Chrome\Application\chrome.exe"),
				Expand(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
				Expand(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe")
			];
		}

		return [
			"/usr/bin/google-chrome",
			"/usr/bin/google-chrome-stable",
			"/usr/bin/chromium",
			"/usr/bin/chromium-browser",
			"/usr/bin/microsoft-edge",
			"/snap/bin/chromium"
		];
	}

	private static OSPlatform CurrentPlatform() =>
		RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX
		: RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
		: OSPlatform.Linux;

	// %NAME% is replaced from the environment this locator reads, as Environment.ExpandEnvironmentVariables does
	// from the process; a variable that is not set is left as written, so its candidate path does not exist.
	private string Expand(string path) =>
		EnvironmentReference.Replace(path, match => _readEnvironment(match.Groups[1].Value) ?? match.Value);
}
