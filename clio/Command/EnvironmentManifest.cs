using System.Collections.Generic;
using System.Linq;
using Clio.CreatioModel;
using CreatioModel;
using YamlDotNet.Serialization;

namespace Clio.Command;

public class EnvironmentManifest
{
    private List<Feature> _features = [];

    private List<CreatioManifestPackage> _packages = [];

    private List<CreatioManifestSetting> _settings = [];

    private List<CreatioManifestWebService> _webServices = [];

    [YamlMember(Alias = "apps")] public List<SysInstalledApp> Applications { get; set; }

    [YamlMember(Alias = "app_hubs")] public List<AppHubInfo> AppHubs { get; set; }

    [YamlMember(Alias = "environment")] public EnvironmentSettings EnvironmentSettings { get; internal set; }

    [YamlMember(Alias = "features")]
    public List<Feature> Features
    {
        get => _features;
        set => _features = value ?? [];
    }

    [YamlMember(Alias = "settings")]
    public List<CreatioManifestSetting> Settings
    {
        get => _settings;
        set => _settings = value ?? [];
    }

    [YamlMember(Alias = "webservices")]
    public List<CreatioManifestWebService> WebServices
    {
        get => _webServices;
        set => _webServices = value ?? [];
    }

    [YamlMember(Alias = "packages")]
    public List<CreatioManifestPackage> Packages
    {
        get => _packages;
        set => _packages = value?.OrderBy(p => p.Name).ToList() ?? [];
    }
}
