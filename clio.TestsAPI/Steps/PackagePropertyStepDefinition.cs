using System.Text.Json;
using Creatio.Client;
using FluentAssertions;

namespace clio.ApiTest.Steps;

[Binding]
public class PackagePropertyStepDefinition
{

	private readonly ICreatioClient _creatioClient;
	private readonly AppSettings _appSettings;

	const string Route = "/ServiceModel/PackageService.svc/GetPackages";
	public PackagePropertyStepDefinition(ICreatioClient creatioClient, AppSettings appSettings){
		_creatioClient = creatioClient;
		_appSettings = appSettings;
	}

	[Then(@"package ""(.*)"" has property ""(.*)"" with value ""(.*)""")]
	public void ThenPackageHasPropertyWithValue(string packageName, string propertyName, string expectedPropertyValue){
		string url = _appSettings.IS_NETCORE ? _appSettings.URL+Route : _appSettings.URL + "/0" + Route;
		var response = _creatioClient.ExecutePostRequest(url, string.Empty);
		Packages? package = JsonSerializer.Deserialize<GetPackagesResponse>(response)
			.packages
			.FirstOrDefault(p=> p.name == packageName);
		string actualPropertyValue = package.GetType().GetProperty(propertyName).GetValue(package).ToString();
		actualPropertyValue.Should().Be(expectedPropertyValue);
	}

}


public record GetPackagesResponse(
    object errorInfo,
    bool success,
    Packages[] packages
);

public record Packages(
    string createdBy,
    string createdOn,
    string description,
    int hotfixState,
    string id,
    int installBehavior,
    int installType,
    bool isReadOnly,
    string maintainer,
    string modifiedBy,
    string modifiedOn,
    string name,
    int position,
    string repositoryAddress,
    int type,
    string uId,
    string version,
    bool isChanged,
    bool isLocked
);









