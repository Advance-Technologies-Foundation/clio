using System.Threading.Tasks;
using k8s.Models;

namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Represents Kubernetes context information.
	/// </summary>
	public class K8Context
	{
		public string Name { get; set; }
		public string Cluster { get; set; }
		public string Namespace { get; set; }
		public string Server { get; set; }
	}

	/// <summary>
	/// Abstraction for Kubernetes client operations.
	/// </summary>
	public interface IKubernetesClient
	{
		/// <summary>
		/// Gets the current Kubernetes context.
		/// </summary>
		Task<K8Context> GetCurrentContextAsync();

		/// <summary>
		/// Validates connectivity to the Kubernetes API server.
		/// </summary>
		Task<bool> ValidateConnectivityAsync();

		/// <summary>
		/// Gets the Kubernetes version information.
		/// </summary>
		Task<VersionInfo> GetVersionAsync();

		/// <summary>
		/// Lists pods in a specific namespace with label selector.
		/// </summary>
		Task<V1PodList> ListPodsAsync(string namespaceParam, string labelSelector);

		/// <summary>
		/// Lists services in a specific namespace with label selector.
		/// </summary>
		Task<V1ServiceList> ListServicesAsync(string namespaceParam, string labelSelector);

		/// <summary>
		/// Lists StatefulSets in a specific namespace with label selector.
		/// </summary>
		Task<V1StatefulSetList> ListStatefulSetsAsync(string namespaceParam, string labelSelector);

		/// <summary>
		/// Lists Deployments in a specific namespace with label selector.
		/// </summary>
		Task<V1DeploymentList> ListDeploymentsAsync(string namespaceParam, string labelSelector);

		/// <summary>
		/// Checks if a namespace exists.
		/// </summary>
		Task<bool> NamespaceExistsAsync(string namespaceParam);

/// <summary>
/// Gets a secret from the specified namespace.
/// </summary>
Task<V1Secret> GetSecretAsync(string namespaceParam, string secretName);
}
}

