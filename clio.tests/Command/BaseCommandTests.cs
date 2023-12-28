using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

public class BaseCommandTests<T>
{

	private static readonly ReadmeChecker ReadmeChecker = ClioTestsSetup.GetService<ReadmeChecker>();
	
	[Test]
	public void Command_ShouldHave_DescriptionBlock_InReadmeFile() =>
		ReadmeChecker
			.IsInReadme(typeof(T))
			.Should()
			.BeTrue("{0} is a command and needs a be described in README.md", this);

}