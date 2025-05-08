using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using CommandLine;
using CreatioModel;
using Newtonsoft.Json;

namespace Clio;

[Verb("set-webservice-url", Aliases = new[] { "swu", "webservice" }, HelpText = "Set base url for web service")]
public class SetWebServiceUrlOptions : RemoteCommandOptions
{
    [Value(0, MetaName = "WebServiceName", Required = true, HelpText = "Web service name")]
    public string WebServiceName { get; set; }

    [Value(1, MetaName = "baseurl", Required = true, HelpText = "Base url of a web service")]
    public string WebServiceUrl { get; set; }
}

public class SetWebServiceUrlCommand : RemoteCommand<SetWebServiceUrlOptions>
{
    private readonly IDataProvider _provider;

    public SetWebServiceUrlCommand(IDataProvider provider, IApplicationClient applicationClient,
        EnvironmentSettings environmentSettings)
        : base(applicationClient, environmentSettings) =>
        _provider = provider;

    public SetWebServiceUrlCommand(IDataProvider provider, EnvironmentSettings environmentSettings)
        : base(environmentSettings) =>
        _provider = provider;

    protected override string ServicePath => "/DataService/json/SyncReply/SetUserPropertyRequest";

    private Guid GetSchemaIdBySchemaName(string optionsWebServiceName, string managerName)
    {
        IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_provider);
        return ctx
            .Models<SysSchema>()
            .FirstOrDefault(s => s.Name == optionsWebServiceName && s.ManagerName == managerName)?.Id ?? Guid.Empty;
    }

    protected override string GetRequestData(SetWebServiceUrlOptions options)
    {
        const string managerName = "ServiceSchemaManager";
        SetWebServiceUrlPayload payload = new()
        {
            ContractName = "SetUserPropertyRequest",
            SchemaId = GetSchemaIdBySchemaName(options.WebServiceName, managerName),
            PropertyName = "BaseUri",
            PropertyValue = options.WebServiceUrl,
            ManagerName = managerName
        };
        string str = JsonConvert.SerializeObject(payload);
        return str;
    }
}

[Verb("get-webservice-url", Aliases = new[] { "gwu" }, HelpText = "Get base url for web service")]
public class GetWebServiceUrlOptions : EnvironmentOptions
{
    [Value(0, MetaName = "WebServiceName", Required = false, HelpText = "Web service name")]
    public string WebServiceName { get; set; }
}

public class GetWebServiceUrlCommand(IWebServiceManager webServiceManager, ILogger logger)
    : Command<GetWebServiceUrlOptions>
{
    private readonly ILogger _logger = logger;
    private readonly IWebServiceManager _webServiceManager = webServiceManager;

    public override int Execute(GetWebServiceUrlOptions options)
    {
        List<VwWebServiceV2> webService = _webServiceManager.GetAllServices();
        List<VwWebServiceV2> webs = webService;
        if (!string.IsNullOrEmpty(options.WebServiceName))
        {
            webs = webService
                .Where(s => s.Name == options.WebServiceName)
                .ToList();
        }

        webs.ForEach(w =>
        {
            string serviceUrl = _webServiceManager.GetServiceUrl(w.PackageUId, w.UId);
            _logger.WriteInfo($"{w.Name}: {serviceUrl}");
        });
        return 0;
    }
}
