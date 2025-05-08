using System;
using Newtonsoft.Json;

namespace Clio.Models;

internal class MarketplaceApplicationModel
{

    #region Properties: Public

    [JsonProperty("AppLicName")]
    public string AppLicName { get; set; }

    [JsonProperty("DistributionType")]
    public string DistributionType { get; set; }

    [JsonProperty("FileLink")]
    public Uri FileLink { get; set; }

    [JsonProperty("HelpLink")]
    public Uri HelpLink { get; set; }

    [JsonProperty("IsLicenseRequired")]
    public bool IsLicenseRequired { get; set; }

    [JsonProperty("LastUpdate")]
    public long LastUpdate { get; set; }

    [JsonProperty("Maintainer")]
    public string Maintainer { get; set; }

    [JsonProperty("MarketplaceLink")]
    public Uri MarketplaceLink { get; set; }

    [JsonProperty("Name")]
    public string Name { get; set; }

    [JsonProperty("OrderLink")]
    public Uri OrderLink { get; set; }

    [JsonProperty("SupportEmail")]
    public string SupportEmail { get; set; }

    #endregion

}
