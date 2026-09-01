using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

// This fixture intentionally reflects obsolete signatures to protect existing binary consumers.
#pragma warning disable CS0618
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class PublicApiCompatibilityTests {
	[Test]
	[Description("Retains public factory and OAuth probe signatures that existed before the CreatioClient migration.")]
	public void PublicContracts_ShouldRetainFormerMethodSignatures() {
		// Arrange
		Type applicationFactory = typeof(IApplicationClientFactory);

		// Act
		Type createClientReturn = applicationFactory.GetMethod("CreateClient")!.ReturnType;
		Type createEnvironmentReturn = applicationFactory.GetMethod("CreateEnvironmentClient")!.ReturnType;
		Type cultureReturn = typeof(ICurrentUserCultureResolverFactory).GetMethod("Create")!.ReturnType;
		Type versionReturn = typeof(IPlatformVersionResolverFactory).GetMethod("Create")!.ReturnType;
		var bearerProbe = typeof(IIdentityServerProbe).GetMethod("RunBearerDataServiceSmokeTest",
			[typeof(string), typeof(string)]);

		// Assert
		createClientReturn.Should().Be(typeof(IApplicationClient),
			because: "existing factory implementers must retain their original return contract");
		createEnvironmentReturn.Should().Be(typeof(IApplicationClient),
			because: "existing environment factory implementers must retain their original return contract");
		cultureReturn.Should().Be(typeof(ICurrentUserCultureResolver),
			because: "culture resolver factories are a public extension point");
		versionReturn.Should().Be(typeof(IPlatformVersionResolver),
			because: "platform resolver factories are a public extension point");
		bearerProbe.Should().NotBeNull(because: "compiled OAuth probe consumers require the former overload");
	}

	[Test]
	[Description("Retains public constructors replaced by ownership-aware CreatioClient dependencies.")]
	public void PublicTypes_ShouldRetainFormerConstructors() {
		// Act
		bool browserConstructor = HasConstructor<BrowserSessionService>(
			typeof(ICreatioAuthClient), typeof(IBrowserSessionCache), typeof(IFileSystem),
			typeof(System.Net.Http.IHttpClientFactory));
		bool imageConstructor = HasConstructor<SysImageUploader>(
			typeof(EnvironmentSettings), typeof(ICreatioAuthClient), typeof(System.Net.Http.IHttpClientFactory),
			typeof(IFileSystem));
		bool probeConstructor = HasConstructor<IdentityServerProbe>(typeof(System.Net.Http.IHttpClientFactory));

		// Assert
		browserConstructor.Should().BeTrue(because: "existing browser-session consumers must still load");
		imageConstructor.Should().BeTrue(because: "existing image-uploader consumers must still load");
		probeConstructor.Should().BeTrue(because: "existing OAuth-probe consumers must still load");
	}

	[Test]
	[Description("Ownership extensions wrap legacy culture resolvers while forwarding cancellation exactly.")]
	public async Task CultureResolverFactory_CreateOwned_ShouldForwardLegacyResolver() {
		// Arrange
		using CancellationTokenSource cancellation = new();
		ICurrentUserCultureResolver resolver = Substitute.For<ICurrentUserCultureResolver>();
		resolver.ResolveAsync(cancellation.Token).Returns(Task.FromResult(CultureResolution.Resolved("en-US")));
		ICurrentUserCultureResolverFactory factory = Substitute.For<ICurrentUserCultureResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);

		// Act
		using IOwnedCurrentUserCultureResolver owned = factory.CreateOwned(new EnvironmentSettings());
		CultureResolution result = await owned.ResolveAsync(cancellation.Token);

		// Assert
		result.Should().NotBeNull(because: "the compatibility lease must forward the legacy resolver result");
		await resolver.Received(1).ResolveAsync(cancellation.Token);
	}

	[Test]
	[Description("Ownership extensions preserve an already-owned platform resolver without adding a wrapper.")]
	public void PlatformResolverFactory_CreateOwned_ShouldPreserveOwnedResolverIdentity() {
		// Arrange
		IOwnedPlatformVersionResolver resolver = Substitute.For<IOwnedPlatformVersionResolver>();
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);

		// Act
		IOwnedPlatformVersionResolver result = factory.CreateOwned(new EnvironmentSettings());

		// Assert
		result.Should().BeSameAs(resolver,
			because: "an owned resolver must not be hidden behind a compatibility lease");
	}

	private static bool HasConstructor<T>(params Type[] parameters) =>
		typeof(T).GetConstructors().Any(constructor =>
			constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters));
}
#pragma warning restore CS0618
