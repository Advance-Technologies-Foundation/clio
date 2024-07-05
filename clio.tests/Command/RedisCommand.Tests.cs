namespace Clio.Tests.Command
{
	using Clio.Command;
	using Clio.Common;
	using NSubstitute;
	using NUnit.Framework;

	[TestFixture]
	public class RedisCommandTestCase
	{
		[Test, Category("Unit")]
		public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
			//Arrange
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			var testUri = "TestUri";
			var settings = new EnvironmentSettings {
				Uri = testUri,
				IsNetCore = false
			};
			RedisCommand redisCommand = new RedisCommand(applicationClient, settings);
			var clearRedisOptions = Substitute.For<ClearRedisOptions>();

			//Act
			redisCommand.Execute(clearRedisOptions);

			//Assert
			applicationClient.Received(1).ExecutePostRequest(
				testUri + "/0/ServiceModel/AppInstallerService.svc/ClearRedisDb",
				"{}", 100_000,3,1);
		}

		[Test, Category("Unit")]
		public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
			//Arrange
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			var testUri = "TestUri";
			var settings = new EnvironmentSettings {
				Uri = testUri,
				IsNetCore = true
			};
			RedisCommand redisCommand = new RedisCommand(applicationClient, settings);
			var clearRedisOptions = Substitute.For<ClearRedisOptions>();

			//Act
			redisCommand.Execute(clearRedisOptions);

			//Assert
			applicationClient.Received(1).ExecutePostRequest(
				testUri + "/ServiceModel/AppInstallerService.svc/ClearRedisDb",
				"{}", 100_000,3,1);
		}
	}
}
