using System.Threading.Tasks;
using Autofac;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
internal class AddSchemaCommandTests : BaseCommandTests<AddSchemaOptions>
{


	[Test(Description = "Describe your test, or ask copilot to describe it for you")]
	public void Execute_ShouldReturn_WhenCalled(){
		var command = Container.Resolve<AddSchemaCommand>();
		var options = new AddSchemaOptions();
		int result = command.Execute(options);
		result.Should().Be(0);
	}

}