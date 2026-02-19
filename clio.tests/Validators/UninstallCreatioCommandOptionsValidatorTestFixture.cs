using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Tests.Command;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NUnit.Framework;

namespace Clio.Tests.Validators;

[TestFixture(Category = "UnitTests")]
public class UninstallCreatioCommandOptionsValidatorTestFixture : BaseClioModuleTests
{

	private UninstallCreatioCommandOptionsValidator _sut;

	public override void Setup(){
		base.Setup();
		_sut = Container.GetRequiredService<UninstallCreatioCommandOptionsValidator>();
	}

	public static IEnumerable<TestCaseData> EnvironmentNameIsEmptyTestCases {
		get
		{
			yield return new TestCaseData(new Tuple<UninstallCreatioCommandOptions,ValidationFailure>( 
				new UninstallCreatioCommandOptions(), 
				new ValidationFailure {
					ErrorCode = "ArgumentParse.Error",
					ErrorMessage = "Either path to creatio directory or environment name must be provided",
					Severity = Severity.Error,
				}))
				.SetName("Returns Error when PhysicalPath and EnvironmentName IsEmpty")
				.SetDescription("Tests that the validator returns an error when both the EnvironmentName and PhysicalPath is empty.");
			
			yield return new TestCaseData(new Tuple<UninstallCreatioCommandOptions,ValidationFailure>( 
				new UninstallCreatioCommandOptions {
					PhysicalPath = string.Empty
				}, 
				new ValidationFailure {
					ErrorCode = "ArgumentParse.Error",
					ErrorMessage = "Either path to creatio directory or environment name must be provided",
					Severity = Severity.Error,
				}))
				.SetName("Returns Error when PhysicalPath IsEmpty")
				.SetDescription("Tests that the validator returns an error when the PhysicalPath is empty.");
				
			yield return new TestCaseData(new Tuple<UninstallCreatioCommandOptions,ValidationFailure>( 
					new UninstallCreatioCommandOptions {
						EnvironmentName = string.Empty
					}, 
					new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "Either path to creatio directory or environment name must be provided",
						Severity = Severity.Error,
					}))
				.SetName("Returns Error when EnvironmentName IsEmpty")
				.SetDescription("Tests that the validator returns an error when the EnvironmentName is empty.");
			
			yield return new TestCaseData(new Tuple<UninstallCreatioCommandOptions,ValidationFailure>( 
					new UninstallCreatioCommandOptions {
						EnvironmentName = "env1",
						PhysicalPath = @"some value"
					}, 
					new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "Either environment name or path to creatio directory must be provided, not both",
						Severity = Severity.Error,
					}))
				.SetName("Returns Error when EnvironmentName and PhysicalPath AreNotEmpty")
				.SetDescription("""
								Tests that the validator returns an error when 
								the EnvironmentName is not empty and PhysicalPath is empty.
								""");
			
			yield return new TestCaseData(new Tuple<UninstallCreatioCommandOptions,ValidationFailure>( 
					new UninstallCreatioCommandOptions {
						PhysicalPath = @"some value"
					}, new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "PhysicalPath must be a valid directory path",
						Severity = Severity.Error,
						AttemptedValue = "some value"
					}))
				.SetName("Returns Error when PhysicalPath IsNot a valid file URI")
				.SetDescription("""
								Tests that the validator returns an error when 
								the PhysicalPath is not a valid file URI.
								""");
			
			yield return new TestCaseData(new Tuple<UninstallCreatioCommandOptions,ValidationFailure>( 
					new UninstallCreatioCommandOptions {
						PhysicalPath = @"https://google.ca"
					}, 
					new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "PhysicalPath must be a valid directory path",
						Severity = Severity.Error,
						AttemptedValue = @"https://google.ca"
					}))
				.SetName("Returns Error when PhysicalPath IsNot a valid file URI")
				.SetDescription("""
								Tests that the validator returns an error when 
								the PhysicalPath is not a valid file URI.
								""");
			
			yield return new TestCaseData(new Tuple<UninstallCreatioCommandOptions,ValidationFailure>( 
					new UninstallCreatioCommandOptions {
						PhysicalPath = @"C:\inetpub\"
					}, new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "PhysicalPath must be a valid directory path to an Existing directory",
						Severity = Severity.Error,
						AttemptedValue = @"C:\inetpub\"
					}))
				.SetName("Returns Error when PhysicalPath does not exist in FS")
				.SetDescription("""
								Tests that the validator returns an error when 
								the PhysicalPath points to non existent directory.
								""");
		}
	}
	[Test, TestCaseSource(nameof(EnvironmentNameIsEmptyTestCases))]
	public void Validate_ShouldReturnError(Tuple<UninstallCreatioCommandOptions,ValidationFailure> testCase)
	{
		//Act
		ValidationResult validationResult = _sut.Validate(testCase.Item1);

		//Assert
		validationResult.Errors.First().Should().BeEquivalentTo(testCase.Item2);
	}
	
	[Test]
	public void Validate_ShouldNotReturnError_When_EnvNotEmpty()
	{
		//Arrange
		UninstallCreatioCommandOptions opts = new () {
			EnvironmentName = "some_env"
		};
		
		//Act
		ValidationResult validationResult = _sut.Validate(opts);

		//Assert
		validationResult.Errors.Should().HaveCount(0);
	}
	[Test]
	public void Validate_ShouldNotReturnError_When_PhysicalPathValid()
	{
		//Arrange
		const string dirName = @"C:\inetpup\wwwroot\";
		FileSystem.AddDirectory(dirName);
		
		UninstallCreatioCommandOptions opts = new () {
			PhysicalPath = dirName
		};
		
		//Act
		ValidationResult validationResult = _sut.Validate(opts);

		//Assert
		validationResult.Errors.Should().HaveCount(0);
	}

	
}