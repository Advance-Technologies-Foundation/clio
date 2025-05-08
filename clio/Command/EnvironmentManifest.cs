using CreatioModel;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Collections.Generic;
using Clio.CreatioModel;
using YamlDotNet.Serialization;
using System.Linq;

namespace Clio.Command;

public class EnvironmentManifest
{
    [YamlMember(Alias = "apps")] public List<SysInstalledApp> Applications { get; set; }

    [YamlMember(Alias = "app_hubs")] public List<AppHubInfo> AppHubs { get; set; }

    [YamlMember(Alias = "environment")] public EnvironmentSettings EnvironmentSettings { get; internal set; }

    private List<Feature> _features = new();

    [YamlMember(Alias = "features")]
    public List<Feature> Features
    {
        get => _features;
        set => _features = value ?? new List<Feature>();
    }

    private List<CreatioManifestSetting> _settings = new();

    [YamlMember(Alias = "settings")]
    public List<CreatioManifestSetting> Settings
    {
        get => _settings;
        set => _settings = value ?? new List<CreatioManifestSetting>();
    }

    private List<CreatioManifestWebService> _webServices = new();

    [YamlMember(Alias = "webservices")]
    public List<CreatioManifestWebService> WebServices
    {
        get => _webServices;
        set => _webServices = value ?? new List<CreatioManifestWebService>();
    }


    private List<CreatioManifestPackage> _packages = new();

    [YamlMember(Alias = "packages")]
    public List<CreatioManifestPackage> Packages
    {
        get => _packages;
        set => _packages = value?.OrderBy(p => p.Name).ToList() ?? new List<CreatioManifestPackage>();
    }
}
