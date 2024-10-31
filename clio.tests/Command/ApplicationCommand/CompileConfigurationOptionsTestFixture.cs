using System.Threading;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ApplicationCommand;

[TestFixture]
public class CompileConfigurationOptionsTestFixture {

	[Test]
	public void CanSetTimeout(){
		//Arrange
		RemoteCommandOptions options = new() {
			TimeOut = 50
		};

		//Assert
		options.TimeOut.Should().Be(50);
	}

	[Test]
	public void DefaultTimeout_ShouldBe_Infinite(){
		//Arrange
		CompileConfigurationOptions options = new();

		//Assert
		options.TimeOut.Should().Be(Timeout.Infinite);
	}

	[Test]
	public void DefaultTimeoutInReomoe_ShouldBe_100K(){
		//Arrange
		RemoteCommandOptions options = new();

		//Assert
		options.TimeOut.Should().Be(100_000);
	}

}