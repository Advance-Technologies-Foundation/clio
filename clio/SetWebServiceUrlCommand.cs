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
using Newtonsoft.Json.Linq;

namespace Clio;

[Verb("set-webservice-url", Aliases = new[] {"swu", "webservice"}, HelpText = "Set base url for web service")]
public class SetWebServiceUrlOptions : EnvironmentOptions
{

	#region Properties: Public

	[Value(0, MetaName = "WebServiceName", Required = true, HelpText = "Web service name")]
	public string WebServiceName { get; set; }

	[Value(1, MetaName = "baseurl", Required = true, HelpText = "Base url of a web service")]
	public string WebServiceUrl { get; set; }

	#endregion

}

public class SetWebServiceUrlCommand : RemoteCommand<SetWebServiceUrlOptions>
{

	#region Fields: Private

	private readonly IDataProvider _provider;

	#endregion

	#region Constructors: Public

	public SetWebServiceUrlCommand(IDataProvider provider, IApplicationClient applicationClient,
		EnvironmentSettings environmentSettings)
		: base(applicationClient, environmentSettings){
		_provider = provider;
	}

	public SetWebServiceUrlCommand(IDataProvider provider, EnvironmentSettings environmentSettings)
		: base(environmentSettings){
		_provider = provider;
	}

	#endregion

	#region Properties: Protected

	protected override string ServicePath => "/DataService/json/SyncReply/SetUserPropertyRequest";

	#endregion

	#region Methods: Private

	private Guid GetSchemaIdBySchemaName(string optionsWebServiceName, string managerName){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_provider);
		return ctx
			.Models<SysSchema>()
			.FirstOrDefault(s => s.Name == optionsWebServiceName && s.ManagerName == managerName)?.Id ?? Guid.Empty;
	}

	#endregion

	#region Methods: Protected

	protected override string GetRequestData(SetWebServiceUrlOptions options){
		const string managerName = "ServiceSchemaManager";
		SetWebServiceUrlPayload payload = new SetWebServiceUrlPayload {
			contractName = "SetUserPropertyRequest",
			schemaId = GetSchemaIdBySchemaName(options.WebServiceName, managerName),
			propertyName = "BaseUri",
			propertyValue = options.WebServiceUrl,
			managerName = managerName
		};
		string str = JsonConvert.SerializeObject(payload);
		return str;
	}

	#endregion

}

[Verb("get-webservice-url", Aliases = new[] {"gwu"}, HelpText = "Get base url for web service")]
public class GetWebServiceUrlOptions : EnvironmentOptions
{

	#region Properties: Public

	[Value(0, MetaName = "WebServiceName", Required = false, HelpText = "Web service name")]
	public string WebServiceName { get; set; }

	#endregion

}

public class GetWebServiceUrlCommand : Command<GetWebServiceUrlOptions>
{

	#region Fields: Private

	private readonly IApplicationClient _client;
	private readonly IDataProvider _dataProvider;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public GetWebServiceUrlCommand(IApplicationClient client, IDataProvider dataProvider,
		IServiceUrlBuilder serviceUrlBuilder, ILogger logger){
		_client = client;
		_dataProvider = dataProvider;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(GetWebServiceUrlOptions options){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);

		
		List<VwWebServiceV2> webService = ctx.Models<VwWebServiceV2>().ToList();

		List<VwWebServiceV2> webs = webService;
		if(!string.IsNullOrEmpty(options.WebServiceName)) {
			webs = webService
				.Where(s=> s.Name== options.WebServiceName)
				.ToList();
		}
		webs.ForEach(w=> {
			string serviceUrl = GetServiceUrl(w.PackageUId, w.UId);
			_logger.WriteInfo($"{w.Name}: {serviceUrl}");
		});
		return 0;
	}

	
	private string GetServiceUrl(Guid packageUId, Guid serviceUId){
		const string endpoint = "DataService/json/SyncReply/ServiceSchemaRequest";
		string url = _serviceUrlBuilder.Build(endpoint);

		var payload = new {
			uId = serviceUId.ToString(),
			packageUId = packageUId.ToString()
		};
		string json = JsonConvert.SerializeObject(payload);
		string response = _client.ExecutePostRequest(url, json);

		JObject jObject = JObject.Parse(response);
		JToken serviceToken = jObject.SelectToken("$.schema.baseUri");
		string serviceUrl = serviceToken?.ToString();
		return serviceUrl;
	}
	
	
	#endregion

}

public record SetWebServiceUrlPayload
{

	#region Properties: Public

	public string contractName { get; init; }

	public string managerName { get; init; }

	public string propertyName { get; init; }

	public string propertyValue { get; init; }

	public Guid schemaId { get; init; }

	#endregion

}