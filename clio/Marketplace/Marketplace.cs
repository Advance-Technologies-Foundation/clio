using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Models;
using Newtonsoft.Json;

namespace Clio;

public interface IMarketplace
{
    Task<string> GetFileByIdAsync(int id);
}

public class Marketplace : IMarketplace, IDisposable
{
    private const string _baseUri = "https://marketplace.creatio.com";
    private readonly HttpClient _httpClient;
    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
    private MarketplaceApplicationModel _model;

    public Marketplace(IWorkingDirectoriesProvider workingDirectoriesProvider)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(_baseUri) };
        _workingDirectoriesProvider = workingDirectoriesProvider;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async Task<string> GetFileByIdAsync(int id)
    {
        await GetMrkModelById(id);
        string dir = _workingDirectoriesProvider.BaseTempDirectory;

        string filename = _model.FileLink.Segments[^1];
        string fullpath = Path.Combine(dir, filename);
        Console.WriteLine(fullpath);
        byte[] bites = await _httpClient.GetByteArrayAsync(_model.FileLink.PathAndQuery);

        using FileStream fs = new(fullpath, FileMode.Create, FileAccess.Write, FileShare.None);
        await fs.WriteAsync(bites);
        return fullpath;
    }

    private async Task GetMrkModelById(int id)
    {
        Uri relativeUri = new($"marketplace/install?appId=com-{id}", UriKind.Relative);
        string resposne = await _httpClient.GetStringAsync(relativeUri);
        _model = JsonConvert.DeserializeObject<MarketplaceApplicationModel>(resposne);
    }

    protected virtual void Dispose(bool disposing) => _httpClient.Dispose();
}
