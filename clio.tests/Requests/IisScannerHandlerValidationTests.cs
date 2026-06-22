using System;
using System.Linq;
using System.Threading.Tasks;
using Clio.Requests;
using Clio.Tests.Command;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;

namespace Clio.Tests.Requests;

[TestFixture]
[Property("Module", "Requests")]
internal class IisScannerHandlerValidationTests : BaseClioModuleTests {
	[Test]
	[Description("Validates the IIS scanner request explicitly and rejects invalid external link input before the scan runs.")]
	public async Task Handle_ShouldThrowValidationException_WhenIISScannerRequestIsInvalid() {
		// Arrange
		// A well-formed absolute URI with no "return" query parameter is invalid on every platform:
		// on Windows the OS rule passes and the ARG001 ("Return type cannot be empty") rule fails;
		// on non-Windows the OS001 rule fails first (CascadeMode.Stop short-circuits before the
		// null-Uri dereference). Both paths raise a FluentValidation.ValidationException.
		IExternalLinkHandler handler = Container.GetServices<IExternalLinkHandler>()
			.First(h => h.RequestType == typeof(IISScannerRequest));
		IISScannerRequest request = new() {
			Content = "clio://IISScannerRequest/"
		};

		// Act
		Func<Task> act = async () => await handler.Handle(request);

		// Assert
		await act.Should().ThrowAsync<ValidationException>(
			because: "the handler should run the registered FluentValidation validator before scanning IIS");
	}
}
