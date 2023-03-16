namespace Clio.Command
{
	using Clio.Common;
	using CommandLine;
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Net.Http;
	using System.Text.Json.Serialization;
	using System.Threading.Tasks;

	[Verb("marketplace-catalog", Aliases = new string[] { "catalog" }, HelpText = "List marketplace applications")]
	public class GetMarketplaceCatalogOptions : EnvironmentOptions
	{
		[Option('n', "Name", Required = false, HelpText = "Application or package name")]
		public string Name
		{
			get; set;
		}
	}

	public class GetMarketplacecatalogCommand : Command<GetMarketplaceCatalogOptions>, IDisposable
	{
		private readonly HttpClient _httpClient;
		const string _baseUri = "https://marketplace.creatio.com";
		private IList<Application> _apps;

		public GetMarketplacecatalogCommand()
		{
			_httpClient = new HttpClient()
			{
				BaseAddress = new Uri(_baseUri)
			};
		}

		public override int Execute(GetMarketplaceCatalogOptions options)
		{
			IList<Application> apps = default;
			Task.Run(async () =>
			{
				await GetAppsAsync();
			}).Wait();

			IList<string[]> table = new List<string[]>();
			table.Add(CreateRow("Marketplace Id", "Application title"));
			table.Add(CreateEmptyRow());

			foreach (var app in _apps
				.Where(a => a.Attributes.Title.ToLower()?
				.Contains(options.Name?.ToLower() ?? "") ?? false)
				.OrderBy(appp => appp.Attributes.Title))
			{
				table.Add(CreateRow(app.Attributes.ContentId.ToString(), app.Attributes.Title));
			}
			Console.WriteLine();
			Console.WriteLine(TextUtilities.ConvertTableToString(table));
			Console.WriteLine();
			return 0;
		}
		private static string[] CreateRow(string nameColumn, string versionColumn)
		{
			return new[] { nameColumn, versionColumn };
		}

		private static string[] CreateEmptyRow()
		{
			return CreateRow(string.Empty, string.Empty);
		}


		public async Task GetAppsAsync()
		{
			List<Application> apps = new List<Application>();
			int offset = 0;
			Dto dto = default;
			do
			{
				string uri = $"/jsonapi/node/application?page[limit]=50&page[offset]={offset}&filter[datefilter][condition][path]=changed&filter[datefilter][condition][operator]=>=&filter[datefilter][condition][value]=0&fields[node--application]=title,created,changed,field_app_name,moderation_state,drupal_internal__nid,field_installation_type";
				var message = new HttpRequestMessage()
				{
					Method = HttpMethod.Get,
					RequestUri = new Uri(uri, UriKind.Relative)
				};

				HttpResponseMessage response = await _httpClient.SendAsync(message).ConfigureAwait(false);
				var strContent = await response.Content.ReadAsStringAsync();
				dto = System.Text.Json.JsonSerializer.Deserialize<Dto>(strContent);
				offset += 50;
				apps.AddRange(dto.Applications.Where(a => a.Attributes.Status == "published"));

			}
			while (dto.Applications is object && dto.Applications.Count != 0);
			_apps = apps;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			_httpClient.Dispose();
		}
	}

	public class Dto
	{
		[JsonPropertyName("links")]
		public object Links
		{
			get; set;
		}

		[JsonPropertyName("jsonapi")]
		public object JsonApi
		{
			get; set;
		}

		[JsonPropertyName("data")]
		public IList<Application> Applications
		{
			get; set;
		}
	}

	public class Application
	{
		[JsonPropertyName("type")]
		public string Type
		{
			get; set;
		}

		[JsonPropertyName("id")]
		public Guid Id
		{
			get; set;
		}

		[JsonPropertyName("links")]
		public Links Links
		{
			get; set;
		}

		[JsonPropertyName("attributes")]
		public Attributes Attributes
		{
			get; set;
		}

	}

	public class Attributes
	{
		[JsonPropertyName("title")]
		public string Title
		{
			get; set;
		}

		[JsonPropertyName("created")]
		public DateTime Created
		{
			get; set;
		}

		[JsonPropertyName("changed")]
		public DateTime Changed
		{
			get; set;
		}

		[JsonPropertyName("moderation_state")]
		public string Status
		{
			get; set;
		}

		[JsonPropertyName("field_app_name")]
		public string Name
		{
			get; set;
		}


		[JsonPropertyName("drupal_internal__nid")]
		public int ContentId
		{
			get; set;
		}

	}

	public class Links
	{
		[JsonPropertyName("self")]
		public Self Self
		{
			get; set;
		}
	}

	public class Self
	{
		[JsonPropertyName("href")]
		public Uri Href
		{
			get; set;
		}
	}
}