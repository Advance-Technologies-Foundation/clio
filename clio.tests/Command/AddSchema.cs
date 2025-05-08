using System.Threading.Tasks;
using Autofac;
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
    private ISchemaBuilder _schemaBuilderMock = Substitute.For<ISchemaBuilder>();

    protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
    {
        base.AdditionalRegistrations(containerBuilder);
        containerBuilder.RegisterInstance(_schemaBuilderMock);
    }

    [Test(Description = "Describe your test, or ask copilot to describe it for you")]
    public void Execute_ShouldReturn_WhenCalled()
    {
        //Arrange
        AddSchemaCommand command = Container.Resolve<AddSchemaCommand>();
        AddSchemaOptions options = new();

        //Act
        int result = command.Execute(options);

        //Assert
        result.Should().Be(0);
    }

    [Test(Description = "Describe your test, or ask copilot to describe it for you")]
    public void Execute_ShouldCall_SchemaBuilder()
    {
        //Arrange
        AddSchemaCommand command = Container.Resolve<AddSchemaCommand>();
        AddSchemaOptions options = new() { Package = "Pkg1", SchemaName = "MyService", SchemaType = "WebService" };

        //Act
        int result = command.Execute(options);

        //Assert
        result.Should().Be(0);

        _schemaBuilderMock.Received(1).AddSchema(options.SchemaType, options.SchemaName, options.Package);
    }
}
