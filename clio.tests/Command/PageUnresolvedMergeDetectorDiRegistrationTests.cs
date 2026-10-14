using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// The detector reaches <see cref="PageUpdateCommand"/> only through DI (it is an optional constructor
/// dependency, absent from every hand-built test construction), so the production registration is the one
/// place a lost warning would show.
/// </summary>
[TestFixture]
[Category("Unit")]
[NonParallelizable]
[Property("Module", "Command")]
public sealed class PageUnresolvedMergeDetectorDiRegistrationTests {

	[Test]
	[Description("The production composition root registers PageUnresolvedMergeDetector for IPageUnresolvedMergeDetector, so update-page's unresolved-merge warning is not silently disabled.")]
	public void BindingsModule_ShouldRegisterUnresolvedMergeDetector() {
		// Arrange
		System.IO.Abstractions.IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		try {
			MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
			SettingsRepository.FileSystem = fileSystem;
			IServiceCollection services = new ServiceCollection();

			// Act
			new BindingsModule(fileSystem).RegisterInto(services);
			ServiceDescriptor registration = services.LastOrDefault(d => d.ServiceType == typeof(IPageUnresolvedMergeDetector));

			// Assert
			registration.Should().NotBeNull(because: "without a registration the warning never fires");
			registration!.ImplementationType.Should().Be(typeof(PageUnresolvedMergeDetector),
				because: "PageUpdateCommand receives the detector only when DI can supply it");
		} finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}
}
