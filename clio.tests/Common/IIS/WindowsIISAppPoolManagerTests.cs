using System.Threading.Tasks;
using Clio.Common.IIS;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.IIS;

[TestFixture]
[Category("Unit")]
public class WindowsIISAppPoolManagerTests
{
	[Test]
	[Description("Returns NotFound or Unknown when app pool does not exist (depends on IIS availability)")]
	public async Task GetAppPoolState_ReturnsNotFound_WhenAppPoolDoesNotExist()
	{
		// Arrange
		WindowsIISAppPoolManager manager = new();
		string nonExistentAppPool = "NonExistentAppPool_" + System.Guid.NewGuid();

		// Act
		string state = await manager.GetAppPoolState(nonExistentAppPool);

		// Assert
		// On systems WITH IIS: returns "NotFound"
		// On systems WITHOUT IIS: returns "Unknown"
		state.Should().Match(s => s == "NotFound" || s == "Unknown", 
			"app pool does not exist (or IIS/appcmd is not installed)");
	}

	[Test]
	[Description("Returns Unknown state when app pool name is null or empty")]
	public async Task GetAppPoolState_ReturnsUnknown_WhenAppPoolNameIsNullOrEmpty()
	{
		// Arrange
		WindowsIISAppPoolManager manager = new();

		// Act
		string stateNull = await manager.GetAppPoolState(null);
		string stateEmpty = await manager.GetAppPoolState(string.Empty);
		string stateWhitespace = await manager.GetAppPoolState("   ");

		// Assert
		stateNull.Should().Be("Unknown", because: "null app pool name is invalid");
		stateEmpty.Should().Be("Unknown", because: "empty app pool name is invalid");
		stateWhitespace.Should().Be("Unknown", because: "whitespace app pool name is invalid");
	}

	[Test]
	[Description("Returns false when checking if non-existent app pool is running")]
	public async Task IsAppPoolRunning_ReturnsFalse_WhenAppPoolDoesNotExist()
	{
		// Arrange
		WindowsIISAppPoolManager manager = new();
		string nonExistentAppPool = "NonExistentAppPool_" + System.Guid.NewGuid();

		// Act
		bool isRunning = await manager.IsAppPoolRunning(nonExistentAppPool);

		// Assert
		isRunning.Should().BeFalse(because: "a non-existent app pool cannot be running");
	}

	[Test]
	[Description("Returns false when starting non-existent app pool")]
	public async Task StartAppPool_ReturnsFalse_WhenAppPoolDoesNotExist()
	{
		// Arrange
		WindowsIISAppPoolManager manager = new();
		string nonExistentAppPool = "NonExistentAppPool_" + System.Guid.NewGuid();

		// Act
		bool started = await manager.StartAppPool(nonExistentAppPool);

		// Assert
		started.Should().BeFalse(because: "cannot start a non-existent app pool");
	}

	[Test]
	[Description("Returns false when stopping non-existent app pool")]
	public async Task StopAppPool_ReturnsFalse_WhenAppPoolDoesNotExist()
	{
		// Arrange
		WindowsIISAppPoolManager manager = new();
		string nonExistentAppPool = "NonExistentAppPool_" + System.Guid.NewGuid();

		// Act
		bool stopped = await manager.StopAppPool(nonExistentAppPool);

		// Assert
		stopped.Should().BeFalse(because: "cannot stop a non-existent app pool");
	}

	[Test]
	[Description("Returns false when starting app pool with null or empty name")]
	public async Task StartAppPool_ReturnsFalse_WhenAppPoolNameIsNullOrEmpty()
	{
		// Arrange
		WindowsIISAppPoolManager manager = new();

		// Act
		bool startedNull = await manager.StartAppPool(null);
		bool startedEmpty = await manager.StartAppPool(string.Empty);
		bool startedWhitespace = await manager.StartAppPool("   ");

		// Assert
		startedNull.Should().BeFalse(because: "null app pool name is invalid");
		startedEmpty.Should().BeFalse(because: "empty app pool name is invalid");
		startedWhitespace.Should().BeFalse(because: "whitespace app pool name is invalid");
	}

	[Test]
	[Description("Returns false when stopping app pool with null or empty name")]
	public async Task StopAppPool_ReturnsFalse_WhenAppPoolNameIsNullOrEmpty()
	{
		// Arrange
		WindowsIISAppPoolManager manager = new();

		// Act
		bool stoppedNull = await manager.StopAppPool(null);
		bool stoppedEmpty = await manager.StopAppPool(string.Empty);
		bool stoppedWhitespace = await manager.StopAppPool("   ");

		// Assert
		stoppedNull.Should().BeFalse(because: "null app pool name is invalid");
		stoppedEmpty.Should().BeFalse(because: "empty app pool name is invalid");
		stoppedWhitespace.Should().BeFalse(because: "whitespace app pool name is invalid");
	}
}
