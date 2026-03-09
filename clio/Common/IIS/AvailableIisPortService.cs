using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Clio.Common.IIS;

/// <summary>
/// Finds an available IIS deployment port by excluding IIS bindings and active TCP reservations.
/// </summary>
public sealed class AvailableIisPortService : IAvailableIisPortService
{
	private readonly IIISSiteDetector _iisSiteDetector;
	private readonly IPlatformDetector _platformDetector;
	private readonly ITcpPortReservationReader _tcpPortReservationReader;

	/// <summary>
	/// Initializes a new instance of the <see cref="AvailableIisPortService"/> class.
	/// </summary>
	public AvailableIisPortService(
		IIISSiteDetector iisSiteDetector,
		IPlatformDetector platformDetector,
		ITcpPortReservationReader tcpPortReservationReader)
	{
		_iisSiteDetector = iisSiteDetector ?? throw new ArgumentNullException(nameof(iisSiteDetector));
		_platformDetector = platformDetector ?? throw new ArgumentNullException(nameof(platformDetector));
		_tcpPortReservationReader =
			tcpPortReservationReader ?? throw new ArgumentNullException(nameof(tcpPortReservationReader));
	}

	/// <inheritdoc />
	public async Task<FindAvailableIisPortResult> FindAsync(int rangeStart, int rangeEnd)
	{
		if (!_platformDetector.IsWindows())
		{
			return new FindAvailableIisPortResult(
				"unavailable",
				"IIS port discovery is only available on Windows hosts.",
				rangeStart,
				rangeEnd,
				null,
				0,
				0);
		}

		try
		{
			int[] iisBoundPorts = (await _iisSiteDetector.GetBoundPorts(rangeStart, rangeEnd))
				.Distinct()
				.OrderBy(port => port)
				.ToArray();
			int[] activeTcpPorts = _tcpPortReservationReader.GetReservedPorts(rangeStart, rangeEnd)
				.Distinct()
				.OrderBy(port => port)
				.ToArray();
			HashSet<int> reservedPorts = [.. iisBoundPorts, .. activeTcpPorts];

			int? firstAvailablePort = null;
			foreach (int port in Enumerable.Range(rangeStart, rangeEnd - rangeStart + 1))
			{
				if (!reservedPorts.Contains(port))
				{
					firstAvailablePort = port;
					break;
				}
			}

			if (firstAvailablePort is null)
			{
				return new FindAvailableIisPortResult(
					"unavailable",
					$"No free IIS deployment port was found between {rangeStart} and {rangeEnd}.",
					rangeStart,
					rangeEnd,
					null,
					iisBoundPorts.Length,
					activeTcpPorts.Length);
			}

			return new FindAvailableIisPortResult(
				"available",
					$"Port {firstAvailablePort.Value} is the first free IIS deployment port between {rangeStart} and {rangeEnd}.",
					rangeStart,
					rangeEnd,
					firstAvailablePort,
					iisBoundPorts.Length,
					activeTcpPorts.Length);
		}
		catch (Exception)
		{
			return new FindAvailableIisPortResult(
				"unavailable",
				"IIS port discovery is temporarily unavailable on this host.",
				rangeStart,
				rangeEnd,
				null,
				0,
				0);
		}
	}
}
