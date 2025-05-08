using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Project.NuGet;

public class CreatioSdkOnline : ICreatioSdk
{

    #region Fields: Private

    private List<Version> _versions;
    private readonly ILogger logger;

    #endregion

    #region Constructors: Public

    public CreatioSdkOnline(ILogger logger)
    {
        this.logger = logger;
    }

    #endregion

    #region Properties: Private

    private List<Version> Versions
    {
        get
        {
            if (_versions == null)
            {
                InitVersionsFromNuget();
            }
            return _versions;
        }
    }

    #endregion

    #region Properties: Public

    public Version LastVersion => Versions[0];

    #endregion

    #region Methods: Private

    private void InitVersionsFromNuget()
    {
        try
        {
            HttpClient client = new()
            {
                BaseAddress = new Uri("https://api.nuget.org")
            };

            string json = default;
            Task.Run(async () =>
            {
                HttpResponseMessage response = await client.GetAsync("/v3/registration5-semver1/creatiosdk/index.json");
                json = await response.Content.ReadAsStringAsync();
            }).Wait();

            Model items = JsonSerializer.Deserialize<Model>(json);
            IEnumerable<string> _ver = items.TopItems.FirstOrDefault().Items.Select(i => i.CatalogEntry.Version);
            if (_versions == null)
            {
                _versions = new List<Version>();
            }
            foreach (string item in _ver)
            {
                _versions.Add(new Version(item));
            }
            _versions.Sort();
            _versions.Reverse();
        }
        catch (Exception e)
        {
            logger.WriteError($"Error while getting Creatio SDK versions from NuGet: {e.Message}");
            _versions = new List<Version>();
        }
    }

    #endregion

    #region Methods: Public

    public Version FindLatestSdkVersion(Version applicationVersion)
    {
        return Versions.FirstOrDefault(v =>
            v.Major == applicationVersion.Major &&
            v.Minor == applicationVersion.Minor &&
            v.Build == applicationVersion.Build) ?? LastVersion;
    }

    #endregion

}

public class Model
{

    #region Properties: Public

    [JsonPropertyName("items")]
    public List<TopItems> TopItems { get; set; }

    #endregion

}

public class TopItems
{

    #region Properties: Public

    [JsonPropertyName("items")]
    public List<InnerItems> Items { get; set; }

    #endregion

}

public class InnerItems
{

    #region Properties: Public

    [JsonPropertyName("catalogEntry")]
    public CatalogEntry CatalogEntry { get; set; }

    #endregion

}

public class CatalogEntry
{

    #region Properties: Public

    [JsonPropertyName("version")]
    public string Version { get; set; }

    #endregion

}
