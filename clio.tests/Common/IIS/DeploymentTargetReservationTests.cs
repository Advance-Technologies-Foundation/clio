using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Clio.Common.IIS;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.IIS;

[TestFixture]
[Category("Integration")]
[Property("Module", "Common")]
public sealed class DeploymentTargetReservationTests {
	[Test]
	[Description("A second operation cannot mutate the same logical environment name while its lease is held.")]
	public void AcquireEnvironment_ShouldRejectSameName_WhenLeaseIsHeld() {
		// Arrange
		string environmentName = $"clio-env-{Guid.NewGuid():N}";
		IDeploymentTargetReservation sut = new DeploymentTargetReservation();
		using IDisposable firstLease = sut.AcquireEnvironment(environmentName);

		// Act
		Action act = () => sut.AcquireEnvironment(environmentName.ToUpperInvariant());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*already being changed*",
			because: "name-scoped dbHub and registration mutations must serialize across clio processes");
	}

	[Test]
	[Description("A second operation cannot mutate the same canonical deployment target while its lease is held.")]
	public void Acquire_ShouldRejectSameTarget_WhenLeaseIsHeld() {
		// Arrange
		string target = Path.Combine(Path.GetTempPath(), $"clio-target-{Guid.NewGuid():N}");
		IDeploymentTargetReservation sut = new DeploymentTargetReservation();
		using IDisposable firstLease = sut.Acquire(target);

		// Act
		Action act = () => sut.Acquire(target + Path.DirectorySeparatorChar);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*already being changed*",
			because: "deploy and uninstall must serialize by normalized target identity across clio processes");
	}

	[Test]
	[Description("Independent deployment targets can be changed concurrently.")]
	public void Acquire_ShouldAllowDifferentTarget_WhenAnotherLeaseIsHeld() {
		// Arrange
		string firstTarget = Path.Combine(Path.GetTempPath(), $"clio-target-{Guid.NewGuid():N}");
		string secondTarget = Path.Combine(Path.GetTempPath(), $"clio-target-{Guid.NewGuid():N}");
		IDeploymentTargetReservation sut = new DeploymentTargetReservation();
		using IDisposable firstLease = sut.Acquire(firstTarget);

		// Act
		Action act = () => {
			using IDisposable secondLease = sut.Acquire(secondTarget);
		};

		// Assert
		act.Should().NotThrow(
			because: "the safety lease must not serialize unrelated Creatio environments");
	}

	[Test]
	[Description("Disposing a target lease permits a later operation on the same directory.")]
	public void Acquire_ShouldAllowSameTarget_AfterLeaseIsDisposed() {
		// Arrange
		string target = Path.Combine(Path.GetTempPath(), $"clio-target-{Guid.NewGuid():N}");
		IDeploymentTargetReservation sut = new DeploymentTargetReservation();
		IDisposable firstLease = sut.Acquire(target);
		firstLease.Dispose();

		// Act
		Action act = () => {
			using IDisposable secondLease = sut.Acquire(target);
		};

		// Assert
		act.Should().NotThrow(because: "failed or completed operations must release their target lease");
	}

	[Test]
	[Description("Windows aliases of one existing directory share the same machine-wide target lease.")]
	public void Acquire_ShouldRejectExtendedWindowsAlias_WhenCanonicalLeaseIsHeld() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Extended Windows path aliases are Windows-specific.");
		}
		string target = Path.Combine(Path.GetTempPath(), $"clio-target-{Guid.NewGuid():N}");
		Directory.CreateDirectory(target);
		try {
			IDeploymentTargetReservation sut = new DeploymentTargetReservation();
			using IDisposable firstLease = sut.Acquire(target);

			// Act
			Action act = () => sut.Acquire(@"\\?\" + target);

			// Assert
			act.Should().Throw<InvalidOperationException>().WithMessage("*already being changed*",
				because: "deploy and uninstall aliases must serialize on physical directory identity");
		}
		finally {
			Directory.Delete(target);
		}
	}

	[Test]
	[Description("A nonexistent child below a Windows directory link shares the physical-parent target lease.")]
	public void Acquire_ShouldRejectPhysicalAlias_WhenLeafDoesNotExist() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Windows directory-link identity is Windows-specific.");
		}
		string root = Path.Combine(Path.GetTempPath(), $"clio-link-{Guid.NewGuid():N}");
		string physicalParent = Path.Combine(root, "physical");
		string aliasParent = Path.Combine(root, "alias");
		Directory.CreateDirectory(physicalParent);
		try {
			try {
				Directory.CreateSymbolicLink(aliasParent, physicalParent);
			}
			catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) {
				Assert.Ignore($"Directory-link creation is unavailable: {exception.Message}");
			}
			IDeploymentTargetReservation sut = new DeploymentTargetReservation();
			using IDisposable firstLease = sut.Acquire(Path.Combine(aliasParent, "new-site"));

			// Act
			Action act = () => sut.Acquire(Path.Combine(physicalParent, "new-site"));

			// Assert
			act.Should().Throw<InvalidOperationException>().WithMessage("*already being changed*",
				because: "the nearest existing ancestor must canonicalize the absent deployment leaf");
		}
		finally {
			if (Directory.Exists(aliasParent)) {
				Directory.Delete(aliasParent);
			}
			if (Directory.Exists(root)) {
				Directory.Delete(root, recursive: true);
			}
		}
	}

	[Test]
	[Description("A target lease held by another operating-system process blocks this clio process until release.")]
	public void Acquire_ShouldSerializeIndependentProcesses_UsingExclusiveTargetFile() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Machine-wide target reservation files are Windows-specific.");
		}
		string target = Path.Combine(Path.GetTempPath(), $"clio-process-target-{Guid.NewGuid():N}");
		string identity = DirectoryPathIdentity.Normalize(target).ToUpperInvariant();
		string lockKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
		string lockPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"Creatio", "clio", "deployment-locks", $"target-{lockKey}.lock");
		IDeploymentTargetReservation sut = new DeploymentTargetReservation();
		using (sut.Acquire(target)) {
			// Ensure the read-only production lock file exists before the child opens it.
		}
		string readyPath = Path.Combine(Path.GetTempPath(), $"clio-target-lock-ready-{Guid.NewGuid():N}");
		ProcessStartInfo startInfo = new("powershell.exe") {
			UseShellExecute = false,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("-NoProfile");
		startInfo.ArgumentList.Add("-NonInteractive");
		startInfo.ArgumentList.Add("-Command");
		string quotedLockPath = lockPath.Replace("'", "''");
		string quotedReadyPath = readyPath.Replace("'", "''");
		startInfo.ArgumentList.Add(
			$"$stream=[IO.File]::Open('{quotedLockPath}','Open','Read','None');"
			+ $"[IO.File]::WriteAllText('{quotedReadyPath}','ready');Start-Sleep -Seconds 30;$stream.Dispose()");
		using Process child = Process.Start(startInfo)!;
		try {
			SpinWait.SpinUntil(() => File.Exists(readyPath) || child.HasExited, TimeSpan.FromSeconds(10))
				.Should().BeTrue(because: "the child must signal after it owns the target lock");
			string childError = child.HasExited ? child.StandardError.ReadToEnd() : string.Empty;
			child.HasExited.Should().BeFalse(
				because: $"the independent target-lock owner must remain alive; stderr: {childError}");

			// Act
			Action act = () => sut.Acquire(target);

			// Assert
			act.Should().Throw<InvalidOperationException>().WithMessage("*already being changed*",
				because: "two independent terminals must serialize mutations of the same physical target");
			child.Kill(entireProcessTree: true);
			child.WaitForExit(10000).Should().BeTrue(
				because: "process exit must release the operating-system target lock");
			Action later = () => {
				using IDisposable lease = sut.Acquire(target);
			};
			later.Should().NotThrow(because: "the target lease must be reusable after process exit");
		}
		finally {
			if (!child.HasExited) {
				child.Kill(entireProcessTree: true);
				child.WaitForExit();
			}
			File.Delete(readyPath);
		}
	}

}
