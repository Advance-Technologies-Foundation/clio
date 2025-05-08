using System;
using System.Collections.Generic;
using System.Linq;

using Clio.Common;

namespace Clio.Package;

public class ApplicationPackageListProvider : IApplicationPackageListProvider
{
    private readonly IJsonConverter _jsonConverter;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;
    private readonly IApplicationClient _applicationClient;

    public ApplicationPackageListProvider(IApplicationClient applicationClient, IJsonConverter jsonConverter,
        IServiceUrlBuilder serviceUrlBuilder)
    {
        applicationClient.CheckArgumentNull(nameof(applicationClient));
        jsonConverter.CheckArgumentNull(nameof(jsonConverter));
        serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
        _applicationClient = applicationClient;
        _jsonConverter = jsonConverter;
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    public ApplicationPackageListProvider()
    {
    }

    public ApplicationPackageListProvider(IJsonConverter jsonConverter) => _jsonConverter = jsonConverter;

    private string PackagesListServiceUrl => _serviceUrlBuilder.Build("/rest/CreatioApiGateway/GetPackages");

    private PackageInfo CreatePackageInfo(Dictionary<string, string> package)
    {
        PackageDescriptor descriptor = new ()
        {
            Name = package["Name"],
            UId = Guid.Parse(package["UId"]),
            Maintainer = package.ContainsKey("Maintainer") ? package["Maintainer"] : string.Empty,
            PackageVersion = package.ContainsKey("Version") ? package["Version"] : string.Empty
        };
        return new PackageInfo(descriptor, string.Empty, Enumerable.Empty<string>());
    }

    public IEnumerable<PackageInfo> GetPackages() => GetPackages("{}");

    public IEnumerable<PackageInfo> GetPackages(string scriptData)
    {
        try
        {
            string responseFormServer = _applicationClient.ExecutePostRequest(PackagesListServiceUrl, scriptData);
            return ParsePackageInfoResponse(responseFormServer);
        }
        catch (Exception)
        {
            return Array.Empty<PackageInfo>();
        }
    }

    internal IEnumerable<PackageInfo> ParsePackageInfoResponse(string responseData)
    {
        string json = _jsonConverter.CorrectJson(responseData);
        List<Dictionary<string, string>> packages =
            _jsonConverter.DeserializeObject<List<Dictionary<string, string>>>(json);
        return packages.Select(CreatePackageInfo);
    }
}
