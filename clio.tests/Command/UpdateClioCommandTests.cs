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
		public void ChechVersionCommandTests() {
			var currentVersion = UpdateCliCommand.GetCurrentVersion();
			var latestVersion = UpdateCliCommand.GetLatestVersion();
			latestVersion.Should().NotBeNullOrEmpty();
			Assert.AreEqual(currentVersion, latestVersion);
		}

		[Test]
		public void ChechVersionFromGitHubAndNuget() {
			var versionFromNuget = UpdateCliCommand.GetLatestVersionFromNuget();
			var versionFromGitHub = UpdateCliCommand.GetLatestVersionFromGitHub();
			Assert.AreEqual(versionFromNuget, versionFromGitHub);
		}
	}
}
