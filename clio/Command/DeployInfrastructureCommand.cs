using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.K8;
using CommandLine;
using StackExchange.Redis;

namespace Clio.Command;

[Verb("deploy-infrastructure", Aliases = ["di"],
	HelpText = "Deploy Kubernetes infrastructure for Creatio (namespace, storage, redis, postgres, pgadmin)")]
public class DeployInfrastructureOptions{
	#region Properties: Public

	[Option('p', "path", Required = false,
		HelpText = "Path to infrastructure files (default: auto-detected from clio settings)")]
	public string InfrastructurePath { get; set; }

	[Option("no-verify", Required = false, Default = false,
		HelpText = "Skip connection verification after deployment")]
	public bool SkipVerification { get; set; }

	#endregion
}

public class DeployInfrastructureCommand(IProcessExecutor processExecutor, ILogger logger, IFileSystem fileSystem,
	Ik8Commands k8Commands, IDbClientFactory dbClientFactory, IInfrastructurePathProvider infrastructurePathProvider)
	: Command<DeployInfrastructureOptions>{

	#region Class: Nested

	private class DeploymentStep(string name, string path){
		#region Properties: Public

		public string Name { get; } = name;
		public string Path { get; } = path;

		#endregion
	}

	#endregion

	internal int CleanupOrphanedPersistentVolumesMaxAttempts = 5;
	internal int PodDelay = 5_000;
	internal int RetryDelay = 1000;
	internal int VerifyPostgresConnectionDelaySeconds = 3;
	internal int VerifyPostgresConnectionMaxRetryAttempts = 40;
	internal int VerifyRedisConnectionConnectionTimeout = 5_000;
	internal int VerifyRedisConnectionDelaySeconds = 3;
	internal int VerifyRedisConnectionMaxRetryAttempts = 10;
	internal int VerifyRedisConnectionSyncTimeout = 5_000;

	#region Methods: Private

	private bool CheckAndHandleExistingNamespace(string namespaceName) {
		logger.WriteInfo("[1/5] Checking for existing namespace...");

		try {
			if (!k8Commands.NamespaceExists(namespaceName)) {
				logger.WriteInfo("✓ No existing namespace found");

				// Always check for orphaned PersistentVolumes
				// They can appear after namespace deletion and prevent new PVC binding
				// Wait a moment for Released PV status to stabilize
				Thread.Sleep(2000);
				logger.WriteInfo("Checking for orphaned PersistentVolumes...");
				CleanupOrphanedPersistentVolumes();

				logger.WriteInfo("Proceeding with deployment");
				return true;
			}

			// Namespace already exists - this is an error
			logger.WriteError($"✗ Namespace '{namespaceName}' already exists");
			logger.WriteLine();
			logger.WriteError("To recreate the infrastructure, first delete it with:");
			logger.WriteError("  clio delete-infrastructure [--force]");
			logger.WriteLine();
			logger.WriteError("Then deploy again:");
			logger.WriteError("  clio deploy-infrastructure");
			logger.WriteLine();

			return false;
		}
		catch (Exception ex) {
			logger.WriteError($"Error checking namespace: {ex.Message}");
			return false;
		}
	}

	private bool CheckKubectlInstalled() {
		logger.WriteInfo("[1/5] Checking kubectl installation...");
		try {
			processExecutor.Execute("kubectl", "version --client --short", true, showOutput: false);
			logger.WriteInfo("✓ kubectl is installed");
			return true;
		}
		catch {
			return false;
		}
	}

	private void CleanupOrphanedPersistentVolumes() {
		try {
			logger.WriteInfo("Checking for orphaned PersistentVolumes...");

			int maxAttempts = CleanupOrphanedPersistentVolumesMaxAttempts;
			int attemptCount = 0;
			HashSet<string> allDeletedPvs = [];

			while (attemptCount < maxAttempts) {
				// Get ALL orphaned PV (not just Released) to handle various states during cleanup
				IList<string> orphanedPvs = k8Commands.GetOrphanedPersistentVolumes("clio-infrastructure");

				if (orphanedPvs.Count == 0) {
					logger.WriteInfo(allDeletedPvs.Count == 0
						? "✓ No orphaned PersistentVolumes found"
						: $"✓ All {allDeletedPvs.Count} orphaned PersistentVolume(s) cleaned up successfully");
					return;
				}

				if (attemptCount == 0) {
					logger.WriteInfo($"Found {orphanedPvs.Count} orphaned PersistentVolume(s), cleaning up...");
				}

				foreach (string pvName in orphanedPvs) {
					if (k8Commands.DeletePersistentVolume(pvName)) {
						logger.WriteInfo($"  ✓ {pvName}");
						allDeletedPvs.Add(pvName);
					}
					else {
						logger.WriteWarning($"  ⚠ Failed to delete {pvName}");
					}
				}

				attemptCount++;
				if (attemptCount < maxAttempts && orphanedPvs.Count > 0) {
					// Wait before retrying
					Thread.Sleep(RetryDelay); // Wait before retrying
				}
			}

			if (allDeletedPvs.Count > 0) {
				logger.WriteInfo($"✓ Cleaned up {allDeletedPvs.Count} orphaned PersistentVolume(s)");
			}
		}
		catch (Exception ex) {
			logger.WriteWarning($"⚠ Error cleaning up orphaned PersistentVolumes: {ex.Message}");
		}
	}

	private bool DeployInfrastructure(string infrastructurePath) {
		logger.WriteInfo("[3/5] Deploying infrastructure to Kubernetes...");
		logger.WriteLine();

		// Before deploying infrastructure, ensure NO orphaned PV exist from previous deployments
		// This is critical for PVC binding to work properly
		logger.WriteInfo("Pre-deployment cleanup: Removing any orphaned PersistentVolumes...");
		CleanupOrphanedPersistentVolumes();
		logger.WriteLine();

		// Define deployment order - order matters for dependencies
		List<DeploymentStep> deploymentSteps = [
			new("Namespace", Path.Join(infrastructurePath, "clio-namespace.yaml")),

			// Step 2: Create a storage class (required by PersistentVolumes)
			new("Storage Class", Path.Join(infrastructurePath, "clio-storage-class.yaml")),

			// Step 3: Deploy Redis - workload contains ConfigMap, then Services
			new("Redis Workload", Path.Join(infrastructurePath, "redis", "redis-workload.yaml")),
			new("Redis Services", Path.Join(infrastructurePath, "redis", "redis-services.yaml")),

			// Step 4: Deploy Postgres SQL - secrets, volumes (ConfigMap), services, then StatefulSet
			new("Postgres SQL Secrets", Path.Join(infrastructurePath, "postgres", "postgres-secrets.yaml")),
			new("Postgres SQL Volumes", Path.Join(infrastructurePath, "postgres", "postgres-volumes.yaml")),
			new("Postgres SQL Services", Path.Join(infrastructurePath, "postgres", "postgres-services.yaml")),
			new("Postgres SQL StatefulSet", Path.Join(infrastructurePath, "postgres", "postgres-stateful-set.yaml")),

			// Step 5: Deploy pgAdmin - secrets, volumes (PVC + ConfigMap), services, then workload
			new("pgAdmin Secrets", Path.Join(infrastructurePath, "pgadmin", "pgadmin-secrets.yaml")),
			new("pgAdmin Volumes", Path.Join(infrastructurePath, "pgadmin", "pgadmin-volumes.yaml")),
			new("pgAdmin Services", Path.Join(infrastructurePath, "pgadmin", "pgadmin-services.yaml")),
			new("pgAdmin Workload", Path.Join(infrastructurePath, "pgadmin", "pgadmin-workload.yaml"))
		];

		int stepNumber = 1;
		foreach (DeploymentStep step in deploymentSteps) {
			if (!DeployStep(step, stepNumber, deploymentSteps.Count)) {
				logger.WriteError($"Deployment failed at step: {step.Name}");
				return false;
			}

			stepNumber++;
		}

		logger.WriteLine();
		logger.WriteInfo("✓ All infrastructure components deployed");
		return true;
	}

	private bool DeployStep(DeploymentStep step, int currentStep, int totalSteps) {
		logger.WriteInfo($"  [{currentStep}/{totalSteps}] Deploying {step.Name}...");

		if (!fileSystem.ExistsFile(step.Path) && !fileSystem.ExistsDirectory(step.Path)) {
			logger.WriteWarning($"  ⚠ Skipping {step.Name} - path not found: {step.Path}");
			return true;
		}

		try {
			string command = $"apply -f \"{step.Path}\"";
			processExecutor.Execute("kubectl", command, waitForExit: true, showOutput: true);
			logger.WriteInfo($"  ✓ {step.Name} deployed successfully");
			return true;
		}
		catch (Exception ex) {
			logger.WriteError($"  ✗ Failed to deploy {step.Name}: {ex.Message}");
			return false;
		}
	}

	private bool GenerateInfrastructureFiles(string infrastructurePath) {
		logger.WriteInfo("[2/5] Generating infrastructure files...");

		try {
			string location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string sourcePath = Path.Join(location, "tpl", "k8", "infrastructure");

			if (!fileSystem.ExistsDirectory(sourcePath)) {
				logger.WriteError($"Template files not found at: {sourcePath}");
				return false;
			}

			CreateInfrastructureOptions options = new() {
				InfrastructurePath = infrastructurePath, PostgresLimitMemory = "4Gi", PostgresLimitCpu = "2",
				PostgresRequestMemory = "2Gi", PostgresRequestCpu = "1", MssqlLimitMemory = "4Gi", MssqlLimitCpu = "2",
				MssqlRequestMemory = "2Gi", MssqlRequestCpu = "1"
			};
			CreateInfrastructureCommand createInfrastructureCommand = new(fileSystem);
			createInfrastructureCommand.Execute(options);

			logger.WriteInfo($"✓ Infrastructure files generated at: {infrastructurePath}");
			return true;
		}
		catch (Exception ex) {
			logger.WriteError($"Failed to generate infrastructure files: {ex.Message}");
			return false;
		}
	}

	private bool VerifyConnections() {
		logger.WriteLine();
		logger.WriteInfo("[4/5] Verifying service connections...");
		logger.WriteInfo("Waiting for services to start (this may take a minute)...");

		// Wait for pods to be ready
		Thread.Sleep(PodDelay); // Initial wait

		bool postgresOk = VerifyPostgresConnection();
		bool redisOk = VerifyRedisConnection();

		logger.WriteLine();
		if (postgresOk && redisOk) {
			logger.WriteInfo("✓ All service connections verified");
			return true;
		}

		logger.WriteError("✗ Some service connections failed");
		return false;
	}

	private bool VerifyPostgresConnection() {
		logger.WriteInfo("  Testing Postgres connection...");

		int maxAttempts = VerifyPostgresConnectionMaxRetryAttempts;
		int delaySeconds = VerifyPostgresConnectionDelaySeconds;

		for (int attempt = 1; attempt <= maxAttempts; attempt++) {
			try {
				k8Commands.ConnectionStringParams connectionParams = k8Commands.GetPostgresConnectionString();

				// Use a silent postgres instance to avoid error logging during connection attempts
				Postgres postgres = dbClientFactory.CreatePostgresSilent(connectionParams.DbPort,
					connectionParams.DbUsername, connectionParams.DbPassword);

				// Try to check if a template exists - this will verify the connection works
				bool exists = postgres.CheckTemplateExists("template0");
				if (exists) {
					logger.WriteInfo($"  ✓ Postgres SQL connection verified (attempt {attempt}/{maxAttempts})");
					return true;
				}
			}
			catch (Exception) {
				// Silently catch exceptions during connection attempts
				if (attempt == maxAttempts) {
					logger.WriteError($"  ✗ Postgres SQL connection failed after {maxAttempts} attempts");
					logger.WriteError("    Please check that Postgres SQL pod is running:");
					logger.WriteError("    kubectl get pods -n clio-infrastructure");
					return false;
				}

				// Only show a friendly progress indicator, not error spam
				if (attempt == 1) {
					logger.WriteInfo("  ⏳ Waiting for Postgres SQL to become available...");
				}
				else if (attempt % 5 == 0) {
					logger.WriteInfo($"  ⏳ Still waiting for Postgres SQL... (attempt {attempt}/{maxAttempts})");
				}

				Thread.Sleep(delaySeconds * 1_000);
			}
		}

		return false;
	}

	private bool VerifyRedisConnection() {
		logger.WriteInfo("  Testing Redis connection...");

		int maxAttempts = VerifyRedisConnectionMaxRetryAttempts;
		int delaySeconds = VerifyRedisConnectionDelaySeconds;
		int connectionTimeout = VerifyRedisConnectionConnectionTimeout;
		int syncTimeout = VerifyRedisConnectionSyncTimeout;

		for (int attempt = 1; attempt <= maxAttempts; attempt++) {
			try {
				k8Commands.ConnectionStringParams connectionParams = k8Commands.GetPostgresConnectionString();

				ConfigurationOptions configurationOptions = new() {
					SyncTimeout = syncTimeout,
					ConnectTimeout = connectionTimeout,
					EndPoints = {
						{ BindingsModule.k8sDns, connectionParams.RedisPort }
					},
					AbortOnConnectFail = false
				};

				// Suppress console output during connection attempts
				TextWriter originalConsoleOut = Console.Out;
				TextWriter originalConsoleError = Console.Error;
				try {
					// Redirect console output to null during connection attempts
					Console.SetOut(TextWriter.Null);
					Console.SetError(TextWriter.Null);

					using ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(configurationOptions);
					IServer server = redis.GetServer(BindingsModule.k8sDns, connectionParams.RedisPort);

					// Simple ping test
					if (server.DatabaseCount >= 0) {
						// Restore console output before logging success
						Console.SetOut(originalConsoleOut);
						Console.SetError(originalConsoleError);
						logger.WriteInfo($"  ✓ Redis connection verified (attempt {attempt}/{maxAttempts})");
						return true;
					}
				}
				finally {
					// Always restore console output
					Console.SetOut(originalConsoleOut);
					Console.SetError(originalConsoleError);
				}
			}
			catch (Exception) {
				// Silently catch exceptions during connection attempts
				if (attempt == maxAttempts) {
					logger.WriteError($"  ✗ Redis connection failed after {maxAttempts} attempts");
					logger.WriteError("    Please check that Redis pod is running:");
					logger.WriteError("    kubectl get pods -n clio-infrastructure");
					return false;
				}

				// Only show a friendly progress indicator, not error spam
				if (attempt == 1) {
					logger.WriteInfo("  ⏳ Waiting for Redis to become available...");
				}
				else if (attempt % 3 == 0) {
					logger.WriteInfo($"  ⏳ Still waiting for Redis... (attempt {attempt}/{maxAttempts})");
				}

				Thread.Sleep(delaySeconds * 1_000);
			}
		}

		return false;
	}

	#endregion

	#region Methods: Public

	public override int Execute(DeployInfrastructureOptions options) {
		try {
			logger.WriteInfo("========================================");
			logger.WriteInfo("  Deploy Kubernetes Infrastructure");
			logger.WriteInfo("========================================");
			logger.WriteLine();

			// Step 1: Check kubectl
			if (!CheckKubectlInstalled()) {
				logger.WriteError("kubectl is not installed or not in PATH");
				logger.WriteInfo("Please install kubectl:");
				logger.WriteInfo("  macOS:   brew install kubectl");
				logger.WriteInfo("  Windows: choco install kubernetes-cli");
				logger.WriteInfo("  Linux:   https://kubernetes.io/docs/tasks/tools/");
				return 1;
			}

			// Step 1.5: Check and handle existing namespace
			const string namespaceName = "clio-infrastructure";
			if (!CheckAndHandleExistingNamespace(namespaceName)) {
				logger.WriteInfo("Infrastructure deployment cancelled by user");
				return 1;
			}

			// Step 2: Generate infrastructure files
			string infrastructurePath = infrastructurePathProvider.GetInfrastructurePath(options.InfrastructurePath);
			if (!GenerateInfrastructureFiles(infrastructurePath)) {
				return 1;
			}

			// Step 3: Deploy infrastructure in order
			if (!DeployInfrastructure(infrastructurePath)) {
				return 1;
			}

			// Step 4: Verify connections (unless skipped)
			if (!options.SkipVerification) {
				if (!VerifyConnections()) {
					logger.WriteWarning("Connection verification failed, but infrastructure may still be starting");
					logger.WriteInfo("You can manually verify with: kubectl get pods -n clio-infrastructure");
					return 1;
				}
			}
			else {
				logger.WriteInfo("Skipping connection verification (--no-verify)");
			}

			logger.WriteLine();
			logger.WriteInfo("========================================");
			logger.WriteInfo("  Infrastructure deployed successfully!");
			logger.WriteInfo("========================================");

			return 0;
		}
		catch (Exception ex) {
			logger.WriteError($"Deployment failed: {ex.Message}");
			logger.WriteError(ex.StackTrace);
			return 1;
		}
	}

	#endregion
}

