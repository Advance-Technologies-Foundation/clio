using System;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
internal sealed class EntitySchemaRequiredColumnsTests : BaseCommandTests<GetEntitySchemaPropertiesOptions> {
	private IRemoteEntitySchemaColumnManager _manager;
	private IToolCommandResolver _resolver;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_manager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_resolver = Substitute.For<IToolCommandResolver>();
		services.AddSingleton(_manager);
		services.AddSingleton(_resolver);
		services.AddTransient<GetEntitySchemaPropertiesTool>();
	}

	[TearDown]
	public void ClearCalls() {
		_manager.ClearReceivedCalls();
		_resolver.ClearReceivedCalls();
	}

	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(true, true)]
	[Description("Filters on required metadata, including an empty result, while retaining schema counts and other fields.")]
	public void GetProperties_ShouldFilterColumns_WhenRequiredOnlyIsSpecified(bool requiredOnly, bool noRequired) {
		// Arrange
		EntitySchemaPropertiesInfo snapshot = new("Contact", "Contact", null, "CrtCoreBase", "BaseEntity",
			true, "Id", "Name", 2, 1, 0, false, false, false, false, false, false, false, false, false, false, false,
			[
				new("Name", Guid.NewGuid(), "own", "Name", null, "Text", !noRequired, false, null),
				new("Code", Guid.NewGuid(), "own", "Code", null, "Text", false, false, null),
				new("Id", Guid.NewGuid(), "inherited", "Id", null, "Guid", !noRequired, true, null)
			]);
		_manager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(snapshot);
		GetEntitySchemaPropertiesCommand command = Container.GetRequiredService<GetEntitySchemaPropertiesCommand>();
		_resolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(command);
		GetEntitySchemaPropertiesTool tool = Container.GetRequiredService<GetEntitySchemaPropertiesTool>();

		// Act
		EntitySchemaPropertiesInfo result = tool.GetEntitySchemaProperties(
			new GetEntitySchemaPropertiesArgs("sandbox", "Contact", RequiredOnly: requiredOnly));

		// Assert
		result.Columns.Should().BeEquivalentTo(snapshot.Columns!.Where(column => !requiredOnly || column.Required),
			because: "required-only must retain exactly the required subset across own and inherited columns");
		(result with { Columns = snapshot.Columns }).Should().BeEquivalentTo(snapshot,
			because: "the filter changes only the returned column list");
		_resolver.ReceivedCalls().Should().Contain(call => call.GetArguments().OfType<GetEntitySchemaPropertiesOptions>()
			.Any(options => options.Environment == "sandbox" && options.Package == null && options.RequiredOnly == requiredOnly),
			because: "the MCP adapter must preserve environment selection, merged mode, and the filter flag");
	}

	[Test]
	[Description("Captures unknown wrapped JSON fields and refuses them before resolving an environment.")]
	public void GetProperties_ShouldRejectUnknownArgument_WhenDeserialized() {
		// Arrange
		GetEntitySchemaPropertiesArgs args = JsonSerializer.Deserialize<GetEntitySchemaPropertiesArgs>(
			"""{"environment-name":"sandbox","schema-name":"Contact","search-pattern":"Name"}""")!;
		GetEntitySchemaPropertiesTool tool = Container.GetRequiredService<GetEntitySchemaPropertiesTool>();

		// Act
		Action act = () => tool.GetEntitySchemaProperties(args);

		// Assert
		act.Should().Throw<ArgumentException>(because: "unsupported fields must not silently disappear")
			.WithMessage("*Unknown args: 'search-pattern'*Valid:*required-only*", because: "the caller needs an actionable field list");
		_resolver.ReceivedCalls().Should().BeEmpty(because: "invalid inputs must be refused before environment resolution");
		args.RequiredOnly.Should().BeFalse(because: "omitting required-only preserves the full column list");
	}
}
