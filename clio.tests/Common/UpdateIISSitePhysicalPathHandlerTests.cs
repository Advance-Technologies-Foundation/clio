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
internal class UpdateIISSitePhysicalPathHandlerTests : BaseClioModuleTests {
	[Test]
	[Description("Validates the update IIS site physical path request explicitly and rejects invalid scenario handler input before the IIS update runs.")]
	public async Task Handle_ShouldThrowValidationException_WhenUpdateIISSitePhysicalPathRequestIsInvalid() {
		// Arrange
		IUpdateIISSitePhysicalPathHandler handler = Container.GetRequiredService<IUpdateIISSitePhysicalPathHandler>();
		UpdateIISSitePhysicalPathRequest request = new() {
			Arguments = []
		};

		// Act
		Func<Task> act = async () => await handler.Handle(request);

		// Assert
		await act.Should().ThrowAsync<ValidationException>(
			because: "the handler should run the registered FluentValidation validator before updating the IIS site physical path");
	}
}
