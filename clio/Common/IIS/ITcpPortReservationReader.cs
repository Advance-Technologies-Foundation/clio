using System.Collections.Generic;

namespace Clio.Common.IIS;

/// <summary>
/// Reads TCP ports that are already reserved by active listeners or connections.
/// </summary>
public interface ITcpPortReservationReader
{
	/// <summary>
	/// Gets reserved TCP ports inside the requested inclusive range.
	/// </summary>
	/// <param name="rangeStart">Inclusive start of the port range.</param>
	/// <param name="rangeEnd">Inclusive end of the port range.</param>
	/// <returns>Distinct reserved TCP ports inside the requested range.</returns>
	IReadOnlyCollection<int> GetReservedPorts(int rangeStart, int rangeEnd);
}
