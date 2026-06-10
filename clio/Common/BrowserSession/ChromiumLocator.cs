using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="IChromiumLocator" />
public sealed class ChromiumLocator : IChromiumLocator {
	private const string ChromePathEnvVar = "CHROME_PATH";

	private readonly IFileSystem _fileSystem;

	/// <summary>Initializes the locator over the file-system abstraction used to probe candidate paths.</summary>
	/// <param name="fileSystem">File-system abstraction (existence checks only).</param>
	public ChromiumLocator(IFileSystem fileSystem) => _fileSystem = fileSystem;

	/// <inheritdoc />
	public string Locate() {
		string fromEnv = Environment.GetEnvironmentVariable(ChromePathEnvVar);
		if (!string.IsNullOrWhiteSpace(fromEnv) && _fileSystem.ExistsFile(fromEnv)) {
			return fromEnv;
		}

		return CandidatePaths()
			.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && _fileSystem.ExistsFile(c))
			?? throw new ChromiumNotFoundException(
				"Error: Chromium binary not found — ensure a Chromium-based browser is installed " +
				$"(or set the {ChromePathEnvVar} environment variable to its executable).");
	}

	// Standard install locations per OS. The macOS `open` indirection cannot be used here because it
	// returns no CDP handle, so a concrete executable path is required (Story 9 / ADR Decision 6).
	private static IEnumerable<string> CandidatePaths() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			return [
				"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
				"/Applications/Chromium.app/Contents/MacOS/Chromium",
				"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
				"/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"
			];
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
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

	private static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);
}
