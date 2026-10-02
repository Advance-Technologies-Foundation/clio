using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture, Property("Module", "Command")]
public sealed class PackageExplorerCommandTests : BaseCommandTests<ExportPackageGraphOptions> {
	private IPackageExplorerClient _client;
	private ILogger _logger;
	private PackageExplorerCommand _command;
	private readonly List<string> _output = [];
	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_client = Substitute.For<IPackageExplorerClient>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_client);
		services.AddSingleton(_logger);
	}
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<PackageExplorerCommand>();
		_output.Clear();
		_logger.When(logger => logger.WriteInfo(Arg.Any<string>())).Do(call => _output.Add(call.Arg<string>()));
		_client.Read(ServiceUrlBuilder.KnownRoute.DependencyGraphV1, Arg.Any<JObject>(), Arg.Any<int>()).Returns(_ => JObject.Parse("""
			{"packages":[{"uId":"a","name":"A"},{"uId":"b","name":"B"},{"uId":"c","name":"C"}],
			"dependencies":[{"packageUId":"a","dependOnPackageUId":"b"},{"packageUId":"b","dependOnPackageUId":"c"},
			{"packageUId":"c","dependOnPackageUId":"a"}],"cycles":[["a","b","c","a"]],"missingDependencies":[]}
			"""));
	}
	public override void TearDown() {
		_client.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}
	[Test, Description("A cycle-safe shortest path includes indirect dependencies and preserves direction.")]
	public void Execute_ShouldReturnShortestPath_WhenGraphContainsCycle() {
		// Arrange
		var options = new PackageDependencyPathOptions { From = "A", To = "C" };
		// Act
		int exitCode = _command.Execute(options);
		// Assert
		exitCode.Should().Be(0, because: "cycles do not prevent read-only path inspection");
		JObject.Parse(_output.Single())["path"].Select(item => (string)item["name"]).Should()
			.Equal(["A", "B", "C"], because: "dependency traversal follows directed edges");
	}
	[TestCase(false, false, 1), TestCase(false, true, 2), TestCase(true, false, 1), TestCase(true, true, 2)]
	[Description("Direction and transitivity flags change traversal while excluding the origin in cycles.")]
	public void Execute_ShouldHonorTraversalFlags_WhenDependenciesRequested(bool reverse, bool transitive, int count) {
		// Arrange
		var options = new GetPackageDependenciesOptions { Package = "A", Dependants = reverse, Transitive = transitive };
		// Act
		int exitCode = _command.Execute(options);
		// Assert
		exitCode.Should().Be(0, because: "each traversal mode is supported");
		((JArray)JObject.Parse(_output.Single())["packages"]).Count.Should().Be(count,
			because: "transitive traversal visits each package once");
	}
	[Test, Description("Exact search is sent to the server, rather than filtering an incomplete broad response.")]
	public void Execute_ShouldRequestExactSearch_WhenContainsOmitted() {
		// Arrange
		_client.Read(ServiceUrlBuilder.KnownRoute.DependencySearchV1, Arg.Any<JObject>(), Arg.Any<int>())
			.Returns(new JObject { ["schemas"] = new JArray(), ["hasMore"] = false });
		// Act
		int exitCode = _command.Execute(new FindPackageBySchemaOptions { Schema = "Contact" });
		// Assert
		exitCode.Should().Be(0, because: "empty complete searches are valid results");
		_client.Received().Read(ServiceUrlBuilder.KnownRoute.DependencySearchV1,
			Arg.Is<JObject>(request => (string)request["matchMode"] == "exact"), Arg.Any<int>());
	}
	[Test, Description("Ambiguous names cannot silently select a different package.")]
	public void Execute_ShouldFail_WhenPackageNameAmbiguous() {
		// Arrange
		_client.Read(ServiceUrlBuilder.KnownRoute.DependencyGraphV1, Arg.Any<JObject>(), Arg.Any<int>()).Returns(JObject.Parse(
			"""{"packages":[{"uId":"a","name":"A"},{"uId":"b","name":"A"}],"dependencies":[]}"""));
		// Act
		int exitCode = _command.Execute(new GetPackageDependenciesOptions { Package = "A" });
		// Assert
		exitCode.Should().Be(1, because: "the user must disambiguate by UId");
	}
}
