using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture, Property("Module", "McpServer")]
public sealed class PackageExplorerToolTests : BaseCommandTests<ExportPackageGraphOptions> {
	private IToolCommandResolver _resolver;
	private IPackageExplorerClient _client;
	private PackageExplorerTool _tool;
	private PackageExplorerOptions _resolvedOptions;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_resolver = Substitute.For<IToolCommandResolver>();
		_client = Substitute.For<IPackageExplorerClient>();
		services.AddSingleton(_resolver);
		services.AddSingleton(_client);
	}

	public override void Setup() {
		base.Setup();
		_resolvedOptions = null;
		_resolver.Resolve<PackageExplorerCommand>(Arg.Any<EnvironmentOptions>()).Returns(call => {
			_resolvedOptions = (PackageExplorerOptions)call.Arg<EnvironmentOptions>();
			return Container.GetRequiredService<PackageExplorerCommand>();
		});
		_client.Read(Arg.Any<ServiceUrlBuilder.KnownRoute>(), Arg.Any<JObject>(), Arg.Any<int>())
			.Returns(_ => JObject.Parse("{\"packages\":[],\"dependencies\":[],\"schemas\":[]}"));
		_tool = Container.GetRequiredService<PackageExplorerTool>();
	}

	public override void TearDown() {
		_resolver.ClearReceivedCalls();
		_client.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase("GetDependencies", "get-pkg-dependencies")]
	[TestCase("GetPath", "pkg-dependency-path")]
	[TestCase("GetReasons", "pkg-dependency-why")]
	[TestCase("FindSchema", "find-pkg-by-schema")]
	[TestCase("ExportGraph", "export-pkg-graph")]
	[TestCase("CheckDependency", "check-pkg-dependency")]
	[Description("All explorer tools retain stable names and truthful read-only metadata.")]
	public void Metadata_ShouldDescribeReadOnlyOperation(string method, string name) {
		// Arrange
		MethodInfo info = typeof(PackageExplorerTool).GetMethod(method)!;
		// Act
		var metadata = info.GetCustomAttribute<McpServerToolAttribute>()!;
		// Assert
		metadata.Name.Should().Be(name, because: "tool names form the public agent contract");
		metadata.ReadOnly.Should().BeTrue(because: "previews do not modify dependency edges");
		metadata.Destructive.Should().BeFalse(because: "all six operations only inspect state");
		metadata.Idempotent.Should().BeTrue(because: "repeating an inspection has no side effects");
	}

	[TestCase("dependencies"), TestCase("path"), TestCase("reasons")]
	[TestCase("search"), TestCase("export"), TestCase("check")]
	[Description("Every explorer tool maps its flags and resolves the command in the requested environment.")]
	public void Invoke_ShouldResolveEnvironmentAndMapOptions(string operation) {
		// Arrange
		const string environment = "requested-environment";
		Action invoke = operation switch {
			"dependencies" => () => _tool.GetDependencies(new(environment, "A", true, true)),
			"path" => () => _tool.GetPath(new(environment, "A", "B")),
			"reasons" => () => _tool.GetReasons(new(environment, "A", "B", true, true)),
			"search" => () => _tool.FindSchema(new(environment, "Contact", "EntitySchemaManager", null, "extend", true, 12)),
			"export" => () => _tool.ExportGraph(new(environment, "dot")),
			_ => () => _tool.CheckDependency(new(environment, "A", "B", "remove"))
		};
		// Act
		invoke();
		// Assert
		_resolvedOptions.Should().NotBeNull(because: "the startup command must not bypass environment resolution");
		_resolvedOptions.Environment.Should().Be(environment, because: "the selected tenant owns the inspection");
		bool mapped = _resolvedOptions switch {
			GetPackageDependenciesOptions o => o.Package == "A" && o.Dependants && o.Transitive,
			PackageDependencyPathOptions o => o.From == "A" && o.To == "B",
			PackageDependencyWhyOptions o => o.From == "A" && o.To == "B" && o.Details && o.CheckRemoval,
			FindPackageBySchemaOptions o => o.Schema == "Contact" && o.ManagerName == "EntitySchemaManager"
				&& o.Purpose == "extend" && o.Contains && o.Limit == 12,
			ExportPackageGraphOptions o => o.Format == "dot",
			CheckPackageDependencyOptions o => o.From == "A" && o.To == "B" && o.Action == "remove",
			_ => false
		};
		mapped.Should().BeTrue(because: "MCP arguments must reach the same options used by the CLI");
	}
}
