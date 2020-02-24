namespace Clio.Tests.Command
{
    using System.Threading;
    using Clio.Command;
	using Clio.Common;
	using NSubstitute;
	using NUnit.Framework;

	[TestFixture]
	public class RedisCommandTestCase
	{
		[Test, Category("Unit")]
		public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			var testUri = "TestUri";
			var settings = new EnvironmentSettings {
				Uri = testUri,
				IsNetCore = false
			};
			RedisCommand redisCommand = new RedisCommand(applicationClient, settings);
			var clearRedisOptions = Substitute.For<ClearRedisOptions>();
			redisCommand.Execute(clearRedisOptions);
			applicationClient.Received(1).ExecutePostRequest(
				testUri + "/0/ServiceModel/AppInstallerService.svc/ClearRedisDb",
				"{}", Timeout.Infinite);
		}

		[Test, Category("Unit")]
		public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			var testUri = "TestUri";
			var settings = new EnvironmentSettings {
				Uri = testUri,
				IsNetCore = true
			};
			RedisCommand redisCommand = new RedisCommand(applicationClient, settings);
			var clearRedisOptions = Substitute.For<ClearRedisOptions>();
			redisCommand.Execute(clearRedisOptions);
			applicationClient.Received(1).ExecutePostRequest(
				testUri + "/ServiceModel/AppInstallerService.svc/ClearRedisDb",
				"{}", Timeout.Infinite);
		}
	}
}
