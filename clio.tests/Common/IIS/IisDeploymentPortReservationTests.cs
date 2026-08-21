using System;
using System.Threading.Tasks;
using Clio.Common.IIS;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.IIS;

[TestFixture]
[Property("Module", "Common")]
public sealed class IisDeploymentPortReservationTests {

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
