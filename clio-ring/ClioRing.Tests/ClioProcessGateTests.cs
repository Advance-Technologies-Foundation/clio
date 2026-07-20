using System;
using System.Threading.Tasks;
using ClioRing.Ipc;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ClioRing.Tests;

[TestFixture]
[Category("Unit")]
public sealed class ClioProcessGateTests {
	[Test]
	[Description("An update waits for existing clio processes and prevents new clio launches until it completes.")]
	public async Task AcquireUpdateLeaseAsync_ShouldCreateExclusiveUpdateWindow() {
		// Arrange
		var services = new ServiceCollection();
		services.AddSingleton<IClioProcessGate, ClioProcessGate>();
		IClioProcessGate sut = services.BuildServiceProvider().GetRequiredService<IClioProcessGate>();
		IAsyncDisposable existingProcess = await sut.AcquireProcessLeaseAsync();

		// Act
		Task<IAsyncDisposable> updateTask = sut.AcquireUpdateLeaseAsync();
		await Task.Delay(25);

		// Assert
		updateTask.IsCompleted.Should().BeFalse(because: "the existing clio process still owns a shared lease");
		await existingProcess.DisposeAsync();
		IAsyncDisposable update = await updateTask;
		Task<IAsyncDisposable> newProcessTask = sut.AcquireProcessLeaseAsync();
		await Task.Delay(25);
		newProcessTask.IsCompleted.Should().BeFalse(because: "no clio process may start during tool replacement");
		await update.DisposeAsync();
		IAsyncDisposable newProcess = await newProcessTask;
		newProcess.Should().NotBeNull(because: "ordinary launches resume after the update lease is released");
		await newProcess.DisposeAsync();
	}
}
