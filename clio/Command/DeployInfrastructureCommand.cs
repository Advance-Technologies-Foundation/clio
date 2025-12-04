using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.K8;
using CommandLine;
using NRedisStack;
using StackExchange.Redis;

namespace Clio.Command
{
	[Verb("deploy-infrastructure", Aliases = new[] { "di" }, 
		HelpText = "Deploy Kubernetes infrastructure for Creatio (namespace, storage, redis, postgres, pgadmin)")]
	public class DeployInfrastructureOptions
	{
		[Option('p', "path", Required = false, 
			HelpText = "Path to infrastructure files (default: auto-detected from clio settings)")]
		public string InfrastructurePath { get; set; }
		
		[Option("no-verify", Required = false, Default = false,
			HelpText = "Skip connection verification after deployment")]
		public bool SkipVerification { get; set; }

		[Option("force", Required = false, Default = false,
			HelpText = "Force recreation of namespace without prompting if it already exists")]
		public bool Force { get; set; }
	}

	public class DeployInfrastructureCommand : Command<DeployInfrastructureOptions>
	{
		private readonly IProcessExecutor _processExecutor;
		private readonly ILogger _logger;
		private readonly Clio.Common.IFileSystem _fileSystem;
		private readonly Ik8Commands _k8Commands;
		private readonly IDbClientFactory _dbClientFactory;

		public DeployInfrastructureCommand(
			IProcessExecutor processExecutor, 
			ILogger logger, 
			Clio.Common.IFileSystem fileSystem,
			Ik8Commands k8Commands,
			IDbClientFactory dbClientFactory)
		{
			_processExecutor = processExecutor;
			_logger = logger;
			_fileSystem = fileSystem;
			_k8Commands = k8Commands;
			_dbClientFactory = dbClientFactory;
		}

		public override int Execute(DeployInfrastructureOptions options)
		{
			try
			{
				_logger.WriteInfo("========================================");
				_logger.WriteInfo("  Deploy Kubernetes Infrastructure");
				_logger.WriteInfo("========================================");
				_logger.WriteLine();

				// Step 1: Check kubectl
				if (!CheckKubectlInstalled())
				{
					_logger.WriteError("kubectl is not installed or not in PATH");
					_logger.WriteInfo("Please install kubectl:");
					_logger.WriteInfo("  macOS:   brew install kubectl");
					_logger.WriteInfo("  Windows: choco install kubernetes-cli");
					_logger.WriteInfo("  Linux:   https://kubernetes.io/docs/tasks/tools/");
					return 1;
				}

				// Step 1.5: Check and handle existing namespace
				const string namespaceName = "clio-infrastructure";
				if (!CheckAndHandleExistingNamespace(namespaceName, options.Force))
				{
					_logger.WriteInfo("Infrastructure deployment cancelled by user");
					return 1;
				}

				// Step 2: Generate infrastructure files
				string infrastructurePath = GetInfrastructurePath(options);
				if (!GenerateInfrastructureFiles(infrastructurePath))
				{
					return 1;
				}

				// Step 3: Deploy infrastructure in order
				if (!DeployInfrastructure(infrastructurePath))
				{
					return 1;
				}

				// Step 4: Verify connections (unless skipped)
				if (!options.SkipVerification)
				{
					if (!VerifyConnections())
					{
						_logger.WriteWarning("Connection verification failed, but infrastructure may still be starting");
						_logger.WriteInfo("You can manually verify with: kubectl get pods -n clio-infrastructure");
						return 1;
					}
				}
				else
				{
					_logger.WriteInfo("Skipping connection verification (--no-verify)");
				}

				_logger.WriteLine();
				_logger.WriteInfo("========================================");
				_logger.WriteInfo("  Infrastructure deployed successfully!");
				_logger.WriteInfo("========================================");
				
				return 0;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Deployment failed: {ex.Message}");
				_logger.WriteError(ex.StackTrace);
				return 1;
			}
		}

