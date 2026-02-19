using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
internal class AddSchemaCommandTests : BaseCommandTests<AddSchemaOptions>
{

	ISchemaBuilder _schemaBuilderMock = Substitute.For<ISchemaBuilder>();
	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_schemaBuilderMock);
	}

	[Test(Description = "Describe your test, or ask copilot to describe it for you")]
	public void Execute_ShouldReturn_WhenCalled(){
		//Arrange
		var command = Container.GetRequiredService<AddSchemaCommand>();
		var options = new AddSchemaOptions();

		//Act
		int result = command.Execute(options);

		//Assert
		result.Should().Be(0);
	}
	
	[Test(Description = "Describe your test, or ask copilot to describe it for you")]
	public void Execute_ShouldCall_SchemaBuilder(){
		//Arrange
		var command = Container.GetRequiredService<AddSchemaCommand>();
		var options = new AddSchemaOptions() {
			Package = "Pkg1",
			SchemaName = "MyService",
			SchemaType = "WebService"
		};

		//Act
		int result = command.Execute(options);

		//Assert
		result.Should().Be(0);
		
		_schemaBuilderMock.Received(1).AddSchema(options.SchemaType, options.SchemaName, options.Package);
	}

}