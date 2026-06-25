using System;
using System.IO;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Story 3 (browser-session-handoff): verifies owner-only hardening on a real filesystem. On Unix
/// the exact mode (0600/0700) is asserted; on Windows the documented limitation applies and the
/// call must simply not throw (per-user %LOCALAPPDATA% ACL is relied upon).
/// </summary>
[TestFixture]
[Category("Integration")]
[Property("Module", "Common")]
public sealed class FileSecurityHardeningTests {

	[Test]
	[Description("HardenFile sets owner read/write only (0600) on Unix and does not throw on Windows.")]
	public void HardenFile_ShouldSetOwnerOnlyMode_OnUnix() {
		// Arrange
		var sut = new FileSecurityHardening();
		string path = Path.Combine(Path.GetTempPath(), $"clio-harden-{Guid.NewGuid():N}.tmp");
		File.WriteAllText(path, "secret");

		try {
			// Act
			Action act = () => sut.HardenFile(path);

			// Assert
			act.Should().NotThrow(because: "hardening must never break the feature on any OS");
			if (!OperatingSystem.IsWindows()) {
				File.GetUnixFileMode(path).Should().Be(
					UnixFileMode.UserRead | UnixFileMode.UserWrite,
					because: "a file holding bearer cookies must be owner read/write only (0600)");
			}
		} finally {
			File.Delete(path);
		}
	}

	[Test]
	[Description("HardenDirectory sets owner read/write/execute only (0700) on Unix and does not throw on Windows.")]
	public void HardenDirectory_ShouldSetOwnerOnlyMode_OnUnix() {
		// Arrange
		var sut = new FileSecurityHardening();
		string dir = Path.Combine(Path.GetTempPath(), $"clio-harden-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);

		try {
			// Act
			Action act = () => sut.HardenDirectory(dir);

			// Assert
			act.Should().NotThrow(because: "hardening must never break the feature on any OS");
			if (!OperatingSystem.IsWindows()) {
				File.GetUnixFileMode(dir).Should().Be(
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
					because: "the sessions directory must be owner-only (0700)");
			}
		} finally {
			Directory.Delete(dir, recursive: true);
		}
	}
}
