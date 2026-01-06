using System;
using Clio.Common;
using Clio.Common.Assertions;
using Clio.Common.Kubernetes;

namespace Clio.Command
{
	/// <summary>
	/// Command for validating infrastructure and filesystem resources.
	/// Provides deterministic assertion mechanism for AI agents and humans.
	/// </summary>
	public class AssertCommand : Command<AssertOptions>
	{
		private readonly ILogger _logger;
		private readonly IKubernetesClient _k8sClient;
		private readonly K8ContextValidator _contextValidator;
		private readonly K8DatabaseAssertion _databaseAssertion;
		private readonly K8RedisAssertion _redisAssertion;

		public AssertCommand(
			ILogger logger, 
			IKubernetesClient k8sClient, 
			K8ContextValidator contextValidator,
			K8DatabaseAssertion databaseAssertion,
			K8RedisAssertion redisAssertion)
		{
			_logger = logger;
			_k8sClient = k8sClient;
			_contextValidator = contextValidator;
			_databaseAssertion = databaseAssertion;
			_redisAssertion = redisAssertion;
		}

		public override int Execute(AssertOptions options)
		{
			try
			{
				var scope = ParseScope(options.Scope);

				AssertionResult result = scope switch
				{
					AssertionScope.K8 => ExecuteK8Assertions(options),
					AssertionScope.Fs => ExecuteFsAssertions(options),
					_ => throw new InvalidOperationException($"Unknown scope: {options.Scope}")
				};

				OutputResult(result);

				return result.Status == "pass" ? 0 : 1;
			}
			catch (ArgumentException ex)
			{
				_logger.WriteError($"Invalid invocation: {ex.Message}");
				return 2;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Unexpected error: {ex.Message}");
				return 2;
			}
		}

		private AssertionScope ParseScope(string scope)
		{
			return scope?.ToLowerInvariant() switch
			{
				"k8" => AssertionScope.K8,
				"fs" => AssertionScope.Fs,
				_ => throw new ArgumentException($"Invalid scope: {scope}. Must be 'k8' or 'fs'.")
			};
		}

		private AssertionResult ExecuteK8Assertions(AssertOptions options)
		{
			// Phase 0: Validate Kubernetes context (mandatory)
			var contextResult = _contextValidator.ValidateContextAsync(
				options.Context,
				options.ContextRegex,
				options.Cluster,
				options.Namespace
			).GetAwaiter().GetResult();

			if (contextResult.Status == "fail")
			{
				return contextResult;
			}

			// Validate namespace exists
			const string requiredNamespace = "clio-infrastructure";
			bool namespaceExists = _k8sClient.NamespaceExistsAsync(requiredNamespace).GetAwaiter().GetResult();
			if (!namespaceExists)
			{
				var result = AssertionResult.Failure(
					AssertionScope.K8,
					AssertionPhase.K8Context,
					$"Required namespace '{requiredNamespace}' does not exist"
				);
				result.Details["namespace"] = requiredNamespace;
				return result;
			}

			// Initialize success result with context
			var successResult = AssertionResult.Success();
			successResult.Scope = AssertionScope.K8;
			successResult.Context = contextResult.Context;

			// Execute database assertions if requested
			if (!string.IsNullOrEmpty(options.DatabaseEngines))
			{
				var dbResult = _databaseAssertion.ExecuteAsync(
					options.DatabaseEngines,
					options.DatabaseMinimum,
					options.DatabaseConnect,
					options.DatabaseCheck,
					requiredNamespace
				).GetAwaiter().GetResult();

				if (dbResult.Status == "fail")
				{
					dbResult.Context = contextResult.Context;
					return dbResult;
				}

				// Merge resolved databases
				if (dbResult.Resolved.ContainsKey("databases"))
				{
					successResult.Resolved["databases"] = dbResult.Resolved["databases"];
				}
			}

			// Execute Redis assertions if requested
			if (options.Redis)
			{
				var redisResult = _redisAssertion.ExecuteAsync(
					options.RedisConnect,
					options.RedisPing,
					requiredNamespace
				).GetAwaiter().GetResult();

				if (redisResult.Status == "fail")
				{
					redisResult.Context = contextResult.Context;
					return redisResult;
				}

				// Merge resolved Redis
				if (redisResult.Resolved.ContainsKey("redis"))
				{
					successResult.Resolved["redis"] = redisResult.Resolved["redis"];
				}
			}

			return successResult;
		}

		private AssertionResult ExecuteFsAssertions(AssertOptions options)
		{
			// Phase 1 placeholder - will implement in Phase 5
			var result = AssertionResult.Failure(
				AssertionScope.Fs,
				AssertionPhase.FsPath,
				"Filesystem assertions not yet implemented"
			);
			return result;
		}

		private void OutputResult(AssertionResult result) {
			if (result.Status != "pass") {
				_logger.WriteError("Assertion failed:");
			}
			else {
				_logger.WriteInfo("Assertion passed");
			}
			var json = result.ToJson();
			_logger.WriteLine(json);
		}
	}
}
