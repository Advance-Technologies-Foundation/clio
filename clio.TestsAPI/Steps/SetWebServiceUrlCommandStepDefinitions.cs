using System.Text.Json;
using ATF.Repository;
using ATF.Repository.Providers;
using Creatio.Client;
using CreatioModel;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace clio.ApiTest.Steps;

[Binding]
public class SetWebServiceUrlCommandStepDefinitions
{
	private readonly string _selectUrl = "DataService/json/SyncReply/SelectQuery";
	private string SelectUrl =>
    		_appSettings.URL + "/" + (_appSettings.IS_NETCORE ? _selectUrl : $"0/{_selectUrl}");
	
	private readonly ICLioRunner _clio;
	private readonly AppSettings _appSettings;
	private readonly ICreatioClient _creatioClient;
	private readonly IDataProvider _dataProvider;
	private string _serviceName;
	private string _serviceUrl;

	public SetWebServiceUrlCommandStepDefinitions(ICLioRunner clio, AppSettings appSettings, 
		ICreatioClient creatioClient, IDataProvider dataProvider){
		_clio = clio;
		_appSettings = appSettings;
		_creatioClient = creatioClient;
		_dataProvider = dataProvider;
	}
	
	[Then(@"service is updated")]
	public void ThenServiceIsUpdated(){
		
		var ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		
		
		
		
		
		
		
		var json = File.ReadAllText("json/GetWebServices.json");
		string response = _creatioClient.ExecutePostRequest(SelectUrl, json);
		
		JObject jObject = JObject.Parse(response);
		JToken metaData = jObject.SelectToken("$.rows[0].MetaData");
		string metadata = metaData.ToString();
		
		
		int index = metadata.IndexOf('{');
		string metadataJsonString = metadata.Substring(index, metadata.Length-index);
		
		JObject metadataJObject = JObject.Parse(metadataJsonString);
		var processUrl = metadataJObject.SelectToken("$.MetaData.Schema.IQ1").ToString();
		
		processUrl.Should().Be(_serviceUrl);
		
	}

	[When(@"user executes command with clio set-webservice-url ""(.*)"" ""(.*)""")]
	public void WhenUserExecutesCommandWithClioSetWebserviceUrl(string serviceName, string serviceUrl){
		_serviceName = serviceName;
		_serviceUrl = serviceUrl;
		
		var clioResult = _clio.RunClioCommand($"set-webservice-url", $"{serviceName} {serviceUrl}");
		
		
	}

}