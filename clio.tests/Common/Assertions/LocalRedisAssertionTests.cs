using System.Threading.Tasks;
using Clio.Common.Assertions;
using Clio.Common.Database;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Assertions;

[TestFixture]
[Category("Unit")]
public class LocalRedisAssertionTests
{
	private IRedisDatabaseSelector _redisDatabaseSelector;
	private LocalRedisAssertion _sut;

	[SetUp]
	public void SetUp()
	{
		_redisDatabaseSelector = Substitute.For<IRedisDatabaseSelector>();
		_sut = new LocalRedisAssertion(_redisDatabaseSelector);
	}

	[Test]
	[Description("Should fail at discovery when local Redis cannot be discovered")]
	public async Task ExecuteAsync_WhenRedisDiscoveryFails_ShouldReturnRedisDiscoveryFailure()
	{
		// Arrange
		_redisDatabaseSelector.FindEmptyLocalDatabase().Returns(new RedisDatabaseSelectionResult
		{
			Success = false,
			ErrorMessage = "redis not reachable"
		});

		// Act
		AssertionResult result = await _sut.ExecuteAsync(false, false);

		// Assert
		result.Status.Should().Be("fail", because: "local Redis discovery failure should fail assertion");
		result.Scope.Should().Be(AssertionScope.Local, because: "local assertion must report local scope");
		result.FailedAt.Should().Be(AssertionPhase.RedisDiscovery, because: "failure originates from discovery step");
	}

	[Test]
	[Description("Should pass and include redis endpoint when discovery succeeds and no active checks requested")]
	public async Task ExecuteAsync_WhenDiscoverySucceedsAndNoChecksRequested_ShouldReturnSuccess()
	{
		// Arrange
		_redisDatabaseSelector.FindEmptyLocalDatabase().Returns(new RedisDatabaseSelectionResult
		{
			Success = true,
			DatabaseNumber = 3
		});

		// Act
		AssertionResult result = await _sut.ExecuteAsync(false, false);

		// Assert
		result.Status.Should().Be("pass", because: "successful discovery without extra checks should pass");
		result.Scope.Should().Be(AssertionScope.Local, because: "success should still indicate local scope");
		result.Resolved.Should().ContainKey("redis", because: "resolved redis info should be present on success");
	}
}
