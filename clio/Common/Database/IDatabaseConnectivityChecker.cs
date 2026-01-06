using System.Threading.Tasks;
using Clio.Common.Kubernetes;

namespace Clio.Common.Database
{
	/// <summary>
	/// Interface for checking database connectivity.
	/// </summary>
	public interface IDatabaseConnectivityChecker
	{
		/// <summary>
		/// Checks if a database is connectable via TCP.
		/// </summary>
		Task<bool> CheckConnectivityAsync(DiscoveredDatabase database, int timeoutSeconds = 10);
	}
}
