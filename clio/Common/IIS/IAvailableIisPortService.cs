using System.Threading.Tasks;

namespace Clio.Common.IIS;

/// <summary>
/// Finds an available IIS deployment port inside a requested range.
/// </summary>
public interface IAvailableIisPortService
{
	/// <summary>
	/// Finds the first available IIS deployment port inside the requested inclusive range.
	/// </summary>
	/// <param name="rangeStart">Inclusive start of the port range.</param>
	/// <param name="rangeEnd">Inclusive end of the port range.</param>
	/// <returns>The structured IIS port discovery result.</returns>
	Task<FindAvailableIisPortResult> FindAsync(int rangeStart, int rangeEnd);
}
