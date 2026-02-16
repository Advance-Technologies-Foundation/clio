using System.Threading.Tasks;

namespace Clio.Common.IIS;

public class StubIISAppPoolManager : IIISAppPoolManager
{
	public Task<string> GetAppPoolState(string appPoolName)
	{
		return Task.FromResult("NotAvailable");
	}

	public Task<bool> IsAppPoolRunning(string appPoolName)
	{
		return Task.FromResult(false);
	}

	public Task<bool> StartAppPool(string appPoolName)
	{
		return Task.FromResult(false);
	}

	public Task<bool> StopAppPool(string appPoolName)
	{
		return Task.FromResult(false);
	}

	public Task<bool> StartSite(string siteName)
	{
		return Task.FromResult(false);
	}

	public Task<bool> IsSiteRunning(string siteName)
	{
		return Task.FromResult(false);
	}
}

