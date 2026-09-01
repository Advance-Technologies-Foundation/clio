using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.IIS;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.IIS;

[TestFixture]
[Category("Integration")]
[Property("Module", "Common")]
public sealed class IisDeploymentPortReservationTests {
	[Test]
	[Description("Reserves the first available port reported in an inclusive range and exposes it through the lease.")]
	public void AcquireFirstAvailable_ShouldReserveFirstAvailablePort_InAscendingRange() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Machine-wide IIS deployment reservations are Windows-specific.");
		}
		int rangeStart = Random.Shared.Next(50000, 55000);
		int selectedPort = rangeStart + 2;
		int rangeEnd = rangeStart + 5;
		IAvailableIisPortService availability = Substitute.For<IAvailableIisPortService>();
		availability.FindAsync(rangeStart, rangeEnd).Returns(new FindAvailableIisPortResult(
			"available", "free", rangeStart, rangeEnd, selectedPort, 0, 0));
		availability.FindAsync(selectedPort, selectedPort).Returns(new FindAvailableIisPortResult(
			"available", "free", selectedPort, selectedPort, selectedPort, 0, 0));
		IIisDeploymentPortReservation sut = new IisDeploymentPortReservation(availability);

		// Act
		using IisDeploymentPortLease lease = sut.AcquireFirstAvailable(rangeStart, rangeEnd);

		// Assert
		lease.Port.Should().Be(selectedPort,
			because: "the reservation lease must expose the first scanner-approved candidate");
		availability.Received(1).FindAsync(selectedPort, selectedPort);
	}

	[Test]
	[Description("Continues to the next available candidate when another clio process already owns the first candidate lock.")]
	public void AcquireFirstAvailable_ShouldContinue_WhenFirstCandidateIsReserved() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Machine-wide IIS deployment reservations are Windows-specific.");
		}
		int firstPort = Random.Shared.Next(55001, 60000);
		int secondPort = firstPort + 1;
		string lockPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"Creatio", "clio", "deployment-locks", $"iis-port-{firstPort}.lock");
		Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
		using FileStream competingLease = new(lockPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
		IAvailableIisPortService availability = Substitute.For<IAvailableIisPortService>();
		availability.FindAsync(firstPort, secondPort).Returns(new FindAvailableIisPortResult(
			"available", "first looks free", firstPort, secondPort, firstPort, 0, 0));
		availability.FindAsync(secondPort, secondPort).Returns(new FindAvailableIisPortResult(
			"available", "second is free", secondPort, secondPort, secondPort, 0, 0));
		IIisDeploymentPortReservation sut = new IisDeploymentPortReservation(availability);

		// Act
		using IisDeploymentPortLease lease = sut.AcquireFirstAvailable(firstPort, secondPort);

		// Assert
		lease.Port.Should().Be(secondPort,
			because: "automatic allocation must skip a candidate atomically claimed by a concurrent clio deployment");
		availability.Received(2).FindAsync(secondPort, secondPort);
	}

	[Test]
	[Description("Fails with the configured range when no IIS port can be reserved.")]
	public void AcquireFirstAvailable_ShouldFailWithRange_WhenNoPortIsAvailable() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Machine-wide IIS deployment reservations are Windows-specific.");
		}
		const int rangeStart = 40100;
		const int rangeEnd = 40199;
		IAvailableIisPortService availability = Substitute.For<IAvailableIisPortService>();
		availability.FindAsync(rangeStart, rangeEnd).Returns(new FindAvailableIisPortResult(
			"unavailable", "all occupied", rangeStart, rangeEnd, null, 1, 1));
		IIisDeploymentPortReservation sut = new IisDeploymentPortReservation(availability);

		// Act
		Action act = () => sut.AcquireFirstAvailable(rangeStart, rangeEnd);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*[40100, 40199]*all occupied*",
				because: "a full range must produce an actionable error naming the exact configured range");
	}

	[Test]
	[Description("Rejects an IIS port when the fail-closed IIS and TCP scan does not prove it available.")]
	public void Acquire_ShouldRejectPort_WhenAvailabilityScanDoesNotProveItFree() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Machine-wide IIS deployment reservations are Windows-specific.");
		}
		const int port = 49151;
		IAvailableIisPortService availability = Substitute.For<IAvailableIisPortService>();
		availability.FindAsync(port, port).Returns(new FindAvailableIisPortResult(
			"unavailable", "occupied", port, port, null, 1, 0));
		IIisDeploymentPortReservation sut = new IisDeploymentPortReservation(availability);

		// Act
		Action act = () => sut.Acquire(port);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not available*",
				because: "deployment must fail before mutation when IIS or TCP already owns the requested port");
	}

	[Test]
	[Description("Rejects a second clio deployment while another process-sized execution owns the same port reservation.")]
	public async Task Acquire_ShouldRejectSecondOwner_WhenPortReservationIsHeld() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Machine-wide IIS deployment reservations are Windows-specific.");
		}
		int port = Random.Shared.Next(50000, 60000);
		IAvailableIisPortService availability = Substitute.For<IAvailableIisPortService>();
		availability.FindAsync(port, port).Returns(new FindAvailableIisPortResult(
			"available", "free", port, port, port, 0, 0));
		IIisDeploymentPortReservation first = new IisDeploymentPortReservation(availability);
		IIisDeploymentPortReservation second = new IisDeploymentPortReservation(availability);
		using IDisposable firstLease = first.Acquire(port);

		// Act
		Func<Task> act = async () => await Task.Run(() => second.Acquire(port));

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>(
			because: "a machine-scoped exclusive file must close the race between independent clio executions")
			.WithMessage("*reserved by another clio deployment*",
				because: "the CLI should report an actionable collision instead of the low-level Windows sharing error");
		await availability.Received(1).FindAsync(port, port);
	}

	[Test]
	[Description("Rejects a port reservation held by a separate operating-system process and permits it after that process releases the file.")]
	public void Acquire_ShouldSerializeIndependentProcesses_UsingExclusiveMachineFile() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Machine-wide IIS deployment reservations are Windows-specific.");
		}
		int port = Random.Shared.Next(50000, 60000);
		string lockPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"Creatio", "clio", "deployment-locks", $"iis-port-{port}.lock");
		Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
		string readyPath = Path.Combine(Path.GetTempPath(), $"clio-port-lock-ready-{Guid.NewGuid():N}");
		ProcessStartInfo startInfo = new("powershell.exe") {
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("-NoProfile");
		startInfo.ArgumentList.Add("-NonInteractive");
		startInfo.ArgumentList.Add("-Command");
		string quotedLockPath = lockPath.Replace("'", "''");
		string quotedReadyPath = readyPath.Replace("'", "''");
		startInfo.ArgumentList.Add(
			$"$stream=[IO.File]::Open('{quotedLockPath}','OpenOrCreate','ReadWrite','None');"
			+ $"[IO.File]::WriteAllText('{quotedReadyPath}','ready');Start-Sleep -Seconds 30;$stream.Dispose()");
		using Process child = Process.Start(startInfo)!;
		try {
			SpinWait.SpinUntil(() => File.Exists(readyPath) || child.HasExited, TimeSpan.FromSeconds(10))
				.Should().BeTrue(because: "the child must signal only after it owns the exclusive lock");
			string childError = child.HasExited ? child.StandardError.ReadToEnd() : string.Empty;
			child.HasExited.Should().BeFalse(
				because: $"the independent lock owner must remain alive during acquisition; stderr: {childError}");
			IAvailableIisPortService availability = Substitute.For<IAvailableIisPortService>();
			availability.FindAsync(port, port).Returns(new FindAvailableIisPortResult(
				"available", "free", port, port, port, 0, 0));
			IIisDeploymentPortReservation sut = new IisDeploymentPortReservation(availability);

			// Act
			Action act = () => sut.Acquire(port);

			// Assert
			act.Should().Throw<InvalidOperationException>()
				.WithMessage("*reserved by another clio deployment*",
					because: "process-local synchronization would not protect two terminals");
			child.Kill(entireProcessTree: true);
			child.WaitForExit(10000).Should().BeTrue(because: "process exit must release the operating-system file handle");
			Action later = () => {
				using IDisposable lease = sut.Acquire(port);
			};
			later.Should().NotThrow(because: "the reservation must become reusable after the other process exits");
		}
		finally {
			if (!child.HasExited) {
				child.Kill(entireProcessTree: true);
				child.WaitForExit();
			}
			File.Delete(readyPath);
		}
	}

	[Test]
	[Description("Releases an IIS port reservation so a later deployment can revalidate and acquire it.")]
	public void Acquire_ShouldAllowLaterOwner_AfterLeaseIsDisposed() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Machine-wide IIS deployment reservations are Windows-specific.");
		}
		int port = Random.Shared.Next(60001, 65000);
		IAvailableIisPortService availability = Substitute.For<IAvailableIisPortService>();
		availability.FindAsync(port, port).Returns(new FindAvailableIisPortResult(
			"available", "free", port, port, port, 0, 0));
		IIisDeploymentPortReservation sut = new IisDeploymentPortReservation(availability);
		IDisposable firstLease = sut.Acquire(port);
		firstLease.Dispose();

		// Act
		Action act = () => {
			using IDisposable secondLease = sut.Acquire(port);
		};

		// Assert
		act.Should().NotThrow(
			because: "disposing a reservation must not leave a stale lock after deployment completes or fails");
	}

}