		private bool CheckAndHandleExistingNamespace(string namespaceName, bool forceRecreate)
		{
			_logger.WriteInfo("[1/5] Checking for existing namespace...");
			
			try
			{
				if (!_k8Commands.NamespaceExists(namespaceName))
				{
					_logger.WriteInfo("✓ No existing namespace found");
					
					// Always check for orphaned PersistentVolumes regardless of --force flag
					// They can appear after namespace deletion and prevent new PVC binding
					// Wait a moment for Released PV status to stabilize
					System.Threading.Thread.Sleep(2000);
					_logger.WriteInfo("Checking for orphaned PersistentVolumes...");
					CleanupOrphanedPersistentVolumes();
					
					_logger.WriteInfo("Proceeding with deployment");
					return true;
				}

				_logger.WriteWarning($"⚠ Namespace '{namespaceName}' already exists");
				_logger.WriteLine();

				if (forceRecreate)
				{
					_logger.WriteInfo("Deleting existing namespace (--force flag)...");
					if (!DeleteExistingNamespace(namespaceName))
					{
						_logger.WriteError("Failed to delete existing namespace");
						return false;
					}
					_logger.WriteInfo("✓ Namespace deleted successfully");
					
					// After namespace deletion, clean up any Released PersistentVolumes
					// They can prevent new PVC binding
					_logger.WriteInfo("Checking for orphaned PersistentVolumes after namespace deletion...");
					CleanupOrphanedPersistentVolumes();
					
					return true;
				}

				// Ask user for confirmation
				_logger.WriteInfo("Do you want to recreate it? (y/n)");
				string answer = Console.ReadLine();

				if (string.IsNullOrWhiteSpace(answer) || !answer.StartsWith("y", StringComparison.CurrentCultureIgnoreCase))
				{
					_logger.WriteInfo("User declined namespace recreation");
					return false;
				}

				if (!DeleteExistingNamespace(namespaceName))
				{
					_logger.WriteError("Failed to delete existing namespace");
					return false;
				}

				_logger.WriteInfo("✓ Namespace deleted successfully");
				
				// After namespace deletion, clean up any Released PersistentVolumes
				// They can prevent new PVC binding
				_logger.WriteInfo("Checking for orphaned PersistentVolumes after namespace deletion...");
				CleanupOrphanedPersistentVolumes();
				
				return true;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Error checking namespace: {ex.Message}");
				return false;
			}
		}

		private void CleanupOrphanedPersistentVolumes()
		{
			try
			{
				_logger.WriteInfo("Checking for orphaned PersistentVolumes...");
				
				var releasedPvs = _k8Commands.GetReleasedPersistentVolumes("clio-infrastructure");
				
				if (releasedPvs.Count == 0)
				{
					_logger.WriteInfo("✓ No orphaned PersistentVolumes found");
					return;
				}
				
				_logger.WriteInfo($"Found {releasedPvs.Count} orphaned PersistentVolume(s), cleaning up...");
				
				foreach (var pvName in releasedPvs)
				{
					if (_k8Commands.DeletePersistentVolume(pvName))
					{
						_logger.WriteInfo($"  ✓ {pvName}");
					}
					else
					{
						_logger.WriteWarning($"  ⚠ Failed to delete {pvName}");
					}
				}
			}
			catch (Exception ex)
			{
				_logger.WriteWarning($"⚠ Error cleaning up orphaned PersistentVolumes: {ex.Message}");
			}
		}

		private bool DeleteExistingNamespace(string namespaceName)
		{
			_logger.WriteInfo($"Deleting namespace '{namespaceName}' and all its contents...");
			
			try
			{
				var result = _k8Commands.CleanupAndDeleteNamespace(namespaceName, "clio-infrastructure");

				if (result.DeletedPersistentVolumes.Count > 0)
				{
					_logger.WriteInfo("  Cleaned up released PersistentVolumes:");
					foreach (var pvName in result.DeletedPersistentVolumes)
					{
						_logger.WriteInfo($"    ✓ {pvName}");
					}
				}

				if (!result.Success)
				{
					_logger.WriteError(result.Message);
					return false;
				}

				if (!result.NamespaceFullyDeleted)
				{
					_logger.WriteWarning($"⚠ {result.Message}");
					System.Threading.Thread.Sleep(5000); // Give it extra time
				}
				else
				{
					_logger.WriteInfo($"✓ {result.Message}");
				}

				return true;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Error deleting namespace: {ex.Message}");
				return false;
			}
		}

		private bool CheckKubectlInstalled()
		{
			_logger.WriteInfo("[1/5] Checking kubectl installation...");
			try
			{
				string result = _processExecutor.Execute("kubectl", "version --client --short", 
					waitForExit: true, showOutput: false);
				_logger.WriteInfo("✓ kubectl is installed");
				return true;
			}
			catch
			{
				return false;
			}
		}

		private string GetInfrastructurePath(DeployInfrastructureOptions options)
		{
			if (!string.IsNullOrWhiteSpace(options.InfrastructurePath))
			{
				return options.InfrastructurePath;
			}
			
			return Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");
		}

