using System;
using Clio.Common;
using Clio.Common.K8;
using CommandLine;

namespace Clio.Command;

[Verb("delete-infrastructure", Aliases = ["di-delete", "remove-infrastructure"],
	HelpText = "Delete Kubernetes infrastructure for Creatio (removes namespace and all resources)")]
public class DeleteInfrastructureOptions{
	#region Properties: Public

	[Option("force", Required = false, Default = false,
		HelpText = "Skip confirmation and delete immediately")]
	public bool Force { get; set; }

	#endregion
}

public class DeleteInfrastructureCommand : Command<DeleteInfrastructureOptions>{
	#region Fields: Private

	private readonly Ik8Commands _k8Commands;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public DeleteInfrastructureCommand(
		ILogger logger,
		Ik8Commands k8Commands) {
		_logger = logger;
		_k8Commands = k8Commands;
	}

	#endregion

	#region Methods: Private

	private bool DeleteInfrastructureNamespace(string namespaceName) {
		try {
			_logger.WriteInfo("Step 1: Cleaning up released PersistentVolumes and deleting namespace...");

			CleanupNamespaceResult result = _k8Commands.CleanupAndDeleteNamespace(namespaceName, "clio-infrastructure");

			if (result.DeletedPersistentVolumes.Count > 0) {
				_logger.WriteInfo($"  Found {result.DeletedPersistentVolumes.Count} released PersistentVolume(s)");
				foreach (string pvName in result.DeletedPersistentVolumes) {
					_logger.WriteInfo($"  ✓ PV '{pvName}' deleted");
				}
			}
			else {
				_logger.WriteInfo("  No released PersistentVolumes found");
			}

			if (!result.Success) {
				_logger.WriteError(result.Message);
				return false;
			}

			if (!result.NamespaceFullyDeleted) {
				_logger.WriteWarning($"⚠ {result.Message}");
				_logger.WriteInfo("You can check status with: kubectl get ns clio-infrastructure");
				return true;
			}

			_logger.WriteInfo($"✓ {result.Message}");
			return true;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error deleting namespace: {ex.Message}");
			return false;
		}
	}

	#endregion

	#region Methods: Public

	public override int Execute(DeleteInfrastructureOptions options) {
		try {
			_logger.WriteInfo("========================================");
			_logger.WriteInfo("  Delete Kubernetes Infrastructure");
			_logger.WriteInfo("========================================");
			_logger.WriteLine();

			const string namespaceName = "clio-infrastructure";

			// Check if namespace exists
			if (!_k8Commands.NamespaceExists(namespaceName)) {
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
			if (!options.Force) {
				_logger.WriteInfo("Are you sure you want to delete the infrastructure? (y/n)");
				string answer = Console.ReadLine();

				if (string.IsNullOrWhiteSpace(answer) ||
					!answer.StartsWith("y", StringComparison.CurrentCultureIgnoreCase)) {
					_logger.WriteInfo("Infrastructure deletion cancelled");
					return 0;
				}
			}
			else {
				_logger.WriteInfo("Deleting infrastructure (--force flag)...");
			}

			_logger.WriteLine();
			_logger.WriteInfo("Deleting namespace and all resources...");

			if (!DeleteInfrastructureNamespace(namespaceName)) {
				return 1;
			}

			_logger.WriteLine();
			_logger.WriteInfo("========================================");
			_logger.WriteInfo("  Infrastructure deleted successfully!");
			_logger.WriteInfo("========================================");

			return 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Deletion failed: {ex.Message}");
			_logger.WriteError(ex.StackTrace);
			return 1;
		}
	}

	#endregion
}
