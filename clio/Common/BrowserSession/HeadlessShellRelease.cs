using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Clio.Common.BrowserSession;

/// <summary>
/// The chrome-headless-shell build page-smoke downloads when the user chooses a download: one Chrome for Testing
/// release, pinned with each archive's size and SHA-256.
/// </summary>
/// <remarks>
/// Chrome for Testing publishes no checksums (its JSON index lists only URLs), so the size and SHA-256 below were
/// measured from the published archives when the version was pinned. Raising the version means downloading every
/// archive again and re-pinning both values; a mismatch is refused, never installed
/// (docs/knowledge/Common/chrome-for-testing-publishes-no-checksums.md).
/// </remarks>
public static class HeadlessShellRelease {

	/// <summary>The pinned Chrome for Testing version.</summary>
	public const string Version = "155.0.8059.79";

	/// <summary>Environment variable that replaces <see cref="DefaultBaseUrl"/>, for a network that reaches only a mirror.</summary>
	public const string MirrorEnvVar = "CLIO_CHROME_FOR_TESTING_MIRROR";

	/// <summary>Where Chrome for Testing publishes its archives: {base}/{version}/{platform}/chrome-headless-shell-{platform}.zip.</summary>
	public const string DefaultBaseUrl = "https://storage.googleapis.com/chrome-for-testing-public"; // NOSONAR - the official Chrome for Testing endpoint; overridden by CLIO_CHROME_FOR_TESTING_MIRROR

	/// <summary>The size of the largest archive, rounded, for the question put to the user.</summary>
	public const int ApproximateDownloadMegabytes = 125;

	/// <summary>One platform's archive: its exact size in bytes and its SHA-256 (lowercase hex).</summary>
	/// <param name="Size">Archive size in bytes.</param>
	/// <param name="Sha256">Archive SHA-256, lowercase hex.</param>
	public sealed record Archive(long Size, string Sha256);

	internal static readonly IReadOnlyDictionary<string, Archive> Archives = new Dictionary<string, Archive>(StringComparer.Ordinal) {
		["linux-arm64"] = new(124_715_442, "72d938cabad9674610939c72ef2d3faf20214a4418c3b4e94c1cebca683e767b"),
		["linux64"] = new(124_406_717, "4cea4c32b501aa43cc2b81eb45734af991a6a82feded48380d6dfaf96642a1c1"),
		["mac-arm64"] = new(102_390_341, "5f51504ead3d41815111cd73e79c4979df3a4e6aedc7cf6fe2b042ae037363b5"),
		["mac-x64"] = new(108_042_306, "36a1da728fc936770a0edc52989b74d8d3a194e8747f490e372a210853ad831c"),
		["win32"] = new(118_265_419, "6b008fa3838b64166c27202f2074843eb59d1e21d97d6fdb3eb8de53f43983a9"),
		["win64"] = new(125_212_001, "c4cc49fa7908cf018600b04c95918d336565d90f9c72241fd83dacd9ec41855c")
	};

	/// <summary>The Chrome for Testing platform name for an OS and process architecture; null when none is published.</summary>
	/// <param name="platform">The operating system.</param>
	/// <param name="architecture">The process architecture.</param>
	/// <returns>For example <c>mac-arm64</c> or <c>win64</c>.</returns>
	public static string PlatformKey(OSPlatform platform, Architecture architecture) {
		if (platform == OSPlatform.OSX) {
			return architecture == Architecture.Arm64 ? "mac-arm64" : "mac-x64";
		}
		if (platform == OSPlatform.Windows) {
			// Windows on Arm runs the x64 build under emulation; there is no arm64 Windows build.
			return architecture == Architecture.X86 ? "win32" : "win64";
		}
		if (platform == OSPlatform.Linux) {
			return architecture switch {
				Architecture.X64 => "linux64",
				Architecture.Arm64 => "linux-arm64",
				_ => null
			};
		}
		return null;
	}

	/// <summary>The platform key of the running process; null when none is published.</summary>
	public static string CurrentPlatformKey() => PlatformKey(CurrentOsPlatform(), RuntimeInformation.ProcessArchitecture);

	/// <summary>The running OS as the browser lookups name it: macOS, Windows, or Linux for every other Unix.</summary>
	public static OSPlatform CurrentOsPlatform() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			return OSPlatform.OSX;
		}
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows : OSPlatform.Linux;
	}

	/// <summary>The archive URL for a platform under a base URL (the default or a mirror).</summary>
	/// <param name="baseUrl">The base URL, without a trailing slash required.</param>
	/// <param name="platformKey">The platform key.</param>
	/// <returns>The archive URL.</returns>
	public static Uri ArchiveUrl(string baseUrl, string platformKey) =>
		new($"{baseUrl.TrimEnd('/')}/{Version}/{platformKey}/chrome-headless-shell-{platformKey}.zip");

	/// <summary>The executable inside the extracted archive, relative to the version directory.</summary>
	/// <param name="platformKey">The platform key.</param>
	/// <returns>For example <c>chrome-headless-shell-win64/chrome-headless-shell.exe</c>.</returns>
	public static string ExecutableRelativePath(string platformKey) =>
		Path.Combine("chrome-headless-shell-" + platformKey,
			platformKey.StartsWith("win", StringComparison.Ordinal) ? "chrome-headless-shell.exe" : "chrome-headless-shell");
}