		private bool GenerateInfrastructureFiles(string infrastructurePath)
		{
			_logger.WriteInfo("[2/5] Generating infrastructure files...");
			
			try
			{
				string location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				string sourcePath = Path.Join(location, "tpl", "k8", "infrastructure");
				
				if (!_fileSystem.ExistsDirectory(sourcePath))
				{
					_logger.WriteError($"Template files not found at: {sourcePath}");
					return false;
				}

				_fileSystem.CopyDirectory(sourcePath, infrastructurePath, overwrite: true);
				_logger.WriteInfo($"✓ Infrastructure files generated at: {infrastructurePath}");
				return true;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Failed to generate infrastructure files: {ex.Message}");
				return false;
			}
		}

		private bool DeployInfrastructure(string infrastructurePath)
		{
			_logger.WriteInfo("[3/5] Deploying infrastructure to Kubernetes...");
			_logger.WriteLine();

			// Define deployment order - order matters for dependencies
			var deploymentSteps = new List<DeploymentStep>
			{
				// Step 1: Create namespace first (required by all other resources)
				new("Namespace", Path.Join(infrastructurePath, "clio-namespace.yaml")),
				
				// Step 2: Create storage class (required by PersistentVolumes)
				new("Storage Class", Path.Join(infrastructurePath, "clio-storage-class.yaml")),
				
				// Step 3: Deploy Redis - workload contains ConfigMap, then Services
				new("Redis Workload", Path.Join(infrastructurePath, "redis", "redis-workload.yaml")),
				new("Redis Services", Path.Join(infrastructurePath, "redis", "redis-services.yaml")),
				
				// Step 4: Deploy PostgreSQL - secrets, volumes (ConfigMap), services, then StatefulSet
				new("PostgreSQL Secrets", Path.Join(infrastructurePath, "postgres", "postgres-secrets.yaml")),
				new("PostgreSQL Volumes", Path.Join(infrastructurePath, "postgres", "postgres-volumes.yaml")),
				new("PostgreSQL Services", Path.Join(infrastructurePath, "postgres", "postgres-services.yaml")),
				new("PostgreSQL StatefulSet", Path.Join(infrastructurePath, "postgres", "postgres-stateful-set.yaml")),
				
				// Step 5: Deploy pgAdmin - secrets, volumes (PVC + ConfigMap), services, then workload
				new("pgAdmin Secrets", Path.Join(infrastructurePath, "pgadmin", "pgadmin-secrets.yaml")),
				new("pgAdmin Volumes", Path.Join(infrastructurePath, "pgadmin", "pgadmin-volumes.yaml")),
				new("pgAdmin Services", Path.Join(infrastructurePath, "pgadmin", "pgadmin-services.yaml")),
				new("pgAdmin Workload", Path.Join(infrastructurePath, "pgadmin", "pgadmin-workload.yaml"))
			};

			int stepNumber = 1;
			foreach (var step in deploymentSteps)
			{
				if (!DeployStep(step, stepNumber, deploymentSteps.Count))
				{
					_logger.WriteError($"Deployment failed at step: {step.Name}");
					return false;
				}
				stepNumber++;
			}

			_logger.WriteLine();
			_logger.WriteInfo("✓ All infrastructure components deployed");
			return true;
		}

		private bool DeployStep(DeploymentStep step, int currentStep, int totalSteps)
		{
			_logger.WriteInfo($"  [{currentStep}/{totalSteps}] Deploying {step.Name}...");

			if (!_fileSystem.ExistsFile(step.Path) && !_fileSystem.ExistsDirectory(step.Path))
			{
				_logger.WriteWarning($"  ⚠ Skipping {step.Name} - path not found: {step.Path}");
				return true;
			}

			try
			{
				string command = $"apply -f \"{step.Path}\"";
				string result = _processExecutor.Execute("kubectl", command, 
					waitForExit: true, showOutput: true);
				
				_logger.WriteInfo($"  ✓ {step.Name} deployed successfully");
				return true;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"  ✗ Failed to deploy {step.Name}: {ex.Message}");
				return false;
			}
		}

