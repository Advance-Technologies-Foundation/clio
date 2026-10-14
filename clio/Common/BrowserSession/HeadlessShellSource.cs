using System;
using System.Collections.Generic;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Where the downloadable headless browser comes from: its version, the archive URL per platform, the size and
/// SHA-256 each archive must have, and the executable inside it. The download, verification and install flow
/// (<see cref="HeadlessShellInstaller"/>) does not change when the source does - switching to Chromium snapshot
/// builds is a second implementation of this interface.
/// </summary>
public interface IHeadlessShellSource {

	/// <summary>A human-readable name of the build and where it comes from, for the question put to the user.</summary>
	string DisplayName { get; }

	/// <summary>The pinned version; the install directory is named after it.</summary>
	string Version { get; }

	/// <summary>The pinned archive for a platform; null when the source publishes none for it.</summary>
	/// <param name="platformKey">The platform key (<see cref="HeadlessShellRelease.PlatformKey"/>).</param>
	HeadlessShellRelease.Archive FindArchive(string platformKey);

	/// <summary>The URL the archive is downloaded from, honouring a configured mirror.</summary>
	/// <param name="platformKey">The platform key.</param>
	Uri ArchiveUrl(string platformKey);

	/// <summary>The executable inside the extracted archive, relative to the install directory.</summary>
	/// <param name="platformKey">The platform key.</param>
	string ExecutableRelativePath(string platformKey);
}

/// <summary>chrome-headless-shell from Chrome for Testing, pinned to <see cref="HeadlessShellRelease.Version"/>.</summary>
public sealed class ChromeForTestingHeadlessShellSource : IHeadlessShellSource {

	private readonly Func<string, string> _readEnvironment;

	/// <summary>Initializes the source over the process environment (for the mirror override).</summary>
	public ChromeForTestingHeadlessShellSource() : this(Environment.GetEnvironmentVariable) {
	}

	/// <summary>Initializes the source over an explicit environment, for tests.</summary>
	internal ChromeForTestingHeadlessShellSource(Func<string, string> readEnvironment) => _readEnvironment = readEnvironment;

	/// <inheritdoc />
	public string DisplayName => "chrome-headless-shell from Google's Chrome for Testing";

	/// <inheritdoc />
	public string Version => HeadlessShellRelease.Version;

	/// <inheritdoc />
	public HeadlessShellRelease.Archive FindArchive(string platformKey) =>
		platformKey is not null && HeadlessShellRelease.Archives.TryGetValue(platformKey, out HeadlessShellRelease.Archive archive)
			? archive
			: null;

	/// <inheritdoc />
	public Uri ArchiveUrl(string platformKey) {
		string mirror = _readEnvironment(HeadlessShellRelease.MirrorEnvVar);
		return HeadlessShellRelease.ArchiveUrl(string.IsNullOrWhiteSpace(mirror) ? HeadlessShellRelease.DefaultBaseUrl : mirror.Trim(),
			platformKey);
	}

	/// <inheritdoc />
	public string ExecutableRelativePath(string platformKey) => HeadlessShellRelease.ExecutableRelativePath(platformKey);
}
