using System;
using System.Linq;
using System.Threading.Tasks;
using k8s;
using k8s.Models;

namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Implementation of Kubernetes client operations.
	/// </summary>
	public class KubernetesClient : IKubernetesClient
	{
		private readonly IKubernetes _k8s;
		private readonly KubernetesClientConfiguration _config;

		public KubernetesClient(IKubernetes k8s)
		{
			_k8s = k8s ?? throw new ArgumentNullException(nameof(k8s));
			_config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
		}

		public async Task<K8Context> GetCurrentContextAsync()
		{
			var config = KubernetesClientConfiguration.LoadKubeConfig();
			var currentContext = config.Contexts?.FirstOrDefault(c => c.Name == config.CurrentContext);

			if (currentContext == null)
			{
				throw new InvalidOperationException("No current context found in kubeconfig");
			}

			var cluster = config.Clusters?.FirstOrDefault(c => c.Name == currentContext.ContextDetails?.Cluster);

			return new K8Context
			{
				Name = currentContext.Name,
				Cluster = currentContext.ContextDetails?.Cluster,
				Namespace = currentContext.ContextDetails?.Namespace ?? "default",
				Server = cluster?.ClusterEndpoint?.Server
			};
		}

		public async Task<bool> ValidateConnectivityAsync()
		{
			try
			{
				await _k8s.CoreV1.ListNamespaceAsync(limit: 1);
				return true;
			}
			catch
			{
				return false;
			}
		}

		public async Task<VersionInfo> GetVersionAsync()
		{
			return await _k8s.Version.GetCodeAsync();
		}

		public async Task<V1PodList> ListPodsAsync(string namespaceParam, string labelSelector)
		{
			return await _k8s.CoreV1.ListNamespacedPodAsync(namespaceParam, labelSelector: labelSelector);
		}

		public async Task<V1ServiceList> ListServicesAsync(string namespaceParam, string labelSelector)
		{
			return await _k8s.CoreV1.ListNamespacedServiceAsync(namespaceParam, labelSelector: labelSelector);
		}

		public async Task<V1StatefulSetList> ListStatefulSetsAsync(string namespaceParam, string labelSelector)
		{
			return await _k8s.AppsV1.ListNamespacedStatefulSetAsync(namespaceParam, labelSelector: labelSelector);
		}

		public async Task<V1DeploymentList> ListDeploymentsAsync(string namespaceParam, string labelSelector)
		{
			return await _k8s.AppsV1.ListNamespacedDeploymentAsync(namespaceParam, labelSelector: labelSelector);
		}

		public async Task<bool> NamespaceExistsAsync(string namespaceParam)
		{
			try
			{
				await _k8s.CoreV1.ReadNamespaceAsync(namespaceParam);
				return true;
			}
			catch
			{
				return false;
			}
		}

		public async Task<V1Secret> GetSecretAsync(string namespaceParam, string secretName)
		{
			try
			{
				return await _k8s.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceParam);
			}
			catch
			{
				return null;
			}
		}
	}
}
