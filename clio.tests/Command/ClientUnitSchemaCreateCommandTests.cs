namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
internal class ClientUnitSchemaCreateCommandTests : BaseCommandTests<ClientUnitSchemaCreateOptions> {
	private const string PackageUId = "bb000000-0000-0000-0000-000000000001";

	private IApplicationClient _applicationClient;
	private ICaptionCultureResolver _captionCultureResolver;
	private ILogger _logger;
	private ClientUnitSchemaCreateCommand _command;
	private List<string> _postedUrls;

	public override void Setup() {
		base.Setup();
		_postedUrls = [];
		_captionCultureResolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string>()).Returns("en-US");
		_applicationClient.ExecutePostRequest(default, default).ReturnsForAnyArgs(ci => {
			string url = ci.ArgAt<string>(0);
			string body = ci.ArgAt<string>(1);
			_postedUrls.Add(url);
			if (url.EndsWith("/SaveSchema", StringComparison.Ordinal)) {
				return """{"success": true}""";
			}
			return body.Contains("SysPackage", StringComparison.Ordinal)
				? $$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}"""
				: """{"success": true, "rows": []}""";
		});
		_command = Container.GetRequiredService<ClientUnitSchemaCreateCommand>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_captionCultureResolver.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_captionCultureResolver = Substitute.For<ICaptionCultureResolver>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_applicationClient);
		// A real builder in .NET Framework mode, so the posted URL proves the KnownRoute and the 0/ prefix rule.
		containerBuilder.AddSingleton<IServiceUrlBuilder>(new ServiceUrlBuilder(new EnvironmentSettings {
			Uri = "http://test",
			IsNetCore = false
		}));
		containerBuilder.AddSingleton(_captionCultureResolver);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("On .NET Framework, TryCreate saves the new client unit schema through the SaveSchema designer URL built from KnownRoute.SaveClientUnitDesignerSchema, with exactly one 0/ prefix.")]
	public void TryCreate_ShouldPostSaveSchemaToKnownRouteUrl() {
		// Arrange
		ClientUnitSchemaCreateOptions options = new() {
			SchemaName = "UsrHelperModule",
			PackageName = "Custom"
		};

		// Act
		bool result = _command.TryCreate(options, out ClientUnitSchemaCreateResponse response);

		// Assert
		result.Should().BeTrue(because: "the package resolved, the name is unused and the designer save succeeded; error: {0}",
			response.Error);
		_postedUrls.Should().ContainSingle(
			url => url == "http://test/0/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema",
			because: "the save must go to the client unit designer SaveSchema route with a single 0/ prefix on .NET Framework");
		_postedUrls.Should().NotContain(url => url.Contains("/0/0/"),
			because: "the framework prefix must be applied exactly once");
	}
}
