using System.Collections.Generic;

namespace Clio.Package;

public interface IPackageBuilder
{
    void Build(IEnumerable<string> packagesNames);

    void Rebuild(IEnumerable<string> packagesNames);
}

public class PackageBuilder : IPackageBuilder
{
    private readonly IApplicationClientFactory _applicationClientFactory;
    private readonly EnvironmentSettings _environmentSettings;
    private readonly ILogger _logger;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;

    public PackageBuilder(
        EnvironmentSettings environmentSettings,
        IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder, ILogger logger)
    {
        environmentSettings.CheckArgumentNull(nameof(environmentSettings));
        applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
        serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
        logger.CheckArgumentNull(nameof(logger));
        _environmentSettings = environmentSettings;
        _applicationClientFactory = applicationClientFactory;
        _serviceUrlBuilder = serviceUrlBuilder;
        _logger = logger;
    }

    public void Build(IEnumerable<string> packagesNames) => Compilation(packagesNames, false);

    public void Rebuild(IEnumerable<string> packagesNames) => Compilation(packagesNames, true);

    private static string CreateRequestData(string packageName) => "{ \"packageName\":\"" + packageName + "\" }";

    private IApplicationClient CreateClient() => _applicationClientFactory.CreateClient(_environmentSettings);

    private string GetSafePackageName(string packageName) =>
        packageName
            .Replace(" ", string.Empty)
            .Replace(",", "\",\"");

    private void Compilation(IEnumerable<string> packagesNames, bool force)
    {
        IApplicationClient applicationClient = CreateClient();
        string compilationName = force ? "rebuild" : "build";
        string fullBuildPackageUrl = _serviceUrlBuilder.Build(
            force
                ? ServiceUrlBuilder.KnownRoute.RebuildPackage
                : ServiceUrlBuilder.KnownRoute.BuildPackage);

        foreach (string packageName in packagesNames)
        {
            string safePackageName = GetSafePackageName(packageName);
            _logger.WriteLine($"Start {compilationName} packages ({safePackageName}).");
            string requestData = CreateRequestData(safePackageName);
            applicationClient.ExecutePostRequest(fullBuildPackageUrl, requestData);
            _logger.WriteLine($"End {compilationName} packages ({safePackageName}).");
        }
    }
}
