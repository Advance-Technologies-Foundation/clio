using System;
using Clio.Common;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class DataForgePlatformVersionGuardTests {
	[Test]
	[Category("Unit")]
	[Description("EnsureSupported should accept Creatio platform version 10.0.0 and cache the successful verification.")]
	public void EnsureSupported_Should_Accept_Minimum_Version_And_Cache_Result() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) = CreateDependencies(
			"""{"GetApplicationInfoResult":{"ProductVersion":"10.0.0"}}""");
		DataForgePlatformVersionGuard guard = new(applicationClient, serviceUrlBuilder);

		// Act
		guard.EnsureSupported();
		guard.EnsureSupported();

		// Assert
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo",
			string.Empty,
			DataForgePlatformVersionGuard.VersionCheckTimeoutMs,
			1,
			1);
	}

	[Test]
	[Category("Unit")]
	[Description("EnsureSupported should reject Creatio platform versions below 10.0.0.")]
	public void EnsureSupported_Should_Reject_Version_Below_Minimum() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) = CreateDependencies(
			"""{"GetApplicationInfoResult":{"ProductVersion":"9.9.9"}}""");
		DataForgePlatformVersionGuard guard = new(applicationClient, serviceUrlBuilder);

		// Act
		Action action = guard.EnsureSupported;

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*Creatio platform version 10.0.0 or later*Current Creatio platform version: 9.9.9*",
				because: "DataForge proxy tools are only supported on Creatio platform versions that bundle CrtDataForge");
	}

	[Test]
	[Category("Unit")]
	[Description("EnsureSupported should reject responses where the Creatio platform version cannot be determined.")]
	public void EnsureSupported_Should_Reject_Missing_Version() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) = CreateDependencies(
			"""{"GetApplicationInfoResult":{"ProductName":"Creatio"}}""");
		DataForgePlatformVersionGuard guard = new(applicationClient, serviceUrlBuilder);

		// Act
		Action action = guard.EnsureSupported;

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*Unable to verify Creatio platform version*",
				because: "the tool should fail closed when platform compatibility cannot be verified");
	}

	[Test]
	[Category("Unit")]
	[Description("EnsureSupported should pass through version 0.0.0.0 as a development build without enforcing the minimum version requirement.")]
	public void EnsureSupported_Should_Accept_Dev_Build_Version_Zero() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) = CreateDependencies(
			"""{"GetApplicationInfoResult":{"ProductVersion":"0.0.0.0"}}""");
		DataForgePlatformVersionGuard guard = new(applicationClient, serviceUrlBuilder);

		// Act
		Action action = guard.EnsureSupported;

		// Assert
		action.Should().NotThrow(
			because: "version 0.0.0.0 identifies a development build where DataForge is expected to be available");
	}

	private static (IApplicationClient ApplicationClient, IServiceUrlBuilder ServiceUrlBuilder) CreateDependencies(
		string response) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetApplicationInfo)
			.Returns("http://localhost/0/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo");
		applicationClient.ExecutePostRequest(
				"http://localhost/0/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo",
				string.Empty,
				DataForgePlatformVersionGuard.VersionCheckTimeoutMs,
				1,
				1)
			.Returns(response);
		return (applicationClient, serviceUrlBuilder);
	}
}
