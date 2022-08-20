using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Clio.Project.NuGet
{
	public class CreatioSdkOnline : ICreatioSdk
	{
		private readonly List<Version> _versions = new List<Version>();
		public Version LastVersion => _versions[0];
		public CreatioSdkOnline()
		{
			var client = new HttpClient()
			{
				BaseAddress = new Uri("https://api.nuget.org")
			};

			string json = default;
			Task.Run(async () => {
				var response = await client.GetAsync("/v3/registration5-semver1/creatiosdk/index.json");
				json = await response.Content.ReadAsStringAsync();
			}).Wait();

			var items = JsonSerializer.Deserialize<Model>(json);
			var _ver = items.TopItems.FirstOrDefault().Items.Select(i => i.CatalogEntry.Version);

			foreach (var item in _ver)
			{
				_versions.Add(new Version(item));
			}
			_versions.Sort();
			_versions.Reverse();
		}
		public Version FindSdkVersion(Version applicationVersion)
		{
			return _versions.FirstOrDefault(v => 
				v.Major == applicationVersion.Major && 
				v.Minor == applicationVersion.Minor && 
				v.Build == applicationVersion.Build);
		}
	}


	public class Model
	{

		[JsonPropertyName("items")]
		public List<TopItems> TopItems { get; set; }
	}

	public class TopItems
	{

		[JsonPropertyName("items")]
		public List<InnerItems> Items { get; set; }
	}

	public class InnerItems
	{

		[JsonPropertyName("catalogEntry")]
		public CatalogEntry CatalogEntry { get; set; }
	}

	public class CatalogEntry
	{

		[JsonPropertyName("version")]
		public string Version { get; set; }
	}
}
