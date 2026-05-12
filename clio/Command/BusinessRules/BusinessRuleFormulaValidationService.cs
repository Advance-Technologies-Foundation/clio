using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.BusinessRules;

internal interface IBusinessRuleFormulaValidationService {
	void Validate(BusinessRuleFormulaValidationContext context);
}

internal sealed class BusinessRuleFormulaValidationService : IBusinessRuleFormulaValidationService {
	private const string ExpressionServiceValidateEndpoint = "ServiceModel/ExpressionService.svc/Validate";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	public BusinessRuleFormulaValidationService(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	public void Validate(BusinessRuleFormulaValidationContext context) {
		var metadata = JsonSerializer.Serialize(context.Metadata, BusinessRuleConstants.JsonOptions);
		var request = new BusinessRuleFormulaValidationRequestDto {
			Metadata = metadata
		};
		var response = _applicationClient.ExecutePostRequest(
			_serviceUrlBuilder.Build(ExpressionServiceValidateEndpoint),
			JsonSerializer.Serialize(request, BusinessRuleConstants.JsonOptions));
		var errors = JsonSerializer.Deserialize<List<BusinessRuleFormulaValidationErrorDto>>(
			response,
			BusinessRuleConstants.JsonOptions) ?? [];
		if (errors.Count == 0) {
			return;
		}
		var message = string.Join("; ", errors.Select(error => {
			var range = error.From is null || error.To is null ? string.Empty : $" [{error.From}-{error.To}]";
			return $"{error.Message}{range}";
		}));
		throw new ArgumentException(
			$"Formula validation failed for '{context.TargetPath}' ('{context.Formula}'): {message}");
	}
}

internal sealed record BusinessRuleFormulaValidationContext(
	string TargetPath,
	string Formula,
	BusinessRuleExpressionValidationMetadataDto Metadata);

internal sealed class BusinessRuleFormulaValidationRequestDto {
	[JsonPropertyName("metadata")]
	public string Metadata { get; set; } = string.Empty;
}

internal sealed class BusinessRuleFormulaValidationErrorDto {
	[JsonPropertyName("message")]
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("from")]
	public int? From { get; set; }

	[JsonPropertyName("to")]
	public int? To { get; set; }
}

internal sealed class BusinessRuleExpressionValidationMetadataDto {
	[JsonPropertyName("engineType")]
	public string EngineType { get; set; } = "PowerFx";

	[JsonPropertyName("expression")]
	public string Expression { get; set; } = string.Empty;

	[JsonPropertyName("resultDataValueType")]
	public string ResultDataValueType { get; set; } = string.Empty;

	[JsonPropertyName("parameters")]
	public List<BusinessRuleExpressionValidationParameterDto> Parameters { get; set; } = [];

	[JsonPropertyName("expressionVariables")]
	public List<BusinessRuleExpressionValidationVariableDto> ExpressionVariables { get; set; } = [];
}

internal sealed class BusinessRuleExpressionValidationParameterDto {
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("dataValueType")]
	public string DataValueType { get; set; } = string.Empty;

	[JsonPropertyName("value")]
	public object? Value { get; set; }
}

internal sealed class BusinessRuleExpressionValidationVariableDto {
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("variableType")]
	public string VariableType { get; set; } = "Record";

	[JsonPropertyName("dataValueType")]
	public string DataValueType { get; set; } = "Lookup";

	[JsonPropertyName("config")]
	public BusinessRuleExpressionValidationRecordVariableConfigDto? Config { get; set; }
}

internal sealed class BusinessRuleExpressionValidationRecordVariableConfigDto {
	[JsonPropertyName("value")]
	public string Value { get; set; } = string.Empty;

	[JsonPropertyName("recordType")]
	public string RecordType { get; set; } = "Entity";

	[JsonPropertyName("primaryValue")]
	public BusinessRuleExpressionSchemaSourceValueConfigDto? PrimaryValue { get; set; }
}
