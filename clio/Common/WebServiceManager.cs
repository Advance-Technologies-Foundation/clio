using System;
using System.Collections.Generic;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Command;
using CreatioModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common;

public interface IWebServiceManager
{
    public List<CreatioManifestWebService> GetCreatioManifestWebServices();

    public List<VwWebServiceV2> GetAllServices();

    public string GetServiceUrl(Guid packageUId, Guid serviceUId);
}

public class WebServiceManager(
    IApplicationClient client,
    IDataProvider dataProvider,
    IServiceUrlBuilder serviceUrlBuilder,
    ILogger logger) : IWebServiceManager
{
    private readonly IApplicationClient _client = client;
    private readonly IDataProvider _dataProvider = dataProvider;
    private readonly ILogger _logger = logger;
    private readonly IServiceUrlBuilder _serviceUrlBuilder = serviceUrlBuilder;

    public List<VwWebServiceV2> GetAllServices()
    {
        IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
        return [.. ctx.Models<VwWebServiceV2>()];
    }

    public List<CreatioManifestWebService> GetCreatioManifestWebServices()
    {
        List<CreatioManifestWebService> webservices = [];
        GetAllServices().ForEach(s =>
        {
            string serviceUrl = GetServiceUrl(s.PackageUId, s.UId);
            webservices.Add(new CreatioManifestWebService { Name = s.Name, Url = serviceUrl });
        });
        return webservices;
    }

    public string GetServiceUrl(Guid packageUId, Guid serviceUId)
    {
        const string endpoint = "DataService/json/SyncReply/ServiceSchemaRequest";
        string url = _serviceUrlBuilder.Build(endpoint);

        var payload = new { uId = serviceUId.ToString(), packageUId = packageUId.ToString() };
        string json = JsonConvert.SerializeObject(payload);
        string response = _client.ExecutePostRequest(url, json);

        JObject jObject = JObject.Parse(response);
        JToken serviceToken = jObject.SelectToken("$.schema.baseUri");
        string serviceUrl = serviceToken?.ToString();
        return serviceUrl;
    }
}

public record SetWebServiceUrlPayload
{
    public string ContractName { get; init; }

    public string ManagerName { get; init; }

    public string PropertyName { get; init; }

    public string PropertyValue { get; init; }

    public Guid SchemaId { get; init; }
}
