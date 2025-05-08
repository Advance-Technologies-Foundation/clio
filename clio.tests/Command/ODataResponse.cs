using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Clio.Tests.Command;

internal class ODataResponse
{

    #region Properties: Public

    [JsonProperty("@odata.context")]
    [JsonPropertyName("@odata.context")]
    public string OdataContext { get; set; }

    [JsonProperty("value")]
    [JsonPropertyName("value")]
    public List<Dictionary<string, object>> Records { get; set; }

    public string SchemaName
    {
        get
        {
            string pattern = @"#(\w+)";
            Regex regex = new(pattern);
            Match match = regex.Match(OdataContext);
            string entityName = match.Groups[1].Value;
            return entityName;
        }
    }

    #endregion

}
