using System;
using System.Reflection;
using Clio.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[NonParallelizable]
[Property("Module", "Command")]
public sealed class PageUpdateCommandResolutionTests {

	[Test]
	[Description("PageUpdateCommand resolved from the composition root receives the layer inspector: without it the ENG-105680 package-choice check silently does not run.")]
	public void Container_Should_Supply_The_Layer_Inspector_To_PageUpdateCommand() {
		// Arrange
		IServiceProvider container = new BindingsModule().Register(new EnvironmentSettings {
			Uri = "https://tenant-a.creatio.example.com",
			Login = "Supervisor",
			Password = "Supervisor"
		});

		// Act
		PageUpdateCommand command = container.GetRequiredService<PageUpdateCommand>();
		object inspector = typeof(PageUpdateCommand)
			.GetField("_pageLayerInspector", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(command);

		// Assert
		inspector.Should().BeOfType<PageLayerInspector>(
			because: "the optional constructor parameter must be filled by DI, or update-page and sync-pages write without the check");
	}
}
