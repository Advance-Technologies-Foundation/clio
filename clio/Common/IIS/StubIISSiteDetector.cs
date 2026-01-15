using System.Collections.Generic;
using System.Threading.Tasks;

namespace Clio.Common.IIS;

/// <summary>
/// Stub implementation of IIS site detector for non-Windows platforms.
/// IIS is Windows-specific, so this always returns empty results.
/// </summary>
public class StubIISSiteDetector : IIISSiteDetector
{
	public async Task<List<IISSiteInfo>> GetSitesByPath(string environmentPath)
	{
		await Task.CompletedTask;
		return new List<IISSiteInfo>();
	}

	public async Task<bool> IsSiteRunning(string siteName)
	{
		await Task.CompletedTask;
		return false;
	}

	public async Task<int?> GetSiteProcessId(string siteName)
	{
		await Task.CompletedTask;
		return null;
	}
}
