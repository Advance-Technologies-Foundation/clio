using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Clio.Common.Assertions;

namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Validates Kubernetes context before running assertions.
	/// </summary>
	public class K8ContextValidator
	{
		private readonly IKubernetesClient _k8sClient;

		public K8ContextValidator(IKubernetesClient k8sClient)
		{
			_k8sClient = k8sClient ?? throw new ArgumentNullException(nameof(k8sClient));
		}

		/// <summary>
		/// Validates the Kubernetes context against the provided options.
		/// This is Phase 0 - mandatory check before all K8 assertions.
		/// </summary>
		public async Task<AssertionResult> ValidateContextAsync(
			string expectedContext = null,
			string contextRegex = null,
			string expectedCluster = null,
			string expectedNamespace = null)
		{
			try
			{
				// Get current context
				var context = await _k8sClient.GetCurrentContextAsync();

				// Validate connectivity
				bool isConnected = await _k8sClient.ValidateConnectivityAsync();
				if (!isConnected)
				{
					return AssertionResult.Failure(
						AssertionScope.K8,
						AssertionPhase.K8Context,
						"API server unreachable"
					);
				}

				// Validate context name if specified
				if (!string.IsNullOrEmpty(expectedContext) && context.Name != expectedContext)
				{
					var result = AssertionResult.Failure(
						AssertionScope.K8,
						AssertionPhase.K8Context,
						$"current context '{context.Name}' does not match expected '{expectedContext}'"
					);
					result.Context["currentContext"] = context.Name;
					result.Context["expectedContext"] = expectedContext;
					return result;
				}

				// Validate context regex if specified
				if (!string.IsNullOrEmpty(contextRegex))
				{
					var regex = new Regex(contextRegex);
					if (!regex.IsMatch(context.Name))
					{
						var result = AssertionResult.Failure(
							AssertionScope.K8,
							AssertionPhase.K8Context,
							$"current context '{context.Name}' does not match pattern '{contextRegex}'"
						);
						result.Context["currentContext"] = context.Name;
						result.Context["expectedPattern"] = contextRegex;
						return result;
					}
				}

				// Validate cluster name if specified
				if (!string.IsNullOrEmpty(expectedCluster) && context.Cluster != expectedCluster)
				{
					var result = AssertionResult.Failure(
						AssertionScope.K8,
						AssertionPhase.K8Context,
						$"current cluster '{context.Cluster}' does not match expected '{expectedCluster}'"
					);
					result.Context["currentCluster"] = context.Cluster;
					result.Context["expectedCluster"] = expectedCluster;
					return result;
				}

				// Validate namespace if specified
				if (!string.IsNullOrEmpty(expectedNamespace) && context.Namespace != expectedNamespace)
				{
					var result = AssertionResult.Failure(
						AssertionScope.K8,
						AssertionPhase.K8Context,
						$"current namespace '{context.Namespace}' does not match expected '{expectedNamespace}'"
					);
					result.Context["currentNamespace"] = context.Namespace;
					result.Context["expectedNamespace"] = expectedNamespace;
					return result;
				}

				// Success - add context info
				var successResult = AssertionResult.Success();
				successResult.Context["name"] = context.Name;
				successResult.Context["cluster"] = context.Cluster;
				successResult.Context["server"] = context.Server;
				successResult.Context["namespace"] = context.Namespace;

				return successResult;
			}
			catch (Exception ex)
			{
				var result = AssertionResult.Failure(
					AssertionScope.K8,
					AssertionPhase.K8Context,
					$"Failed to validate context: {ex.Message}"
				);
				result.Details["exception"] = ex.GetType().Name;
				return result;
			}
		}
	}
}
