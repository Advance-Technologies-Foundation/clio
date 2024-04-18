using System.Text.Json;
using Creatio.Client;
using FluentAssertions;

namespace clio.ApiTest.Steps;

[Binding]
public class PackagePropertyStepDefinition: BaseServiceStepDefinition<GetPackagesResponse>
{

	internal string Route = "/ServiceModel/PackageService.svc/GetPackages";

	public PackagePropertyStepDefinition(ICreatioClient creatioClient, AppSettings appSettings) : base(creatioClient, appSettings) {
	}

	[Then(@"package ""(.*)"" has property ""(.*)"" with value ""(.*)""")]
	public void ThenPackageHasPropertyWithValue(string packageName, string propertyName, string expectedPropertyValue){
        GetPackagesResponse serviceResponse = GetServiceResopnse();
        var package = serviceResponse
			.packages
			.FirstOrDefault(p=> p.name == packageName);
		string actualPropertyValue = GeObjectPropertyValue(package, propertyName);
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









