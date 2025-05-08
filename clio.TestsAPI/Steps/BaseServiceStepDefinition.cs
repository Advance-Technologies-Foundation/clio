using System;
using System.Security.Policy;
using System.Text.Json;
using Creatio.Client;

namespace clio.ApiTest.Steps;

public class BaseServiceStepDefinition<T>(ICreatioClient creatioClient, AppSettings appSettings)
{
    internal readonly ICreatioClient _creatioClient = creatioClient;
    internal readonly AppSettings _appSettings = appSettings;

    internal virtual string Route { get; set; }

    internal string Url => _appSettings.IS_NETCORE ? _appSettings.URL + Route : _appSettings.URL + "/0" + Route;

    internal virtual string GetPayload() => string.Empty;

    internal T GetServiceResopnse()
    {
        string? response = _creatioClient.ExecutePostRequest(Url, GetPayload());
        return JsonSerializer.Deserialize<T>(response);
    }

    internal string GeObjectPropertyValue(Packages? package, string propertyName) =>
        package.GetType().GetProperty(propertyName).GetValue(package).ToString();
}
