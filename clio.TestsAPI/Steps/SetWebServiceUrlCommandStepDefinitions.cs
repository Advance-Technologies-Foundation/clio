using System.Text.Json;
using ATF.Repository;
using ATF.Repository.Providers;
using Creatio.Client;
using CreatioModel;
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
	private string _serviceName;
	public SetWebServiceUrlCommandStepDefinitions(ICLioRunner clio, AppSettings appSettings, ICreatioClient creatioClient){
		_clio = clio;
		_appSettings = appSettings;
		_creatioClient = creatioClient;
	}
	
	[Then(@"service is updated")]
	public void ThenServiceIsUpdated(){
		string selectRequest = "";
		string response = _creatioClient.ExecutePostRequest(SelectUrl, selectRequest);
		JToken token = JToken.Parse(response);
	}

	[When(@"user executes command with clio set-webservice-url ""(.*)"" ""(.*)""")]
	public void WhenUserExecutesCommandWithClioSetWebserviceUrl(string serviceName, string baseUrl){
		_serviceName = serviceName;
	}

}