using System;
using System.IO;
using System.Threading.Tasks;
using Clio.Command.CreatioInstallCommand;

namespace Clio.Common.DeploymentStrategies;

/// <summary>
/// Defines the contract for deployment strategy implementations.
/// Each strategy represents a different way to deploy Creatio application on a specific platform.
/// </summary>
public interface IDeploymentStrategy
{
	/// <summary>
	/// Gets the platform this strategy targets.
	/// </summary>
	DeploymentPlatform TargetPlatform { get; }

	/// <summary>
	/// Determines whether this strategy can be applied on the current system.
	/// Checks OS and required dependencies (IIS, dotnet runtime, etc).
	/// </summary>
	/// <returns>True if strategy is applicable; false otherwise</returns>
	bool CanDeploy();

	/// <summary>
	/// Deploys the Creatio application using this strategy.
	/// </summary>
	/// <param name="appDirectory">Directory containing extracted application files</param>
	/// <param name="options">Deployment options from command line</param>
	/// <returns>Exit code: 0 on success, non-zero on failure</returns>
	Task<int> Deploy(DirectoryInfo appDirectory, PfInstallerOptions options);

	/// <summary>
	/// Gets the base URL where the deployed application will be accessible.
	/// </summary>
	/// <param name="options">Deployment options (contains host, port, https flag)</param>
	/// <returns>Application URL (e.g., http://localhost:40000)</returns>
	string GetApplicationUrl(PfInstallerOptions options);

	/// <summary>
	/// Gets a human-readable description of the deployment strategy.
	/// </summary>
	/// <returns>Description (e.g., "Windows IIS" or "macOS dotnet runner")</returns>
	string GetDescription();
}
