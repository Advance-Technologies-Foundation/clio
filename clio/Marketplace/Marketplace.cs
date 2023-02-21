namespace Clio
{
	using Clio.Common;
	using Clio.Models;
	using Newtonsoft.Json;
	using System;
	using System.IO;
	using System.Net.Http;
	using System.Threading.Tasks;

	public interface IMarketplace
	{
		Task<string> GetFileByIdAsync(int id);
	}

	public class Marketplace : IMarketplace, IDisposable
	{
		const string _baseUri = "https://marketplace.creatio.com";
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly HttpClient _httpClient;
		private MarketplaceApplicationModel _model;

		public Marketplace(IWorkingDirectoriesProvider workingDirectoriesProvider)
		{
			_httpClient = new HttpClient
			{
				BaseAddress = new Uri(_baseUri)
			};
			_workingDirectoriesProvider = workingDirectoriesProvider;
		}

		private async Task GetMrkModelById(int id)
		{
			Uri relativeUri = new Uri($"marketplace/install?appId=com-{id}", UriKind.Relative);
			var resposne = await _httpClient.GetStringAsync(relativeUri);
			_model = JsonConvert.DeserializeObject<MarketplaceApplicationModel>(resposne);
		}

		public async Task<string> GetFileByIdAsync(int id)
		{
			await GetMrkModelById(id);
			var dir = _workingDirectoriesProvider.BaseTempDirectory;

			var filename = _model.FileLink.Segments[^1];
			var fullpath = Path.Combine(dir, filename);
			Console.WriteLine(fullpath);
			var bites = await _httpClient.GetByteArrayAsync(_model.FileLink.PathAndQuery);

			using var fs = new FileStream(fullpath, FileMode.Create, FileAccess.Write, FileShare.None);
			await fs.WriteAsync(bites);
			return fullpath;
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
}