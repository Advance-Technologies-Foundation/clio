using System;
using Clio.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class BuildDockerImageCommandTests : BaseCommandTests<BuildDockerImageOptions> {
	private BuildDockerImageCommand _command = null!;
	private IBuildDockerImageService _service = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IBuildDockerImageService>();
		containerBuilder.AddSingleton(_service);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<BuildDockerImageCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Execute should delegate docker image builds to the registered build service.")]
	public void Execute_Should_DelegateToBuildService() {
		// Arrange
		BuildDockerImageOptions options = new() {
			SourcePath = "/workspace/app",
			Template = "dev",
			OutputPath = "/workspace/out/image.tar",
			Registry = "ghcr.io/example"
		};
		_service.Execute(options).Returns(0);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because the service reported a successful build");
		_service.Received(1).Execute(options);
	}
}
