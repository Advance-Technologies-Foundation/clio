using System;
using System.IO;

namespace Clio.Common;

/// <summary>
/// Default <see cref="IFileSecurityHardening"/>.
/// <para>
/// <b>Unix (macOS/Linux):</b> sets the file to <c>0600</c> and the directory to <c>0700</c> via
/// <see cref="File.SetUnixFileMode(string, UnixFileMode)"/> — owner read/write only.
/// </para>
/// <para>
/// <b>Windows:</b> currently a documented limitation. The session cache lives under
/// <c>%LOCALAPPDATA%</c> (<see cref="SettingsRepository.AppSettingsFolderPath"/>), a per-user
/// location that is not world-readable by default, so files inherit a per-user ACL. An explicit
/// current-user-only ACL (inheritance disabled) is a tracked follow-up; it is intentionally not
/// implemented here to avoid shipping unverified Windows-ACL code. A one-time debug note records
/// the gap.
/// </para>
/// </summary>
public sealed class FileSecurityHardening : IFileSecurityHardening {
	private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
	private const UnixFileMode OwnerOnlyDirectory =
		UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

	private readonly ILogger _logger;

	/// <summary>Initializes the hardening helper.</summary>
	/// <param name="logger">Optional logger used to record the Windows documented-limitation note.</param>
	public FileSecurityHardening(ILogger logger = null) {
		_logger = logger;
	}

	/// <inheritdoc />
	public void HardenFile(string filePath) => Harden(filePath, OwnerOnlyFile);

	/// <inheritdoc />
	public void HardenDirectory(string directoryPath) => Harden(directoryPath, OwnerOnlyDirectory);

	private void Harden(string path, UnixFileMode mode) {
		if (string.IsNullOrEmpty(path)) {
			return;
		}
		if (OperatingSystem.IsWindows()) {
			// Documented limitation: rely on the per-user %LOCALAPPDATA% ACL; explicit owner-only
			// ACL tightening is a tracked follow-up (see FileSecurityHardening summary / Story 3).
			_logger?.WriteDebug(
				$"Owner-only ACL tightening is not yet applied on Windows; relying on the per-user profile ACL for '{path}'.");
			return;
		}
		File.SetUnixFileMode(path, mode);
	}
}
