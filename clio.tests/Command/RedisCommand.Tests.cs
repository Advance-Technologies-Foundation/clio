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
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			RedisCommand redisCommand = new RedisCommand(applicationClient);
			var restartOptions = new ClearRedisOptions {
				Login = "Test",
				Password = "Test",
				IsNetCore = false,
				Maintainer = "Test",
				Uri = "http://test.domain.com"
			};
			redisCommand.ClearRedisDb(restartOptions);
			applicationClient.Received(1).ExecutePostRequest(
				restartOptions.Uri + "/0/ServiceModel/AppInstallerService.svc/ClearRedisDb",
				"{}");
		}

		[Test, Category("Unit")]
		public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			RedisCommand redisCommand = new RedisCommand(applicationClient);
			var restartOptions = new ClearRedisOptions {
				Login = "Test",
				Password = "Test",
				IsNetCore = true,
				Maintainer = "Test",
				Uri = "http://test.domain.com"
			};
			redisCommand.ClearRedisDb(restartOptions);
			applicationClient.Received(1).ExecutePostRequest(
				restartOptions.Uri + "/ServiceModel/AppInstallerService.svc/ClearRedisDb",
				"{}");
		}
	}
}
