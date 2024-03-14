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
				{"Version", "1.0.0"}
			}
		]);
		
		int callCount = 0;
		mock.ReceiveHandler(_ => callCount++);
		
		//Act
		command.Execute(options);

		//Assert
		
		callCount.Should().Be(1);
		loggerMock.Received(1).PrintTable(Arg.Is<ConsoleTable>(table => 
			table.Rows.Count == 1 
			&& (string)table.Rows[0].GetValue(0) == "Fake name"
			&& (string)table.Rows[0].GetValue(1) == "FakeCode"
			&& (string)table.Rows[0].GetValue(2) == "1.0.0"
			)
		);
	}
}