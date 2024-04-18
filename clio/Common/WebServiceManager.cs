using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using CreatioModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common;

public interface IWebServiceManager
{

	#region Methods: Public

	public List<VwWebServiceV2> GetAllServices();

	string GetServiceUrl(Guid packageUId, Guid serviceUId);

	#endregion

}

public class WebServiceManager : IWebServiceManager
{

	#region Fields: Private

	private readonly IApplicationClient _client;
	private readonly IDataProvider _dataProvider;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public WebServiceManager(IApplicationClient client, IDataProvider dataProvider,
		IServiceUrlBuilder serviceUrlBuilder, ILogger logger){
		_client = client;
		_dataProvider = dataProvider;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public List<VwWebServiceV2> GetAllServices(){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		return ctx.Models<VwWebServiceV2>().ToList();
	}

	public string GetServiceUrl(Guid packageUId, Guid serviceUId){
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