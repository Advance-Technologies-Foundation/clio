using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using k8s.Models;

namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Discovers databases in Kubernetes using label-based detection.
	/// </summary>
	public class K8DatabaseDiscovery : IK8DatabaseDiscovery
	{
		private readonly IKubernetesClient _k8sClient;
		private readonly IK8ServiceResolver _serviceResolver;

		public K8DatabaseDiscovery(IKubernetesClient k8sClient, IK8ServiceResolver serviceResolver)
		{
			_k8sClient = k8sClient ?? throw new ArgumentNullException(nameof(k8sClient));
			_serviceResolver = serviceResolver ?? throw new ArgumentNullException(nameof(serviceResolver));
		}

		public async Task<List<DiscoveredDatabase>> DiscoverDatabasesAsync(List<DatabaseEngine> engines, string namespaceParam)
		{
			var databases = new List<DiscoveredDatabase>();

			foreach (var engine in engines)
			{
				var discovered = await DiscoverEngineAsync(engine, namespaceParam);
				databases.AddRange(discovered);
			}

			return databases;
		}

		private async Task<List<DiscoveredDatabase>> DiscoverEngineAsync(DatabaseEngine engine, string namespaceParam)
		{
			var labelSelector = GetLabelSelector(engine);
			var appName = GetAppName(engine);

			// Get all StatefulSets in namespace (can't filter by label on StatefulSet metadata)
			var allStatefulSets = await _k8sClient.ListStatefulSetsAsync(namespaceParam, null);

			if (allStatefulSets == null || !allStatefulSets.Items.Any())
			{
				return new List<DiscoveredDatabase>();
			}

			// Filter by selector.matchLabels
			var statefulSets = allStatefulSets.Items.Where(sts => 
				sts.Spec?.Selector?.MatchLabels != null && 
				sts.Spec.Selector.MatchLabels.ContainsKey("app") &&
				sts.Spec.Selector.MatchLabels["app"] == appName).ToList();

			if (!statefulSets.Any())
			{
				return new List<DiscoveredDatabase>();
			}

			var databases = new List<DiscoveredDatabase>();

			foreach (var sts in statefulSets)
			{
				// Get pods for this StatefulSet using label selector
				var pods = await _k8sClient.ListPodsAsync(namespaceParam, labelSelector);

				// Check if at least one pod is ready and not permanently failed
				var readyPods = pods.Items.Where(p => IsPodReady(p)).ToList();

				if (!readyPods.Any())
				{
					continue; // Skip this database if no pods are ready
				}

				// Resolve service and port
				var serviceInfo = await _serviceResolver.ResolveServiceAsync(engine, namespaceParam);

				if (serviceInfo == null)
				{
					continue; // Skip if service not found
				}
				if (serviceInfo.Host == "localhost") {
					serviceInfo.Host = "127.0.0.1";
				}
				var database = new DiscoveredDatabase
				{
					Engine = engine,
					Name = sts.Metadata.Name,
					PodName = readyPods.First().Metadata.Name,
					ServiceName = serviceInfo.ServiceName,
					Port = serviceInfo.Port,
					Host = serviceInfo.Host,
					IsReady = true
				};

				databases.Add(database);
			}

			return databases;
		}

		private bool IsPodReady(V1Pod pod)
		{
			if (pod.Status?.Phase != "Running" && pod.Status?.Phase != "Pending")
			{
				return false;
			}

			// Check if permanently failed
			if (pod.Status?.Phase == "Failed" || pod.Status?.Phase == "Unknown")
			{
				return false;
			}

			// Check readiness
			var readyCondition = pod.Status?.Conditions?.FirstOrDefault(c => c.Type == "Ready");
			return readyCondition?.Status == "True";
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

		private string GetAppName(DatabaseEngine engine)
		{
			return engine switch
			{
				DatabaseEngine.Postgres => "clio-postgres",
				DatabaseEngine.Mssql => "clio-mssql",
				_ => throw new ArgumentException($"Unknown engine: {engine}")
			};
		}
	}
}
