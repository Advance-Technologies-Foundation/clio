using FluentAssertions;

namespace clio.ApiTest.Steps;

[Binding]
public class SetWebServiceUrlCommandStepDefinitions
{

	#region Fields: Private

	private readonly ICLioRunner _clio;

	#endregion

	#region Constructors: Public

	public SetWebServiceUrlCommandStepDefinitions(ICLioRunner clio){
		_clio = clio;
	}

	#endregion

	#region Methods: Public

	[Then(@"command clio get-webservice-url ""(.*)"" returns ""(.*)""")]
	public void ThenCommandClioGetWebserviceUrlReturns(string serviceName, string baseUrl){
		string clioResult = _clio.RunClioCommand("get-webservice-url", $"{serviceName}");
		string expected = $"[INF] - {serviceName}: {baseUrl}";
		clioResult.Trim().Should().Be(expected);
	}

	[When(@"user executes command with clio set-webservice-url ""(.*)"" ""(.*)""")]
	public void WhenUserExecutesCommandWithClioSetWebserviceUrl(string serviceName, string serviceUrl){
		string clioResult = _clio.RunClioCommand("set-webservice-url", $"{serviceName} {serviceUrl}");
		const string expected = @"[INF] - Done set-webservice-url";
		clioResult.Trim().Should().Be(expected);
	}

	#endregion

}