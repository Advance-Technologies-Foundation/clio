namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
public class RedisCommandTestCase : BaseCommandTests<ClearRedisOptions>{
	
	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();
	private readonly IServiceUrlBuilder _serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	private const string TestUri = "https://localhost";

	[Test, Category("Unit")]
	public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
		//Arrange
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearRedisDb)
						 .Returns($"{TestUri}/0/ServiceModel/AppInstallerService.svc/ClearRedisDb");
		
		EnvironmentSettings settings = new () {
			Uri = TestUri,
			IsNetCore = false
		};
		RedisCommand redisCommand = new (_applicationClient, settings, _serviceUrlBuilder);
		ClearRedisOptions clearRedisOptions = new();

		//Act
		redisCommand.Execute(clearRedisOptions);

		//Assert
		_applicationClient.Received(1).ExecutePostRequest(
			TestUri + "/0/ServiceModel/AppInstallerService.svc/ClearRedisDb",
			"{}", 100_000,3,1);
	}

	[Test, Category("Unit")]
	public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
		//Arrange
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearRedisDb)
						  .Returns($"{TestUri}/ServiceModel/AppInstallerService.svc/ClearRedisDb");
		EnvironmentSettings settings = new () {
			Uri = TestUri,
			IsNetCore = true
		};
		RedisCommand redisCommand = new (_applicationClient, settings, _serviceUrlBuilder);
		ClearRedisOptions clearRedisOptions = new();

		//Act
		redisCommand.Execute(clearRedisOptions);

		//Assert
		_applicationClient.Received(1).ExecutePostRequest(
			TestUri + "/ServiceModel/AppInstallerService.svc/ClearRedisDb",
			"{}", 100_000,3,1);
	}
}
