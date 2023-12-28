using System;
using System.Collections.Generic;
using ATF.Repository.Mock;
using Clio.Command;
using Clio.Common;
using ConsoleTables;
using CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
public class ListInstalledAppsCommandTests : BaseCommandTests<ListInstalledAppsOptions>
{

	[Test]
	public void Repository_ShouldBeCalled()
	{

		//Arrange
		DataProviderMock dataProviderMock = new ();
		ILogger loggerMock = Substitute.For<ILogger>();
		ListInstalledAppsCommand command = new(dataProviderMock, loggerMock);
		ListInstalledAppsOptions options = new();

		var mock = dataProviderMock
			.MockItems(nameof(SysInstalledApp));
			
		mock.Returns([
			new Dictionary<string, object> {
				{"Id", Guid.NewGuid()},
				{"Name", "Fake name"},
				{"Code", "FakeCode"},
				{"Description", "Fake description"}
			}
		]);
		
		int callCount = 0;
		mock.ReceiveHandler(_ => callCount++);
		
		//Act
		command.Execute(options);

		//Assert
		loggerMock.Received(1).PrintTable(Arg.Is<ConsoleTable>(table => 
			table.Rows.Count == 1 
			&& (string)table.Rows[0].GetValue(1) == "Fake name"
			&& (string)table.Rows[0].GetValue(2) == "FakeCode"
			&& (string)table.Rows[0].GetValue(3) == "Fake description"
			)
		);
		callCount.Should().Be(1);
	}
}

public class BaseCommandTests<T>
{

	protected static readonly ReadmeChecker ReadmeChecker = ClioTestsSetup.GetService<ReadmeChecker>();
	[Test]
	public void Command_ShouldHave_DescriptionBlock_InReadmeFile() =>
		ReadmeChecker
			.IsInReadme(typeof(T))
			.Should()
			.BeTrue("{0} is a command and needs a be described in README.md", this);

}