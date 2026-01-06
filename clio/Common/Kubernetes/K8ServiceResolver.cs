using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Resolves Kubernetes services and dynamically discovers ports.
	/// </summary>
	public class K8ServiceResolver : IK8ServiceResolver
	{
		private readonly IKubernetesClient _k8sClient;
		private bool _isInCluster;

		public K8ServiceResolver(IKubernetesClient k8sClient)
		{
			_k8sClient = k8sClient ?? throw new ArgumentNullException(nameof(k8sClient));
			_isInCluster = DetectIfInCluster();
		}

		public async Task<ServiceInfo> ResolveServiceAsync(DatabaseEngine engine, string namespaceParam)
		{
			var serviceNamePattern = GetServiceNamePattern(engine);
			
			// Try to find service by label selector (more reliable than name matching)
			var labelSelector = GetLabelSelector(engine);
			var services = await _k8sClient.ListServicesAsync(namespaceParam, labelSelector);
			
			if (services == null || !services.Items.Any())
			{
				return null;
			}

			// Prefer LoadBalancer service if outside cluster, otherwise internal
			var service = _isInCluster
				? services.Items.FirstOrDefault(s => s.Spec?.Type == "ClusterIP") ?? services.Items.First()
				: services.Items.FirstOrDefault(s => s.Spec?.Type == "LoadBalancer") ?? services.Items.First();

			// Dynamically resolve port from service
			var port = ResolvePortFromService(service);
			if (port == 0)
			{
				return null;
			}

			var host = ResolveHost(service, namespaceParam);

			return new ServiceInfo
			{
				ServiceName = service.Metadata.Name,
				Host = host,
				Port = port
			};
		}

		public async Task<ServiceInfo> ResolveRedisServiceAsync(string namespaceParam)
		{
			var labelSelector = "app=clio-redis";

			var services = await _k8sClient.ListServicesAsync(namespaceParam, labelSelector);
			
			if (services == null || !services.Items.Any())
			{
				return null;
			}

			// Prefer LoadBalancer service if outside cluster, otherwise internal
			var service = _isInCluster
				? services.Items.FirstOrDefault(s => s.Spec?.Type == "ClusterIP") ?? services.Items.First()
				: services.Items.FirstOrDefault(s => s.Spec?.Type == "LoadBalancer") ?? services.Items.First();

			var port = ResolvePortFromService(service);
			if (port == 0)
			{
				return null;
			}

			var host = ResolveHost(service, namespaceParam);

			return new ServiceInfo
			{
				ServiceName = service.Metadata.Name,
				Host = host,
				Port = port
			};
		}

		private int ResolvePortFromService(k8s.Models.V1Service service)
		{
			if (service.Spec?.Ports == null || !service.Spec.Ports.Any())
			{
				return 0;
			}

			// Use the first port (spec says to use 'port', not 'targetPort')
			return service.Spec.Ports.First().Port;
		}

		private string ResolveHost(k8s.Models.V1Service service, string namespaceParam)
		{
			// If LoadBalancer, try to get external IP or LoadBalancer IP
			if (service.Spec?.Type == "LoadBalancer")
			{
				// For local clusters, LoadBalancer services are accessible via localhost
				return "localhost";
			}

			// For ClusterIP and internal services, use DNS name
			return $"{service.Metadata.Name}.{namespaceParam}.svc.cluster.local";
		}

		private bool DetectIfInCluster()
		{
			// Check if running inside a Kubernetes cluster
			// This is a simple heuristic - in production, you might check for service account tokens
			var kubernetesServiceHost = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
			return !string.IsNullOrEmpty(kubernetesServiceHost);
		}

		private string GetServiceNamePattern(DatabaseEngine engine)
		{
			return engine switch
			{
				DatabaseEngine.Postgres => "clio-postgres",
				DatabaseEngine.Mssql => "clio-mssql",
				_ => throw new ArgumentException($"Unknown engine: {engine}")
			};
		}

		private string GetLabelSelector(DatabaseEngine engine)
		{
			return engine switch
			{
				DatabaseEngine.Postgres => "app=clio-postgres",
				DatabaseEngine.Mssql => "app=clio-mssql",
				_ => throw new ArgumentException($"Unknown engine: {engine}")
			};
		}
	}
}

