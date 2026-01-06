using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Clio.Common.Assertions;

namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Represents a discovered Redis instance.
	/// </summary>
	public class DiscoveredRedis
	{
		public string Name { get; set; }
		public string PodName { get; set; }
		public string ServiceName { get; set; }
		public int Port { get; set; }
		public string Host { get; set; }
		public bool IsReady { get; set; }
	}

	/// <summary>
	/// Executes Redis assertions for Kubernetes.
	/// </summary>
	public class K8RedisAssertion
	{
		private readonly IKubernetesClient _k8sClient;
		private readonly IK8ServiceResolver _serviceResolver;

		public K8RedisAssertion(IKubernetesClient k8sClient, IK8ServiceResolver serviceResolver)
		{
			_k8sClient = k8sClient ?? throw new ArgumentNullException(nameof(k8sClient));
			_serviceResolver = serviceResolver ?? throw new ArgumentNullException(nameof(serviceResolver));
		}

		public async Task<AssertionResult> ExecuteAsync(
			bool checkConnect,
			bool checkPing,
			string namespaceParam)
		{
			// Discover Redis
			var redis = await DiscoverRedisAsync(namespaceParam);

			if (redis == null)
			{
				return AssertionResult.Failure(
					AssertionScope.K8,
					AssertionPhase.RedisDiscovery,
					"Redis not found in cluster"
				);
			}

			// Check connectivity if requested
			if (checkConnect)
			{
				bool isConnectable = await CheckConnectivityAsync(redis);
				if (!isConnectable)
				{
					var result = AssertionResult.Failure(
						AssertionScope.K8,
						AssertionPhase.RedisConnect,
						$"Cannot connect to Redis at {redis.Host}:{redis.Port}"
					);
					result.Details["host"] = redis.Host;
					result.Details["port"] = redis.Port;
					return result;
				}
			}

			// Check ping if requested
			if (checkPing)
			{
				bool pingSuccess = await CheckPingAsync(redis);
				if (!pingSuccess)
				{
					var result = AssertionResult.Failure(
						AssertionScope.K8,
						AssertionPhase.RedisPing,
						$"Redis PING command failed at {redis.Host}:{redis.Port}"
					);
					result.Details["host"] = redis.Host;
					result.Details["port"] = redis.Port;
					return result;
				}
			}

			// Success
			var successResult = AssertionResult.Success();
			successResult.Resolved["redis"] = new
			{
				name = redis.Name,
				host = redis.Host,
				port = redis.Port
			};
			return successResult;
		}

		private async Task<DiscoveredRedis> DiscoverRedisAsync(string namespaceParam)
		{
			const string labelSelector = "app=clio-redis";
			const string appName = "clio-redis";

			// Get all Deployments in namespace (can't filter by label on Deployment metadata)
			var allDeployments = await _k8sClient.ListDeploymentsAsync(namespaceParam, null);

			if (allDeployments == null || !allDeployments.Items.Any())
			{
				return null;
			}

			// Filter by selector.matchLabels
			var deployment = allDeployments.Items.FirstOrDefault(d =>
				d.Spec?.Selector?.MatchLabels != null &&
				d.Spec.Selector.MatchLabels.ContainsKey("app") &&
				d.Spec.Selector.MatchLabels["app"] == appName);

			if (deployment == null)
			{
				return null;
			}

			// Get pods for this Deployment using label selector
			var pods = await _k8sClient.ListPodsAsync(namespaceParam, labelSelector);

			// Check if at least one pod is ready
			var readyPods = pods.Items.Where(p => IsPodReady(p)).ToList();

			if (!readyPods.Any())
			{
				return null;
			}

			// Resolve service and port
			var serviceInfo = await _serviceResolver.ResolveRedisServiceAsync(namespaceParam);

			if (serviceInfo == null)
			{
				return null;
			}

			return new DiscoveredRedis
			{
				Name = deployment.Metadata.Name,
				PodName = readyPods.First().Metadata.Name,
				ServiceName = serviceInfo.ServiceName,
				Port = serviceInfo.Port,
				Host = serviceInfo.Host,
				IsReady = true
			};
		}

		private bool IsPodReady(k8s.Models.V1Pod pod)
		{
			if (pod.Status?.Phase != "Running" && pod.Status?.Phase != "Pending")
			{
				return false;
			}

			if (pod.Status?.Phase == "Failed" || pod.Status?.Phase == "Unknown")
			{
				return false;
			}

			var readyCondition = pod.Status?.Conditions?.FirstOrDefault(c => c.Type == "Ready");
			return readyCondition?.Status == "True";
		}

		private async Task<bool> CheckConnectivityAsync(DiscoveredRedis redis, int timeoutSeconds = 10)
		{
			try
			{
				using var client = new TcpClient();
				var connectTask = client.ConnectAsync(redis.Host, redis.Port);
				var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

				var completedTask = await Task.WhenAny(connectTask, timeoutTask);

				if (completedTask == timeoutTask)
				{
					return false;
				}

				if (connectTask.IsFaulted)
				{
					return false;
				}

				return client.Connected;
			}
			catch
			{
				return false;
			}
		}

		private async Task<bool> CheckPingAsync(DiscoveredRedis redis, int timeoutSeconds = 10)
		{
			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(redis.Host, redis.Port);

				using var stream = client.GetStream();
				
				// Send PING command (Redis protocol)
				var pingCommand = Encoding.UTF8.GetBytes("PING\r\n");
				await stream.WriteAsync(pingCommand, 0, pingCommand.Length);

				// Read response
				var buffer = new byte[1024];
				var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
				var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

				// Check for PONG response
				return response.Contains("PONG");
			}
			catch
			{
				return false;
			}
		}
	}
}
