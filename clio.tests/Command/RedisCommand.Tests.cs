namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
public class RedisCommandTestCase
{
    [Test]
    [Category("Unit")]
    public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework()
    {
        //Arrange
        IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
        string testUri = "TestUri";
        EnvironmentSettings settings = new() { Uri = testUri, IsNetCore = false };
        RedisCommand redisCommand = new(applicationClient, settings);
        ClearRedisOptions clearRedisOptions = new();

        //Act
        redisCommand.Execute(clearRedisOptions);

        //Assert
        applicationClient.Received(1).ExecutePostRequest(
            testUri + "/0/ServiceModel/AppInstallerService.svc/ClearRedisDb",
            "{}", 100_000, 3, 1);
    }

    [Test]
    [Category("Unit")]
    public void ClearRedisDb_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCore()
    {
        //Arrange
        IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
        string testUri = "TestUri";
        EnvironmentSettings settings = new() { Uri = testUri, IsNetCore = true };
        RedisCommand redisCommand = new(applicationClient, settings);
        ClearRedisOptions clearRedisOptions = new();

        //Act
        redisCommand.Execute(clearRedisOptions);

        //Assert
        applicationClient.Received(1).ExecutePostRequest(
            testUri + "/ServiceModel/AppInstallerService.svc/ClearRedisDb",
            "{}", 100_000, 3, 1);
    }
}
