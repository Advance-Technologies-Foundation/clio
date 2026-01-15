using System.Collections.Generic;
using System.Threading.Tasks;

namespace Clio.Common.IIS;

/// <summary>
/// Provides information about IIS sites and application pools.
/// </summary>
public interface IIISSiteDetector
{
	/// <summary>
	/// Gets information about all IIS sites that match the specified environment path.
	/// </summary>
	/// <param name="environmentPath">Physical path to match against IIS sites</param>
	/// <returns>List of IIS site information</returns>
	Task<List<IISSiteInfo>> GetSitesByPath(string environmentPath);
	
	/// <summary>
	/// Checks if an IIS site is running (both site and app pool must be started).
	/// </summary>
	/// <param name="siteName">Name of the IIS site</param>
	/// <returns>True if site and app pool are running; false otherwise</returns>
	Task<bool> IsSiteRunning(string siteName);
	
	/// <summary>
	/// Gets the process ID (PID) of the w3wp.exe worker process for the specified site.
	/// </summary>
	/// <param name="siteName">Name of the IIS site</param>
	/// <returns>Process ID if found; null otherwise</returns>
	Task<int?> GetSiteProcessId(string siteName);
}

/// <summary>
/// Contains information about an IIS site.
/// </summary>
public class IISSiteInfo
{
	public string SiteName { get; set; }
	public string PhysicalPath { get; set; }
	public string State { get; set; }
	public string AppPoolName { get; set; }
	public string AppPoolState { get; set; }
	public bool IsRunning => State == "Started" && AppPoolState == "Started";
}