		private bool VerifyConnections()
		{
			_logger.WriteLine();
			_logger.WriteInfo("[4/5] Verifying service connections...");
			_logger.WriteInfo("Waiting for services to start (this may take a minute)...");
			
			// Wait for pods to be ready
			System.Threading.Thread.Sleep(5000); // Initial wait

			bool postgresOk = VerifyPostgresConnection();
			bool redisOk = VerifyRedisConnection();

			_logger.WriteLine();
			if (postgresOk && redisOk)
			{
				_logger.WriteInfo("✓ All service connections verified");
				return true;
			}
			else
			{
				_logger.WriteError("✗ Some service connections failed");
				return false;
			}
		}

		private bool VerifyPostgresConnection()
		{
			_logger.WriteInfo("  Testing PostgreSQL connection...");
			
			const int maxAttempts = 40;
			const int delaySeconds = 3;

			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				   try
				   {
					   k8Commands.ConnectionStringParams connectionParams = _k8Commands.GetPostgresConnectionString();
					   // Use silent postgres instance to avoid error logging during connection attempts
					   Postgres postgres = _dbClientFactory.CreatePostgresSilent(
						   connectionParams.DbPort, 
						   connectionParams.DbUsername, 
						   connectionParams.DbPassword);

					   // Try to check if template exists - this will verify connection works
					   bool exists = postgres.CheckTemplateExists("template0");
					   if (exists)
					   {
						   _logger.WriteInfo($"  ✓ PostgreSQL connection verified (attempt {attempt}/{maxAttempts})");
						   return true;
					   }
				   }
				   catch (Exception)
				   {
					   // Silently catch exceptions during connection attempts
					   if (attempt == maxAttempts)
					   {
						   _logger.WriteError($"  ✗ PostgreSQL connection failed after {maxAttempts} attempts");
						   _logger.WriteError($"    Please check that PostgreSQL pod is running:");
						   _logger.WriteError($"    kubectl get pods -n clio-infrastructure");
						   return false;
					   }
					   // Only show a friendly progress indicator, not error spam
					   if (attempt == 1)
					   {
						   _logger.WriteInfo($"  ⏳ Waiting for PostgreSQL to become available...");
					   }
					   else if (attempt % 5 == 0)
					   {
						   _logger.WriteInfo($"  ⏳ Still waiting for PostgreSQL... (attempt {attempt}/{maxAttempts})");
					   }
					   Thread.Sleep(delaySeconds * 1000);
				   }
			}

