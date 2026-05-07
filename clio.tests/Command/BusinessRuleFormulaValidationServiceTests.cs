using System;
using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleFormulaValidationServiceTests {
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private BusinessRuleFormulaValidationService _service = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("ServiceModel/ExpressionService.svc/Validate")
			.Returns("https://creatio.example/ServiceModel/ExpressionService.svc/Validate");
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("[]");
		_service = new BusinessRuleFormulaValidationService(_applicationClient, _serviceUrlBuilder);
	}

	[Test]
	[Category("Unit")]
	[Description("Posts a stringified expression metadata payload to the Creatio expression validation endpoint.")]
	public void Validate_Should_Post_Stringified_Metadata_To_Expression_Service() {
		// Arrange
		BusinessRuleFormulaValidationContext context = CreateContext();

		// Act
		_service.Validate(context);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"https://creatio.example/ServiceModel/ExpressionService.svc/Validate",
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		string request = (string)_applicationClient.ReceivedCalls()
			.Single(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.GetArguments()[1]!;
		using JsonDocument requestDocument = JsonDocument.Parse(request);
		string metadata = requestDocument.RootElement.GetProperty("metadata").GetString()!;
		using JsonDocument metadataDocument = JsonDocument.Parse(metadata);
		metadataDocument.RootElement.GetProperty("engineType").GetString().Should().Be("PowerFx",
			because: "the remote service validates PowerFx execution metadata");
		metadataDocument.RootElement.GetProperty("expression").GetString().Should().Be("#UsrOrderRecord.BaseScore# + #UsrOrderRecord.BonusScore#",
			because: "validation should run after agent-friendly field names are translated to expression-schema references");
		metadataDocument.RootElement.GetProperty("resultDataValueType").GetString().Should().Be("Integer",
			because: "the remote validator needs the target result type");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws a validation error that includes the target path formula and remote diagnostic range.")]
	public void Validate_Should_Throw_When_Expression_Service_Returns_Errors() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("[{\"message\":\"Unexpected token\",\"from\":3,\"to\":4}]");
		BusinessRuleFormulaValidationContext context = CreateContext();

		// Act
		Action act = () => _service.Validate(context);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula validation failed for 'TotalScore' ('BaseScore + BonusScore'): Unexpected token [3-4]",
				because: "remote syntax errors should stop business-rule creation with actionable diagnostics");
	}

	private static BusinessRuleFormulaValidationContext CreateContext() =>
		new(
			"TotalScore",
			"BaseScore + BonusScore",
			new BusinessRuleExpressionValidationMetadataDto {
				EngineType = "PowerFx",
				Expression = "#UsrOrderRecord.BaseScore# + #UsrOrderRecord.BonusScore#",
				ResultDataValueType = "Integer",
				Parameters = [
					new BusinessRuleExpressionValidationParameterDto {
						Name = "UsrOrderIdParameter",
						DataValueType = "Guid"
					},
					new BusinessRuleExpressionValidationParameterDto {
						Name = "UsrOrderfieldValuesParameter",
						DataValueType = "Text"
					}
				],
				ExpressionVariables = [
					new BusinessRuleExpressionValidationVariableDto {
						Name = "UsrOrderRecord",
						VariableType = "Record",
						DataValueType = "Lookup",
						Config = new BusinessRuleExpressionValidationRecordVariableConfigDto {
							Value = "UsrOrder",
							RecordType = "Entity",
							PrimaryValue = new BusinessRuleExpressionSchemaSourceValueConfigDto {
								Value = "UsrOrderIdParameter"
							}
						}
					}
				]
			});
}
