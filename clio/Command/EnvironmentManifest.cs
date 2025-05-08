using System.Collections.Generic;
using System.Linq;

using Clio.CreatioModel;
using CreatioModel;
using DocumentFormat.OpenXml.Spreadsheet;
using YamlDotNet.Serialization;

namespace Clio.Command;

public class EnvironmentManifest
{
    [YamlMember(Alias = "apps")]
    public List<SysInstalledApp> Applications { get; set; }

    [YamlMember(Alias = "app_hubs")]
    public List<AppHubInfo> AppHubs { get; set; }

    [YamlMember(Alias = "environment")]
    public EnvironmentSettings EnvironmentSettings { get; internal set; }

    private List<Feature> _features = [];

    [YamlMember(Alias = "features")]
    public List<Feature> Features
    {
        get => _features;
        set => _features = value ?? [];
    }

    private List<CreatioManifestSetting> _settings = [];

    [YamlMember(Alias = "settings")]
    public List<CreatioManifestSetting> Settings
    {
        get => _settings;
        set => _settings = value ?? [];
    }

    private List<CreatioManifestWebService> _webServices = [];

    [YamlMember(Alias = "webservices")]
    public List<CreatioManifestWebService> WebServices
    {
        get => _webServices;
        set => _webServices = value ?? [];
    }

    private List<CreatioManifestPackage> _packages = [];

    [YamlMember(Alias = "packages")]
    public List<CreatioManifestPackage> Packages
    {
        get => _packages;
        set => _packages = value?.OrderBy(p => p.Name).ToList() ?? [];
    }
}
