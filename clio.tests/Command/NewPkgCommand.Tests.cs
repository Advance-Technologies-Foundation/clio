using System.IO;
using Clio.Command;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command
{
	[TestFixture]
	public class NewPkgCommandTestCase
	{

		[Test, Category("Integration")]
		public void Execute_CreatesNewPackageInFileSystem() {
			ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
			settingsRepository.GetEnvironment().Returns(new EnvironmentSettings {
				Maintainer = "TestMaintainer"
			});
			Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
			NewPkgCommand command = new NewPkgCommand(settingsRepository, referenceCommand);
			NewPkgOptions options = new NewPkgOptions { Name = "Test" };
			command.Execute(options);
			Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), options.Name)).Should().BeTrue();
		}

		[Test, Category("Unit")]
		public void Execute_ChangesReferences_WhenRebaseSpecifiedAndNotEqualsToNuget() {
			ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
			settingsRepository.GetEnvironment().Returns(new EnvironmentSettings {
				Maintainer = "TestMaintainer"
			});
			Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
			NewPkgCommand command = new NewPkgCommand(settingsRepository, referenceCommand);
			NewPkgOptions options = new NewPkgOptions { Name = "Test", Rebase = "src" };
			command.Execute(options);
			referenceCommand.Received(1).Execute(Arg.Is<ReferenceOptions>(e => e.ReferenceType == options.Rebase));
		}

	}
}
