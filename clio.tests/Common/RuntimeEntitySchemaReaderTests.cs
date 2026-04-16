using System;
using System.Linq;
using Clio.Common;
using Clio.Common.EntitySchema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public sealed class RuntimeEntitySchemaReaderTests {
	[Test]
	[Category("Unit")]
	[Description("Reads a runtime schema by name, preserves the full column set, and resolves the primary display column name from the explicit schema field when it is provided.")]
	public void GetByName_Should_Parse_Rich_Runtime_Schema_Response() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		applicationClient.ExecutePostRequest(
				"https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""
				{
				  "success": true,
				  "schema": {
				    "uId": "11111111-1111-1111-1111-111111111111",
				    "name": "Contact",
				    "primaryColumnUId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				    "primaryDisplayColumnName": "Name",
				    "primaryDisplayColumnUId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				    "columns": {
				      "Items": {
				        "1": {
				          "uId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				          "name": "Name",
				          "caption": { "en-US": "Full name" },
				          "description": { "en-US": "Primary display name" },
				          "dataValueType": 1,
				          "isRequired": true,
				          "isInherited": false
				        },
				        "2": {
				          "uId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
				          "name": "CreatedOn",
				          "caption": { "en-US": "Created on" },
				          "description": {},
				          "dataValueType": 7,
				          "isRequired": false,
				          "isInherited": true
				        }
				      }
				    }
				  }
				}
				""");
		RuntimeEntitySchemaReader reader = new(applicationClient, serviceUrlBuilder);

		// Act
		RuntimeEntitySchemaResult result = reader.GetByName("Contact");

		// Assert
		result.Name.Should().Be("Contact", because: "the reader should preserve the runtime schema name");
		result.PrimaryColumnUId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
			because: "the reader should expose the schema primary column UId");
		result.PrimaryDisplayColumnName.Should().Be("Name",
			because: "the reader should preserve the explicit primary display column name");
		result.Columns.Should().HaveCount(2, because: "the shared reader must preserve the full column set without filtering inherited columns");
		result.Columns.Single(column => column.Name == "CreatedOn").IsInherited.Should().BeTrue(
			because: "the reader should leave inherited-column filtering to its consumers");
		result.Columns.Single(column => column.Name == "Name").Caption.Should().Be("Full name",
			because: "the reader should resolve localized captions into a consumable string value");
		result.Columns.Single(column => column.Name == "Name").Description.Should().Be("Primary display name",
			because: "the reader should resolve localized descriptions into a consumable string value");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the primary display column name from the primaryDisplayColumnUId when the runtime schema omits the explicit name field.")]
	public void GetByName_Should_Fallback_To_PrimaryDisplayColumnUId() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		applicationClient.ExecutePostRequest(
				"https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""
				{
				  "success": true,
				  "schema": {
				    "uId": "11111111-1111-1111-1111-111111111111",
				    "name": "Account",
				    "primaryColumnUId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				    "primaryDisplayColumnUId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
				    "columns": {
				      "Items": {
				        "1": {
				          "uId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				          "name": "Id",
				          "dataValueType": 0,
				          "isRequired": true,
				          "isInherited": false
				        },
				        "2": {
				          "uId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
				          "name": "Name",
				          "dataValueType": 1,
				          "isRequired": true,
				          "isInherited": false
				        }
				      }
				    }
				  }
				}
				""");
		RuntimeEntitySchemaReader reader = new(applicationClient, serviceUrlBuilder);

		// Act
		RuntimeEntitySchemaResult result = reader.GetByName("Account");

		// Assert
		result.PrimaryDisplayColumnName.Should().Be("Name",
			because: "the reader should recover the primary display column name from the referenced column when the explicit field is missing");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats empty localized objects as missing values instead of throwing, because Creatio often returns {} for caption or description.")]
	public void GetByName_Should_Tolerate_Empty_Localized_Objects() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		applicationClient.ExecutePostRequest(
				"https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""
				{
				  "success": true,
				  "schema": {
				    "uId": "11111111-1111-1111-1111-111111111111",
				    "name": "Contact",
				    "primaryColumnUId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				    "columns": {
				      "Items": {
				        "1": {
				          "uId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				          "name": "Name",
				          "caption": {},
				          "description": {},
				          "dataValueType": 1,
				          "isRequired": true,
				          "isInherited": false
				        }
				      }
				    }
				  }
				}
				""");
		RuntimeEntitySchemaReader reader = new(applicationClient, serviceUrlBuilder);

		// Act
		RuntimeEntitySchemaResult result = reader.GetByName("Contact");

		// Assert
		result.Columns.Should().ContainSingle(because: "an empty localized object should not break runtime schema parsing");
		result.Columns[0].Caption.Should().BeNull(because: "empty localized caption objects should map to null");
		result.Columns[0].Description.Should().BeNull(because: "empty localized description objects should map to null");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails clearly when Creatio does not return a successful runtime schema payload for the requested schema name.")]
	public void GetByName_Should_Fail_When_Runtime_Schema_Is_Missing() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		applicationClient.ExecutePostRequest(
				"https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"success":false,"errorInfo":{"message":"Schema not found"}}""");
		RuntimeEntitySchemaReader reader = new(applicationClient, serviceUrlBuilder);

		// Act
		Action act = () => reader.GetByName("MissingSchema");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "missing runtime schemas should fail with the server-side error message instead of returning a partial object")
			.WithMessage("*Schema not found*");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes the schema-name request body through JsonSerializer so quotes and backslashes cannot break the RuntimeEntitySchemaRequest payload.")]
	public void GetByName_Should_Serialize_Request_Body_Safely() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		applicationClient.ExecutePostRequest(
				"https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""
				{
				  "success": true,
				  "schema": {
				    "uId": "11111111-1111-1111-1111-111111111111",
				    "name": "Contact\\\"Quoted",
				    "primaryColumnUId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				    "columns": {
				      "Items": {
				        "1": {
				          "uId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
				          "name": "Name",
				          "dataValueType": 1,
				          "isRequired": true,
				          "isInherited": false
				        }
				      }
				    }
				  }
				}
				""");
		RuntimeEntitySchemaReader reader = new(applicationClient, serviceUrlBuilder);

		// Act
		RuntimeEntitySchemaResult result = reader.GetByName("Contact\\\"Quoted");

		// Assert
		result.Name.Should().Be("Contact\\\"Quoted",
			because: "the runtime schema response should still deserialize when the request body contains escaped quotes");
		applicationClient.Received(1).ExecutePostRequest(
			"https://sandbox/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest",
			Arg.Is<string>(body => body == "{\"Name\":\"Contact\\\\\\\"Quoted\"}"),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}
}
