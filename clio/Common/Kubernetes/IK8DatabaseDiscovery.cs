using System.Collections.Generic;
using System.Threading.Tasks;

namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Interface for discovering databases in Kubernetes.
	/// </summary>
	public interface IK8DatabaseDiscovery
	{
		/// <summary>
		/// Discovers databases of specified engines in the cluster.
		/// </summary>
		Task<List<DiscoveredDatabase>> DiscoverDatabasesAsync(List<DatabaseEngine> engines, string namespaceParam);
	}
}
