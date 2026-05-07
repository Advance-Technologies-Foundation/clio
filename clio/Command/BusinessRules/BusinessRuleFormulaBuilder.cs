using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleFormulaBuilder {
	private static readonly Regex _supportedExpressionRegex = new(
		@"^(?:#\w+(?:\.\w+)*#|[\d\s+\-*/.()])*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly HashSet<string> _knownIdentifiers = new(StringComparer.OrdinalIgnoreCase) {
		"Blank",
		"false",
		"Self",
		"true"
	};

	internal static IReadOnlyList<BusinessRuleFormulaValidationContext> BuildValidationContexts(
		string entitySchemaName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule) {
		var contexts = new List<BusinessRuleFormulaValidationContext>();
		foreach (var action in rule.Actions) {
			if (!string.Equals(action.ActionType, BusinessRuleConstants.SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			foreach (var item in action.SetValueItems) {
				if (!IsFormulaExpression(item.Value)) {
					continue;
				}
				var targetPath = item.Expression.Path ?? string.Empty;
				var targetDescriptor = ResolveRequiredAttribute(attributeMap, targetPath,
					"rule.actions[*].items[*].expression.path");
				var formula = GetRequiredFormulaText(item.Value);
				contexts.Add(BuildValidationContext(entitySchemaName, attributeMap, targetPath, formula, targetDescriptor.DataValueTypeName));
			}
		}
		return contexts;
	}

	internal static BusinessRuleExpressionMetadataDto BuildValueExpression(
		string entitySchemaName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string targetPath,
		string formula,
		string targetDataValueTypeName) {
		var context = BuildFormulaContext(entitySchemaName, attributeMap, targetPath, formula, targetDataValueTypeName);
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleConstants.BusinessRuleFormulaExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = BusinessRuleConstants.FormulaExpressionType,
			ParameterMappings = [
				new BusinessRuleFormulaParameterMappingDto {
					ParameterName = context.IdParameterName,
					Expression = new BusinessRuleExpressionMetadataDto {
						TypeName = BusinessRuleConstants.BusinessRuleAttributeExpressionTypeName,
						UId = Guid.NewGuid().ToString(),
						DataValueTypeName = "Guid",
						Type = BusinessRuleConstants.AttributeValueExpressionType,
						Path = "Id"
					}
				},
				new BusinessRuleFormulaParameterMappingDto {
					ParameterName = context.FieldValuesParameterName,
					Expression = new BusinessRuleExpressionMetadataDto {
						TypeName = BusinessRuleConstants.BusinessRuleContextExpressionTypeName,
						UId = Guid.NewGuid().ToString()
					}
				}
			],
			ExpressionSchema = context.ExpressionSchema
		};
	}

	internal static BusinessRuleFormulaValidationContext BuildValidationContext(
		string entitySchemaName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string targetPath,
		string formula,
		string targetDataValueTypeName) {
		var context = BuildFormulaContext(entitySchemaName, attributeMap, targetPath, formula, targetDataValueTypeName);
		return new BusinessRuleFormulaValidationContext(
			targetPath,
			formula,
			new BusinessRuleExpressionValidationMetadataDto {
				EngineType = context.ExpressionSchema.EngineType,
				Expression = context.ExpressionSchema.Expression,
				ResultDataValueType = context.ExpressionSchema.ResultDataValueType,
				ExpressionVariables = [
					new BusinessRuleExpressionValidationVariableDto {
						Name = context.RecordVariableName,
						VariableType = "Record",
						DataValueType = "Lookup",
						Config = new BusinessRuleExpressionValidationRecordVariableConfigDto {
							Value = entitySchemaName,
							RecordType = "Entity",
							PrimaryValue = new BusinessRuleExpressionSchemaSourceValueConfigDto {
								Value = context.IdParameterName
							}
						}
					}
				],
				Parameters = [
					new BusinessRuleExpressionValidationParameterDto {
						Name = context.IdParameterName,
						DataValueType = "Guid"
					},
					new BusinessRuleExpressionValidationParameterDto {
						Name = context.FieldValuesParameterName,
						DataValueType = "Text"
					}
				]
			});
	}

	internal static IReadOnlyList<string> GetFormulaSourcePaths(
		string formula,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		var translation = TranslateAndValidateFormula(formula, "Record", attributeMap);
		return translation.SourcePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	internal static bool IsFormulaExpression(BusinessRuleExpression? expression) =>
		string.Equals(expression?.Type, BusinessRuleConstants.FormulaExpressionType, StringComparison.OrdinalIgnoreCase);

	internal static void ValidateFormulaScope(
		string formula,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string targetPath) =>
		TranslateAndValidateFormula(formula, "Record", attributeMap, targetPath);

	internal static string GetRequiredFormulaText(BusinessRuleExpression? expression) {
		if (string.IsNullOrWhiteSpace(expression?.Expression)) {
			throw new ArgumentException(
				"rule.actions[*].items[*].value.expression must be a non-empty string when value.type is 'Formula'.");
		}
		return expression.Expression;
	}

	private static BusinessRuleAttributeDescriptor ResolveRequiredAttribute(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string path,
		string fieldName) {
		if (!attributeMap.TryGetValue(path, out BusinessRuleAttributeDescriptor? descriptor)) {
			throw new ArgumentException($"Unknown attribute '{path}' in {fieldName}.");
		}
		return descriptor;
	}

	private static BusinessRuleFormulaContext BuildFormulaContext(
		string entitySchemaName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string targetPath,
		string formula,
		string targetDataValueTypeName) {
		var recordVariableName = $"{entitySchemaName}Record";
		var idParameterName = $"{entitySchemaName}IdParameter";
		var fieldValuesParameterName = $"{entitySchemaName}fieldValuesParameter";
		var translation = TranslateAndValidateFormula(formula, recordVariableName, attributeMap, targetPath);
		var expressionSchema = new BusinessRuleExpressionSchemaDto {
			Expression = translation.Expression,
			ResultDataValueType = targetDataValueTypeName,
			ExpressionVariables = [
				new BusinessRuleExpressionSchemaVariableDto {
					Name = recordVariableName,
					VariableType = "Record",
					DataValueType = "Lookup",
					Config = new BusinessRuleExpressionSchemaRecordVariableConfigDto {
						Value = entitySchemaName,
						RecordType = "Entity",
						PrimaryValue = new BusinessRuleExpressionSchemaSourceValueConfigDto {
							Value = idParameterName
						},
						FieldValues = new BusinessRuleExpressionSchemaSourceValueConfigDto {
							Value = fieldValuesParameterName
						}
					}
				}
			],
			Parameters = [
				new BusinessRuleExpressionSchemaParameterDto {
					Name = idParameterName,
					DataValueType = "Guid"
				},
				new BusinessRuleExpressionSchemaParameterDto {
					Name = fieldValuesParameterName,
					DataValueType = "Text"
				}
			]
		};
		return new BusinessRuleFormulaContext(
			recordVariableName,
			idParameterName,
			fieldValuesParameterName,
			expressionSchema);
	}

	private static BusinessRuleFormulaTranslation TranslateAndValidateFormula(
		string formula,
		string recordVariableName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string? targetPath = null) {
		var attributesByName = BuildAttributesByName(attributeMap);
		var sourcePaths = new List<string>();
		var expression = TranslateFormula(formula, recordVariableName, attributesByName, sourcePaths);
		ValidateSupportedExpressionSyntax(expression);
		if (sourcePaths.Count == 0) {
			throw new ArgumentException(targetPath is null
				? "Formula must reference at least one entity attribute."
				: $"Formula for '{targetPath}' must reference at least one entity attribute.");
		}
		return new BusinessRuleFormulaTranslation(expression, sourcePaths);
	}

	private static string TranslateFormula(
		string formula,
		string recordVariableName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributesByName,
		ICollection<string> sourcePaths) =>
		BuildTranslatedFormula(formula, recordVariableName, attributesByName, sourcePaths);

	private static string BuildTranslatedFormula(
		string formula,
		string recordVariableName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributesByName,
		ICollection<string> sourcePaths) {
		var result = new StringBuilder(formula.Length);
		for (var index = 0; index < formula.Length;) {
			AppendTranslatedFormulaPart(formula, recordVariableName, attributesByName, sourcePaths, result, ref index);
		}
		return result.ToString();
	}

	private static void AppendTranslatedFormulaPart(
		string formula,
		string recordVariableName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributesByName,
		ICollection<string> sourcePaths,
		StringBuilder result,
		ref int index) {
		var current = formula[index];
		if (current == '"') {
			CopyStringLiteral(formula, result, ref index);
			return;
		}
		if (IsIdentifierStart(current)) {
			AppendTranslatedIdentifier(formula, recordVariableName, attributesByName, sourcePaths, result, ref index);
			return;
		}
		result.Append(current);
		index++;
	}

	private static void AppendTranslatedIdentifier(
		string formula,
		string recordVariableName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributesByName,
		ICollection<string> sourcePaths,
		StringBuilder result,
		ref int index) {
		var identifier = ReadIdentifier(formula, ref index);
		if (TryAppendAttributeReference(identifier, recordVariableName, attributesByName, sourcePaths, result)) {
			return;
		}
		if (_knownIdentifiers.Contains(identifier)) {
			result.Append(identifier);
			return;
		}
		ThrowUnsupportedIdentifier(formula, index, identifier);
	}

	private static string ReadIdentifier(string formula, ref int index) {
		var start = index;
		index++;
		while (index < formula.Length && IsIdentifierPart(formula[index])) {
			index++;
		}
		return formula[start..index];
	}

	private static bool TryAppendAttributeReference(
		string identifier,
		string recordVariableName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributesByName,
		ICollection<string> sourcePaths,
		StringBuilder result) {
		if (!attributesByName.TryGetValue(identifier, out var descriptor)) {
			return false;
		}
		result.Append('#').Append(recordVariableName).Append('.').Append(descriptor.Path).Append('#');
		sourcePaths.Add(descriptor.Path);
		return true;
	}

	private static void ThrowUnsupportedIdentifier(string formula, int index, string identifier) {
		if (IsFunctionCall(formula, index)) {
			throw new ArgumentException(
				$"Formula functions are not supported in rule.actions[*].items[*].value.expression. Use a simple direct-field expression instead of '{identifier}(...)'.");
		}
		throw new ArgumentException(
			$"Unknown attribute '{identifier}' in rule.actions[*].items[*].value.expression formula.");
	}

	private static void ValidateSupportedExpressionSyntax(string expression) {
		if (_supportedExpressionRegex.IsMatch(expression)) {
			return;
		}
		throw new ArgumentException(
			"Formula expression supports only direct entity fields, numbers, arithmetic operators (+, -, *, /), dots, parentheses, and whitespace.");
	}

	private static void CopyStringLiteral(string formula, StringBuilder result, ref int index) {
		result.Append(formula[index]);
		index++;
		while (index < formula.Length) {
			var current = formula[index];
			result.Append(current);
			index++;
			if (current != '"') {
				continue;
			}
			if (index < formula.Length && formula[index] == '"') {
				result.Append(formula[index]);
				index++;
				continue;
			}
			break;
		}
	}

	private static IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> BuildAttributesByName(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		var attributesByName = new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in attributeMap) {
			if (string.IsNullOrWhiteSpace(pair.Key)) {
				continue;
			}
			attributesByName.TryAdd(pair.Key, pair.Value);
		}
		return attributesByName;
	}

	private static bool IsFunctionCall(string formula, int index) {
		while (index < formula.Length && char.IsWhiteSpace(formula[index])) {
			index++;
		}
		return index < formula.Length && formula[index] == '(';
	}

	private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

	private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';

	private sealed record BusinessRuleFormulaContext(
		string RecordVariableName,
		string IdParameterName,
		string FieldValuesParameterName,
		BusinessRuleExpressionSchemaDto ExpressionSchema);

	private sealed record BusinessRuleFormulaTranslation(
		string Expression,
		IReadOnlyList<string> SourcePaths);
}

