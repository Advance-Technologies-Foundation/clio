using Clio.Command.UpdateCliCommand;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Tests.Command
{
	[TestFixture]
	internal class UpdateClioCommandTests
	{
		[Test]
		public void CheckVersionCommandTests() {
			var currentVersion = UpdateCliCommand.GetCurrentVersion();
			var latestVersion = UpdateCliCommand.GetLatestVersion();
			latestVersion.Should().NotBeNullOrEmpty();
			currentVersion.Should().Be(latestVersion);
		}

		[Test]
		public void CheckVersionFromGitHubAndNuget() {
			var versionFromNuget = UpdateCliCommand.GetLatestVersionFromNuget();
			var versionFromGitHub = UpdateCliCommand.GetLatestVersionFromGitHub();
			versionFromNuget.Should().Be(versionFromGitHub);
		}
	}
}
