namespace Clio.Tests.YAML
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Reflection;
	using Clio.Command;
	using Clio.YAML;
	using CommandLine;
	using FluentAssertions;
	using NUnit.Framework;
	using OneOf;
	using OneOf.Types;
	using YamlDotNet.Serialization;

	[TestFixture]
	[Category("YAML")]
	internal class StepTests
	{

		private readonly Step _sut;
		private readonly RestartOptions _marker = new RestartOptions();
		private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();
		public StepTests()
		{
			_sut = new Step
			{
				Action = "restart",
				Description = "restart application",
				Options = new Dictionary<object, object> {
					   {"Environment","digitalads"}
				}
			};
		}
		
		[TestCase("restart")]
		[TestCase("restart-web-app")]
		public void FindTest_ShouldFindTypeByNameOrAlias(string searchName)
		{
			// Arrange
			Type[] allTypes = _marker.GetType().Assembly.GetTypes();
			
			// Act
			OneOf<Type, None> result = Step.FindOptionTypeByName(allTypes, searchName);
			if (result.Value is None type) {
				throw new NullReferenceException("Not a type");
			}
			object instance = Activator.CreateInstance(result.Value as Type);
				
			//Assert
			instance.Should().BeOfType<RestartOptions>();
		}

		
		[TestCaseSource(nameof(Source_For_ActivateOptions_ShouldReturn_Object))]
		public void ActivateOptions_ShouldReturn_Object 
			(IReadOnlyDictionary<object, object> optionValues, object expected) {
			
			//Arrange
			const string sampleFile = @"YAML/Yaml-Samples/steps.yaml";
			Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
			IReadOnlyDictionary<string, object> secrets = Scenario.ParseSecrets(scenario.Sections);
			IReadOnlyDictionary<string, object> config = Scenario.ParseSettings(scenario.Sections);
			
			// Act
			OneOf<Type, None> input = expected.GetType();
			OneOf<None, object> result = Step.ActivateOptions(input, optionValues,config, secrets);
			
			
			// Asert
			result.Should().NotBeNull();
			result.Value.Should().NotBeOfType<None>();
			result.Value.GetType().Should().Be(expected.GetType());
			
			
			var resultValue = result.Value;
			expected
				.GetType()
				.GetProperties()
				.Where(p=> 
					p.GetCustomAttribute<ValueAttribute>() is not null || 
					p.GetCustomAttribute<OptionAttribute>() is not null)
				.ToList()
				.ForEach(aP=> {
					var expectedValue = aP.GetValue(expected);
					var actualValue = resultValue.GetType().GetProperty(aP.Name).GetValue(resultValue);
					actualValue.Should().Be(expectedValue);
				});
		}
		
		static IEnumerable<object[]> Source_For_ActivateOptions_ShouldReturn_Object() {
			return new[] {
				new object[]{
					new Dictionary<object, object>() {
						{"Name","TestEnvironment"},
					},
					new RestartOptions() {
						Name = "TestEnvironment"
					}
				},
				new object[]{
					new Dictionary<object, object>() {
						{"Environment","TestEnvironment"}
					},
					new RestartOptions() {
						Environment = "TestEnvironment"
					}
				},
				new object[]{
					new Dictionary<object, object>() {
						{"e","TestEnvironment"}
					},
					new RestartOptions() {
						Environment = "TestEnvironment"
					}
				},
				new object[]{
					new Dictionary<object, object>() {
						{"u","https://work.creatio.com"},
						{"l","Supervisor"},
						{"p","Supervisor"},
						{"silent","true"},
						{"IsNetCore","true"}
					},
					new RestartOptions() {
						Uri = "https://work.creatio.com",
						Login = "Supervisor",
						Password = "Supervisor",
						IsSilent = true,
						IsNetCore = true
					}
				},
				new object[]{
					new Dictionary<object, object>() {
						{"DestinationPath","C:\\destination.zip"},
						{"SkipPdb","true"},
						{"Packages","Package1,Package2,Package3"},
						{"non-existent-option",""}
					},
					new GeneratePkgZipOptions() {
						DestinationPath = "C:\\destination.zip",
						SkipPdb = true,
						Packages = "Package1,Package2,Package3"
						
					}
				}
			};
		}
		
		[TestCase("aaa")]
		[TestCase("aaaaaaaaaaa")]
		public void FindTest_ShouldFindTypeByNameOrAlias_WhenNameIsIncorrect(string searchName)
		{
			// Arrange
			Type[] allTypes = _marker.GetType().Assembly.GetTypes();
			
			// Act
			OneOf<Type, None> result = Step.FindOptionTypeByName(allTypes, searchName);

			//Assert
			result.Value.Should().BeOfType<None>();
		}
		
		
		[Test]
		public void ActivateOptions_ShouldReturn_NotObject_When_Receiving_None() {
			
			//Arrange
			var options = new Dictionary<object, object>() {
				{"Name","TestEnvironment"},
			};
			const string sampleFile = @"YAML/Yaml-Samples/steps.yaml";
			Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
			IReadOnlyDictionary<string, object> secrets = Scenario.ParseSecrets(scenario.Sections);
			IReadOnlyDictionary<string, object> config = Scenario.ParseSettings(scenario.Sections);
			
			// Act
			OneOf<Type, None> input = new None();
			OneOf<None, object> result = Step.ActivateOptions(input, options, config, secrets);

			// Asert
			result.Should().NotBeNull();
			result.Value.Should().BeOfType<None>();
			
		}
		
		
		[Test]
		public void ActivateOptions_ShouldReturn_ActiveatedCommand_When_CommandUsesMacros() {
			
			//Arrange
			const string sampleFile = @"YAML/Yaml-Samples/steps_with_macro.yaml";
			Scenario scenario = (Scenario.CreateScenarioFromFile(sampleFile, _deserializer)).Value as Scenario;
			IReadOnlyDictionary<string, object> secrets = Scenario.ParseSecrets(scenario.Sections);
			IReadOnlyDictionary<string, object> settings = Scenario.ParseSettings(scenario.Sections);
			
			Dictionary<object, object> options = new () {
				{"Environment","{{settings.Environment.name}}"},
			};
			
			// Act
			OneOf<None, object> result = Step.ActivateOptions(typeof(RestartOptions), options, settings, secrets);
			
			// Asert
			result.Value.Should().BeOfType<RestartOptions>();
			
		}
	}
}
