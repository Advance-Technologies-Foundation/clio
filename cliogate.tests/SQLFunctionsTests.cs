using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using ClioGate.Functions.SQL;
using FluentAssertions;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Configuration.Tests;
using Terrasoft.Core;
using Terrasoft.Core.DB;

namespace cliogate.tests
{
	[TestFixture]
	[MockSettings(RequireMock.All)]
	[Category("ClioGate")]
	public class SQLFunctionsTests : BaseMarketplaceTestFixture
	{

		protected override void SetUp(){
			base.SetUp();
		}

		[Test]
		public void Script_Returns_RowsInJson(){

			//Arrange
			UserConnection.DBEngine = Substitute.ForPartsOf<DBEngine>();
			const string sqlString = "SELECT Id, Name FROM CONTACT";
			DataTable dt = new DataTable("clioTable");
			dt.Columns.Add(new DataColumn("Id", typeof(Guid)));
			dt.Columns.Add(new DataColumn("Name", typeof(string)));
			Guid id1 = Guid.NewGuid();
			Guid id2 = Guid.NewGuid();
			dt.Rows.Add(new object[] { id1, "Contact1" });
			dt.Rows.Add(new object[] { id2, "Contact2" });
			DataTableReader reader = dt.CreateDataReader();
			
			Type gasType = typeof(GlobalAppSettings);
			
			//Is there a better way to do it?
			PropertyInfo propertyInfo = gasType.GetProperty("DenyCustomQueryApiUsage", BindingFlags.Static | BindingFlags.NonPublic);
			propertyInfo.SetValue(null, false);
			
			UserConnection.DBExecutor.ExecuteReader(
				Arg.Is<string>(s=> sqlEval(s))
			).Returns(reader);
			
			//Act
			var actual = SQLFunctions.ExecuteSQL(sqlString, UserConnection);
			var list = JsonConvert.DeserializeObject<List<Dto>>(actual);
			
			//Assert
			UserConnection.DBExecutor.Received(1).ExecuteReader(Arg.Any<string>());
			list.Should().HaveCount(2);
			list.First().Name.Should().Be("Contact1");
			list.First().Id.Should().Be(id1);
			list.Last().Name.Should().Be("Contact2");
			list.Last().Id.Should().Be(id2);
		}
		public bool sqlEval(string sqlString){
			return true;
		}
	}
	
	internal class Dto
	{

		[JsonProperty("Id")]
		public Guid Id { get; set; }

		[JsonProperty("Name")]
		public string Name { get; set; }

	}
}