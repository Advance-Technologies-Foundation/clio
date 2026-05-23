using System.Threading.Tasks;
using Clio.Common.IIS;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.IIS;

[TestFixture]
[Property("Module", "Common")]
public sealed class AvailableIisPortServiceTests
{
	[Test]
	[Category("Unit")]
	[Description("Returns the first port in the requested range that is neither bound in IIS nor reserved by active TCP usage.")]
	public async Task FindAsync_Should_Return_First_Free_Port()
	{
		// Arrange
		IIISSiteDetector iisSiteDetector = Substitute.For<IIISSiteDetector>();
		IPlatformDetector platformDetector = Substitute.For<IPlatformDetector>();
		ITcpPortReservationReader tcpPortReservationReader = Substitute.For<ITcpPortReservationReader>();
		platformDetector.IsWindows().Returns(true);
		iisSiteDetector.GetBoundPorts(40000, 40005).Returns([40001, 40002]);
		tcpPortReservationReader.GetReservedPorts(40000, 40005).Returns([40000, 40002]);
		AvailableIisPortService sut = new(iisSiteDetector, platformDetector, tcpPortReservationReader);

		// Act
		FindAvailableIisPortResult result = await sut.FindAsync(40000, 40005);

		// Assert
		result.Status.Should().Be("available",
			because: "the service should report available when at least one port in the requested range is free");
		result.FirstAvailablePort.Should().Be(40003,
			because: "40003 is the first port that is not occupied by IIS bindings or active TCP reservations");
		result.IisBoundPortCount.Should().Be(2,
			because: "the response should preserve how many IIS-bound ports were seen during the scan");
		result.ActiveTcpPortCount.Should().Be(2,
			because: "the response should preserve how many active TCP reservations were seen during the scan");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns unavailable when every port in the requested range is already reserved.")]
	public async Task FindAsync_Should_Return_Unavailable_When_Range_Is_Full()
	{
		// Arrange
		IIISSiteDetector iisSiteDetector = Substitute.For<IIISSiteDetector>();
		IPlatformDetector platformDetector = Substitute.For<IPlatformDetector>();
		ITcpPortReservationReader tcpPortReservationReader = Substitute.For<ITcpPortReservationReader>();
		platformDetector.IsWindows().Returns(true);
		iisSiteDetector.GetBoundPorts(40000, 40002).Returns([40000, 40002]);
		tcpPortReservationReader.GetReservedPorts(40000, 40002).Returns([40001]);
		AvailableIisPortService sut = new(iisSiteDetector, platformDetector, tcpPortReservationReader);

		// Act
		FindAvailableIisPortResult result = await sut.FindAsync(40000, 40002);

		// Assert
		result.Status.Should().Be("unavailable",
			because: "the service should report unavailable when the entire requested range is already reserved");
		result.FirstAvailablePort.Should().BeNull(
			because: "there is no free IIS deployment port left in the requested range");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns unavailable on non-Windows hosts instead of pretending IIS port discovery can run there.")]
	public async Task FindAsync_Should_Return_Unavailable_On_NonWindows()
	{
		// Arrange
		IIISSiteDetector iisSiteDetector = Substitute.For<IIISSiteDetector>();
		IPlatformDetector platformDetector = Substitute.For<IPlatformDetector>();
		ITcpPortReservationReader tcpPortReservationReader = Substitute.For<ITcpPortReservationReader>();
		platformDetector.IsWindows().Returns(false);
		AvailableIisPortService sut = new(iisSiteDetector, platformDetector, tcpPortReservationReader);

		// Act
		FindAvailableIisPortResult result = await sut.FindAsync(40000, 42000);

		// Assert
		result.Status.Should().Be("unavailable",
			because: "IIS port discovery is a Windows-only capability");
		result.FirstAvailablePort.Should().BeNull(
			because: "non-Windows hosts cannot produce an IIS deployment port recommendation");
	}
}
