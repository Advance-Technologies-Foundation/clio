using System;
using System.Threading.Tasks;
using Clio.Common.ScenarioHandlers;
using Clio.Tests.Command;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
internal class CreateIISSiteHandlerTests : BaseClioModuleTests {
	[Test]
	[Description("Validates the create IIS site request explicitly and rejects invalid scenario handler input before site creation runs.")]
	public async Task Handle_ShouldThrowValidationException_WhenCreateIISSiteRequestIsInvalid() {
		// Arrange
		ICreateIISSiteHandler handler = Container.GetRequiredService<ICreateIISSiteHandler>();
		CreateIISSiteRequest request = new() {
			Arguments = []
		};

		// Act
		Func<Task> act = async () => await handler.Handle(request);

		// Assert
		await act.Should().ThrowAsync<ValidationException>(
			because: "the handler should run the registered FluentValidation validator before creating the IIS site");
	}
}
