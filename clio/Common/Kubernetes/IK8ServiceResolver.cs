using System.Threading.Tasks;

namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Represents resolved service information.
	/// </summary>
	public class ServiceInfo
	{
		public string ServiceName { get; set; }
		public string Host { get; set; }
		public int Port { get; set; }
	}

	/// <summary>
	/// Interface for resolving Kubernetes services and ports.
	/// </summary>
	public interface IK8ServiceResolver
	{
		/// <summary>
		/// Resolves service information for a database engine.
		/// </summary>
		Task<ServiceInfo> ResolveServiceAsync(DatabaseEngine engine, string namespaceParam);

		/// <summary>
		/// Resolves service information for Redis.
		/// </summary>
		Task<ServiceInfo> ResolveRedisServiceAsync(string namespaceParam);
	}
}
