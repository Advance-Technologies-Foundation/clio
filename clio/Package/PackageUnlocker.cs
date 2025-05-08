using System.Collections.Generic;
using System.Linq;

using Common;

namespace Clio.Package;

public interface IPackageLockManager
{
    void Unlock();

    void Lock();

    void Unlock(IEnumerable<string> packages);

    void Lock(IEnumerable<string> packages);
}

public class PackageLockManager : IPackageLockManager
{
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IApplicationClientFactory _applicationClientFactory;

    public PackageLockManager(
        EnvironmentSettings environmentSettings,
        IApplicationClientFactory applicationClientFactory)
    {
        environmentSettings.CheckArgumentNull(nameof(environmentSettings));
        applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
        _environmentSettings = environmentSettings;
        _applicationClientFactory = applicationClientFactory;
    }

    private IApplicationClient CreateApplicationClient() =>
        _applicationClientFactory.CreateClient(_environmentSettings);

    private string GetRequestData(string argumentName, IEnumerable<string> packages) =>
        "{\"" + argumentName + "\":[" + string.Join(",", packages.Select(pkg => $"\"{pkg.Trim()}\"")) + "]}";

    public void Unlock(IEnumerable<string> packages)
    {
        IApplicationClient applicationClient = CreateApplicationClient();
        string requestData = GetRequestData("unlockPackages", packages);
        applicationClient.CallConfigurationService("CreatioApiGateway", "UnlockPackages", requestData);
    }

    public void Unlock() => Unlock(Enumerable.Empty<string>());

    public void Lock(IEnumerable<string> packages)
    {
        IApplicationClient applicationClient = CreateApplicationClient();
        string requestData = GetRequestData("lockPackages", packages);
        applicationClient.CallConfigurationService("CreatioApiGateway", "LockPackages", requestData);
    }

    public void Lock() => Lock(Enumerable.Empty<string>());
}