			return false;
		}

		private bool VerifyRedisConnection()
		{
			_logger.WriteInfo("  Testing Redis connection...");
			
			const int maxAttempts = 10;
			const int delaySeconds = 3;

			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				   try
				   {
					   k8Commands.ConnectionStringParams connectionParams = _k8Commands.GetPostgresConnectionString();
               
					   ConfigurationOptions configurationOptions = new ConfigurationOptions()
					   {
						   SyncTimeout = 5000,
						   ConnectTimeout = 5000,
						   EndPoints = { { BindingsModule.k8sDns, connectionParams.RedisPort } },
						   AbortOnConnectFail = false
					   };

					   // Suppress console output during connection attempts
					   var originalConsoleOut = Console.Out;
					   var originalConsoleError = Console.Error;
					   try
					   {
						   // Redirect console output to null during connection attempts
						   Console.SetOut(System.IO.TextWriter.Null);
						   Console.SetError(System.IO.TextWriter.Null);
						   
						   using ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(configurationOptions);
						   IServer server = redis.GetServer(BindingsModule.k8sDns, connectionParams.RedisPort);
						   
						   // Simple ping test
						   int dbCount = server.DatabaseCount;
						   if (dbCount >= 0)
						   {
							   // Restore console output before logging success
							   Console.SetOut(originalConsoleOut);
							   Console.SetError(originalConsoleError);
							   _logger.WriteInfo($"  ✓ Redis connection verified (attempt {attempt}/{maxAttempts})");
							   return true;
						   }
					   }
					   finally
					   {
						   // Always restore console output
						   Console.SetOut(originalConsoleOut);
						   Console.SetError(originalConsoleError);
					   }
				   }
				   catch (Exception)
				   {
					   // Silently catch exceptions during connection attempts
					   if (attempt == maxAttempts)
					   {
						   _logger.WriteError($"  ✗ Redis connection failed after {maxAttempts} attempts");
						   _logger.WriteError($"    Please check that Redis pod is running:");
						   _logger.WriteError($"    kubectl get pods -n clio-infrastructure");
						   return false;
					   }
					   // Only show a friendly progress indicator, not error spam
					   if (attempt == 1)
					   {
						   _logger.WriteInfo($"  ⏳ Waiting for Redis to become available...");
					   }
					   else if (attempt % 3 == 0)
					   {
						   _logger.WriteInfo($"  ⏳ Still waiting for Redis... (attempt {attempt}/{maxAttempts})");
					   }
					   Thread.Sleep(delaySeconds * 1000);
				   }
			}

			return false;
		}

		private class DeploymentStep
		{
			public string Name { get; }
			public string Path { get; }

			public DeploymentStep(string name, string path)
			{
				Name = name;
				Path = path;
			}
		}
	}

	[Verb("delete-infrastructure", Aliases = new[] { "di-delete", "remove-infrastructure" },
		HelpText = "Delete Kubernetes infrastructure for Creatio (removes namespace and all resources)")]
	public class DeleteInfrastructureOptions
	{
		[Option("force", Required = false, Default = false,
			HelpText = "Skip confirmation and delete immediately")]
		public bool Force { get; set; }
	}

	public class DeleteInfrastructureCommand : Command<DeleteInfrastructureOptions>
	{
		private readonly ILogger _logger;
		private readonly Ik8Commands _k8Commands;

		public DeleteInfrastructureCommand(
			ILogger logger,
			Ik8Commands k8Commands)
		{
			_logger = logger;
			_k8Commands = k8Commands;
		}

		public override int Execute(DeleteInfrastructureOptions options)
		{
			try
			{
				_logger.WriteInfo("========================================");
				_logger.WriteInfo("  Delete Kubernetes Infrastructure");
				_logger.WriteInfo("========================================");
				_logger.WriteLine();

				const string namespaceName = "clio-infrastructure";

				// Check if namespace exists
				if (!_k8Commands.NamespaceExists(namespaceName))
				{
					_logger.WriteInfo($"Namespace '{namespaceName}' does not exist");
					_logger.WriteInfo("Nothing to delete");
					return 0;
				}

				_logger.WriteWarning($"⚠ This will delete the '{namespaceName}' namespace and all its contents:");
				_logger.WriteWarning("  - All pods and deployments");
				_logger.WriteWarning("  - All services and volumes");
				_logger.WriteWarning("  - All persistent volume claims");
				_logger.WriteWarning("  - All configuration and secrets");
				_logger.WriteLine();

				// Ask for confirmation unless --force is used
				if (!options.Force)
				{
					_logger.WriteInfo("Are you sure you want to delete the infrastructure? (y/n)");
					string answer = Console.ReadLine();

					if (string.IsNullOrWhiteSpace(answer) || !answer.StartsWith("y", StringComparison.CurrentCultureIgnoreCase))
					{
						_logger.WriteInfo("Infrastructure deletion cancelled");
						return 0;
					}
				}
				else
				{
					_logger.WriteInfo("Deleting infrastructure (--force flag)...");
				}

				_logger.WriteLine();
				_logger.WriteInfo("Deleting namespace and all resources...");

				if (!DeleteInfrastructureNamespace(namespaceName))
				{
					return 1;
				}

				_logger.WriteLine();
				_logger.WriteInfo("========================================");
				_logger.WriteInfo("  Infrastructure deleted successfully!");
				_logger.WriteInfo("========================================");

				return 0;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Deletion failed: {ex.Message}");
				_logger.WriteError(ex.StackTrace);
				return 1;
			}
		}

		private bool DeleteInfrastructureNamespace(string namespaceName)
		{
			try
			{
				_logger.WriteInfo("Step 1: Cleaning up released PersistentVolumes and deleting namespace...");

				var result = _k8Commands.CleanupAndDeleteNamespace(namespaceName, "clio-infrastructure");

				if (result.DeletedPersistentVolumes.Count > 0)
				{
					_logger.WriteInfo($"  Found {result.DeletedPersistentVolumes.Count} released PersistentVolume(s)");
					foreach (var pvName in result.DeletedPersistentVolumes)
					{
						_logger.WriteInfo($"  ✓ PV '{pvName}' deleted");
					}
				}
				else
				{
					_logger.WriteInfo("  No released PersistentVolumes found");
				}

				if (!result.Success)
				{
					_logger.WriteError(result.Message);
					return false;
				}

				if (!result.NamespaceFullyDeleted)
				{
					_logger.WriteWarning($"⚠ {result.Message}");
					_logger.WriteInfo("You can check status with: kubectl get ns clio-infrastructure");
					return true;
				}

				_logger.WriteInfo($"✓ {result.Message}");
				return true;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Error deleting namespace: {ex.Message}");
				return false;
			}
		}
	}
}
