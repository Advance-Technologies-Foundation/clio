using System.Threading.Tasks;

namespace Clio.Common.SystemServices;

/// <summary>
/// Defines the contract for managing operating system services.
/// Implementations provide platform-specific service management (systemd, launchd, etc).
/// </summary>
public interface ISystemServiceManager
{
	/// <summary>
	/// Creates or updates a system service configuration.
	/// </summary>
	/// <param name="serviceName">Name of the service (e.g., "creatio-myapp")</param>
	/// <param name="description">Service description for system display</param>
	/// <param name="workingDirectory">Directory where service process runs</param>
	/// <param name="executablePath">Path to executable or script to run</param>
	/// <param name="arguments">Command-line arguments for executable</param>
	/// <param name="autoStart">Whether service should start on system boot</param>
	/// <returns>True if service was created/updated successfully; false otherwise</returns>
	Task<bool> CreateOrUpdateService(
		string serviceName,
		string description,
		string workingDirectory,
		string executablePath,
		string arguments = "",
		bool autoStart = true
	);

	/// <summary>
	/// Enables service to automatically start on system boot.
	/// </summary>
	/// <param name="serviceName">Service name</param>
	/// <returns>True if operation succeeded; false otherwise</returns>
	Task<bool> EnableService(string serviceName);

	/// <summary>
	/// Disables service from automatically starting on system boot.
	/// </summary>
	/// <param name="serviceName">Service name</param>
	/// <returns>True if operation succeeded; false otherwise</returns>
	Task<bool> DisableService(string serviceName);

	/// <summary>
	/// Starts the service immediately.
	/// </summary>
	/// <param name="serviceName">Service name</param>
	/// <returns>True if operation succeeded; false otherwise</returns>
	Task<bool> StartService(string serviceName);

	/// <summary>
	/// Stops the running service.
	/// </summary>
	/// <param name="serviceName">Service name</param>
	/// <returns>True if operation succeeded; false otherwise</returns>
	Task<bool> StopService(string serviceName);

	/// <summary>
	/// Restarts the service.
	/// </summary>
	/// <param name="serviceName">Service name</param>
	/// <returns>True if operation succeeded; false otherwise</returns>
	Task<bool> RestartService(string serviceName);

	/// <summary>
	/// Checks if service is currently running.
	/// </summary>
	/// <param name="serviceName">Service name</param>
	/// <returns>True if service is running; false otherwise</returns>
	Task<bool> IsServiceRunning(string serviceName);

	/// <summary>
	/// Removes service configuration from system.
	/// </summary>
	/// <param name="serviceName">Service name</param>
	/// <returns>True if operation succeeded; false otherwise</returns>
	Task<bool> DeleteService(string serviceName);
}
