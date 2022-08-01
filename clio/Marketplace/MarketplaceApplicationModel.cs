namespace Clio.Models
{
	using System;
	using Newtonsoft.Json;

	class MarketplaceApplicationModel
	{
		[JsonProperty("Name")]
		public string Name { get; set; }

		[JsonProperty("Maintainer")]
		public string Maintainer { get; set; }

		[JsonProperty("SupportEmail")]
		public string SupportEmail { get; set; }

		[JsonProperty("LastUpdate")]
		public long LastUpdate { get; set; }

		[JsonProperty("MarketplaceLink")]
		public Uri MarketplaceLink { get; set; }

		[JsonProperty("OrderLink")]
		public Uri OrderLink { get; set; }

		[JsonProperty("FileLink")]
		public Uri FileLink { get; set; }

		[JsonProperty("HelpLink")]
		public Uri HelpLink { get; set; }

		[JsonProperty("IsLicenseRequired")]
		public bool IsLicenseRequired { get; set; }

		[JsonProperty("DistributionType")]
		public string DistributionType { get; set; }
		[JsonProperty("AppLicName")]
		public string AppLicName { get; set; }
	}
}