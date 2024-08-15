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
		public void CheckVersionFromGitHubAndNuget() {
			var versionFromNuget = UpdateCliCommand.GetLatestVersionFromNuget();
			var versionFromGitHub = UpdateCliCommand.GetLatestVersionFromGitHub();
			versionFromGitHub.CompareTo(versionFromNuget).Should().BeLessThan(1);
		}
	}
}
