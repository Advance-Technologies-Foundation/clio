using System;
using System.Threading.Tasks;

namespace Clio.Common.SystemServices;

/// <summary>
/// Stub implementation of ISystemServiceManager for Windows.
/// Windows service management is handled differently through IIS,
/// so this implementation provides minimal functionality.
/// </summary>
public class WindowsSystemServiceManager : ISystemServiceManager
{
	/// <summary>
	/// Not implemented for Windows - service management handled through IIS.
	/// </summary>
	public async Task<bool> CreateOrUpdateService(
		string serviceName,
		string description,
		string workingDirectory,
		string executablePath,
		string arguments = "",
		bool autoStart = true
	)
	{
		await Task.CompletedTask;
		return true;
	}

	/// <summary>
	/// Not implemented for Windows.
	/// </summary>
	public async Task<bool> EnableService(string serviceName)
	{
		await Task.CompletedTask;
		return true;
	}

	/// <summary>
	/// Not implemented for Windows.
	/// </summary>
	public async Task<bool> DisableService(string serviceName)
	{
		await Task.CompletedTask;
		return true;
	}

	/// <summary>
	/// Not implemented for Windows.
	/// </summary>
	public async Task<bool> StartService(string serviceName)
	{
		await Task.CompletedTask;
		return true;
	}

	/// <summary>
	/// Not implemented for Windows.
	/// </summary>
	public async Task<bool> StopService(string serviceName)
	{
		await Task.CompletedTask;
		return true;
	}

	/// <summary>
	/// Not implemented for Windows.
	/// </summary>
	public async Task<bool> RestartService(string serviceName)
	{
		await Task.CompletedTask;
		return true;
	}

	/// <summary>
	/// Not implemented for Windows.
	/// </summary>
	public async Task<bool> IsServiceRunning(string serviceName)
	{
		await Task.CompletedTask;
		return true;
	}

	/// <summary>
	/// Not implemented for Windows.
	/// </summary>
	public async Task<bool> DeleteService(string serviceName)
	{
		await Task.CompletedTask;
		return true;
	}
}
