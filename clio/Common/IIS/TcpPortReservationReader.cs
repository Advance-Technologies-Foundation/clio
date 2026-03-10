using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace Clio.Common.IIS;

/// <summary>
/// Reads active TCP listeners and connections to determine which ports are already reserved.
/// </summary>
public sealed class TcpPortReservationReader : ITcpPortReservationReader
{
	/// <inheritdoc />
	public IReadOnlyCollection<int> GetReservedPorts(int rangeStart, int rangeEnd)
	{
		IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
		HashSet<int> reservedPorts = [];

		foreach (TcpConnectionInformation connection in ipGlobalProperties.GetActiveTcpConnections())
		{
			AddPortIfInRange(connection.LocalEndPoint.Port, rangeStart, rangeEnd, reservedPorts);
		}

		foreach (IPEndPoint listener in ipGlobalProperties.GetActiveTcpListeners())
		{
			AddPortIfInRange(listener.Port, rangeStart, rangeEnd, reservedPorts);
		}

		return reservedPorts.OrderBy(port => port).ToArray();
	}

	private static void AddPortIfInRange(int port, int rangeStart, int rangeEnd, ISet<int> reservedPorts)
	{
		if (port >= rangeStart && port <= rangeEnd)
		{
			reservedPorts.Add(port);
		}
	}
}
