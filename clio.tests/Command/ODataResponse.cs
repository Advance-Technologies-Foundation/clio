using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Clio.Tests.Command;

internal class ODataResponse
{
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	[JsonProperty("@odata.context")]
	[JsonPropertyName("@odata.context")]
	public string OdataContext { get; set; }

	public string SchemaName {
		get {
			string pattern = @"#(\w+)";
			Regex regex = new Regex(pattern, RegexOptions.None, RegexTimeout);
			Match match = regex.Match(OdataContext);
			string entityName = match.Groups[1].Value;
			return entityName;
		}
	}

	[JsonProperty("value")]
	[JsonPropertyName("value")]
	public List<Dictionary<string, object>> Records { get; set; }
}