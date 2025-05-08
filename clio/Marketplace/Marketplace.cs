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

    #region Methods: Public

    Task<string> GetFileByIdAsync(int id);

    #endregion

}

public class Marketplace : IMarketplace, IDisposable
{

    #region Constants: Private

    private const string _baseUri = "https://marketplace.creatio.com";

    #endregion

    #region Fields: Private

    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
    private readonly HttpClient _httpClient;
    private MarketplaceApplicationModel _model;

    #endregion

    #region Constructors: Public

    public Marketplace(IWorkingDirectoriesProvider workingDirectoriesProvider)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_baseUri)
        };
        _workingDirectoriesProvider = workingDirectoriesProvider;
    }

    #endregion

    #region Methods: Private

    private async Task GetMrkModelById(int id)
    {
        Uri relativeUri = new($"marketplace/install?appId=com-{id}", UriKind.Relative);
        string resposne = await _httpClient.GetStringAsync(relativeUri);
        _model = JsonConvert.DeserializeObject<MarketplaceApplicationModel>(resposne);
    }

    #endregion

    #region Methods: Protected

    protected virtual void Dispose(bool disposing)
    {
        _httpClient.Dispose();
    }

    #endregion

    #region Methods: Public

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

    #endregion

}
