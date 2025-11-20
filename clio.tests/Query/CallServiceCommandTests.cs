using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CommandLine;
using Clio.Query;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Query;

/// <summary>
/// Unit tests for CallServiceCommand with --body parameter functionality
/// </summary>
[TestFixture]
public class CallServiceCommandTestCase
{
	[Test]
	[Description("Verifies CallServiceCommandOptions has RequestBody property with Option attribute")]
	public void CallServiceCommandOptions_HasRequestBodyProperty_WithOptionAttribute()
	{
		// Arrange
		var optionsType = typeof(CallServiceCommandOptions);
		var requestBodyProperty = optionsType.GetProperty("RequestBody");

		// Act & Assert
		requestBodyProperty.Should().NotBeNull("because RequestBody property should exist on CallServiceCommandOptions");
		
		// Verify Option attribute exists
		var optionAttribute = requestBodyProperty.GetCustomAttribute<OptionAttribute>();
		optionAttribute.Should().NotBeNull("because RequestBody should have Option attribute to expose CLI parameter");
	}

	[Test]
	[Description("Verifies RequestBody property is public and not read-only")]
	public void CallServiceCommandOptions_RequestBodyProperty_IsPublic()
	{
		// Arrange
		var optionsType = typeof(CallServiceCommandOptions);
		var requestBodyProperty = optionsType.GetProperty("RequestBody");

		// Act & Assert
		requestBodyProperty.Should().NotBeNull();
		requestBodyProperty.GetMethod.Should().NotBeNull("because RequestBody should have a public getter");
		requestBodyProperty.SetMethod.Should().NotBeNull("because RequestBody should have a public setter");
	}

	[Test]
	[Description("Verifies CallServiceCommandOptions inherits from RemoteCommandOptions")]
	public void CallServiceCommandOptions_InheritsFromRemoteCommandOptions()
	{
		// Arrange
		var optionsType = typeof(CallServiceCommandOptions);
		var baseType = optionsType.BaseType;

		// Act & Assert
		baseType.Should().NotBeNull();
		baseType.Name.Should().Be("RemoteCommandOptions", "because CallServiceCommandOptions should inherit from RemoteCommandOptions");
	}

	[Test]
	[Description("Verifies CallServiceCommand class exists and inherits from BaseServiceCommand")]
	public void CallServiceCommand_ClassExists_WithCorrectBase()
	{
		// Arrange
		var commandType = typeof(CallServiceCommand);

		// Act & Assert
		commandType.Should().NotBeNull("because CallServiceCommand should exist");
		var baseType = commandType.BaseType;
		baseType.Should().NotBeNull();
		baseType.Name.Should().Contain("BaseServiceCommand", "because CallServiceCommand should inherit from BaseServiceCommand");
	}

	[Test]
	[Description("Verifies Option attribute on RequestBody has 'b' short name")]
	public void CallServiceCommandOptions_OptionAttribute_HasShortName()
	{
		// Arrange
		var optionsType = typeof(CallServiceCommandOptions);
		var requestBodyProperty = optionsType.GetProperty("RequestBody");
		var optionAttribute = requestBodyProperty.GetCustomAttribute<OptionAttribute>();

		// Act & Assert
		optionAttribute.ShortName.Should().Be("b", "because --body parameter should have 'b' short name");
	}

	[Test]
	[Description("Verifies Option attribute on RequestBody has 'body' long name")]
	public void CallServiceCommandOptions_OptionAttribute_HasLongName()
	{
		// Arrange
		var optionsType = typeof(CallServiceCommandOptions);
		var requestBodyProperty = optionsType.GetProperty("RequestBody");
		var optionAttribute = requestBodyProperty.GetCustomAttribute<OptionAttribute>();

		// Act & Assert
		optionAttribute.LongName.Should().Be("body", "because --body parameter should have 'body' long name");
	}

	[Test]
	[Description("Verifies RequestBody property is optional (not required)")]
	public void CallServiceCommandOptions_RequestBodyProperty_IsNotRequired()
	{
		// Arrange
		var optionsType = typeof(CallServiceCommandOptions);
		var requestBodyProperty = optionsType.GetProperty("RequestBody");
		var optionAttribute = requestBodyProperty.GetCustomAttribute<OptionAttribute>();

		// Act & Assert
		optionAttribute.Required.Should().Be(false, "because --body parameter should be optional");
	}

	[Test]
	[Description("Verifies CallServiceCommand can be instantiated with required dependencies")]
	public void CallServiceCommand_CanBeInstantiated()
	{
		// Arrange
		var commandType = typeof(CallServiceCommand);
		var constructors = commandType.GetConstructors();

		// Act & Assert
		constructors.Should().NotBeEmpty("because CallServiceCommand should have at least one constructor");
		constructors[0].GetParameters().Should().HaveCount(4, "because constructor should accept 4 parameters");
	}

	[Test]
	[Description("Verifies RequestBody property can store JSON strings")]
	public void CallServiceCommandOptions_RequestBody_CanStoreJson()
	{
		// Arrange
		var options = new CallServiceCommandOptions();
		var testJson = "{\"test\":\"value\",\"number\":123}";

		// Act
		options.RequestBody = testJson;

		// Assert
		options.RequestBody.Should().Be(testJson, "because RequestBody should store JSON strings");
	}

	[Test]
	[Description("Verifies RequestBody property can be null or empty")]
	public void CallServiceCommandOptions_RequestBody_CanBeNullOrEmpty()
	{
		// Arrange
		var options = new CallServiceCommandOptions();

		// Act & Assert
		options.RequestBody.Should().BeNull("because RequestBody should be null by default");
		
		options.RequestBody = string.Empty;
		options.RequestBody.Should().Be(string.Empty, "because RequestBody should allow empty string");
	}
}
