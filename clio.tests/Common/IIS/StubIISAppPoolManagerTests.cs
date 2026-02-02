using System.Threading.Tasks;
using Clio.Common.IIS;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.IIS;

[TestFixture]
[Category("Unit")]
public class StubIISAppPoolManagerTests
{
	[Test]
	[Description("Returns NotAvailable state for any app pool name")]
	public async Task GetAppPoolState_ReturnsNotAvailable_ForAnyAppPool()
	{
		// Arrange
		StubIISAppPoolManager manager = new();

		// Act
		string state = await manager.GetAppPoolState("TestAppPool");

		// Assert
		state.Should().Be("NotAvailable", because: "stub implementation is used on non-Windows platforms");
	}

	[Test]
	[Description("Returns false when checking if app pool is running")]
	public async Task IsAppPoolRunning_ReturnsFalse_ForAnyAppPool()
	{
		// Arrange
		StubIISAppPoolManager manager = new();

		// Act
		bool isRunning = await manager.IsAppPoolRunning("TestAppPool");

		// Assert
		isRunning.Should().BeFalse(because: "IIS is not available on non-Windows platforms");
	}

	[Test]
	[Description("Returns false when starting app pool")]
	public async Task StartAppPool_ReturnsFalse_ForAnyAppPool()
	{
		// Arrange
		StubIISAppPoolManager manager = new();

		// Act
		bool started = await manager.StartAppPool("TestAppPool");

		// Assert
		started.Should().BeFalse(because: "IIS operations are not supported on non-Windows platforms");
	}

	[Test]
	[Description("Returns false when stopping app pool")]
	public async Task StopAppPool_ReturnsFalse_ForAnyAppPool()
	{
		// Arrange
		StubIISAppPoolManager manager = new();

		// Act
		bool stopped = await manager.StopAppPool("TestAppPool");

		// Assert
		stopped.Should().BeFalse(because: "IIS operations are not supported on non-Windows platforms");
	}

	[Test]
	[Description("Handles null app pool name gracefully")]
	public async Task StubIISAppPoolManager_HandlesNull_Gracefully()
	{
		// Arrange
		StubIISAppPoolManager manager = new();

		// Act & Assert - should not throw
		string state = await manager.GetAppPoolState(null);
		bool isRunning = await manager.IsAppPoolRunning(null);
		bool started = await manager.StartAppPool(null);
		bool stopped = await manager.StopAppPool(null);

		state.Should().Be("NotAvailable");
		isRunning.Should().BeFalse();
		started.Should().BeFalse();
		stopped.Should().BeFalse();
	}
}
