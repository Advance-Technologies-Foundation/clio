using System.Threading.Tasks;

namespace Clio.Common.IIS;

public interface IIISAppPoolManager
{
	Task<bool> StartAppPool(string appPoolName);
	Task<bool> StopAppPool(string appPoolName);
	Task<bool> IsAppPoolRunning(string appPoolName);
	Task<string> GetAppPoolState(string appPoolName);
	Task<bool> StartSite(string siteName);
	Task<bool> IsSiteRunning(string siteName);
}

