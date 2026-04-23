namespace Clio.Command;

using System;
using System.Collections.Generic;
using McpServer.Resources;

/// <summary>
/// Validates the structural contract of the <c>SCHEMA_HANDLERS</c> section.
/// </summary>
internal static class SchemaHandlerValidationService
{
	private const string RequestPropertyName = "request";
	private const string HandlerPropertyName = "handler";

	/// <summary>
	/// Validates handler entries in the supplied Freedom UI page body.
	/// </summary>
	/// <param name="jsBody">Raw JavaScript body of a Freedom UI page schema.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> describing the first invalid handler shape,
	/// or a passing result when the handlers section stays structurally valid.
	/// </returns>
	internal static SchemaValidationResult Validate(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string handlersContent, SchemaValidationService.SchemaHandlersMarker)) {
			return result;
		}

		string trimmedContent = handlersContent.Trim();
		if (string.IsNullOrWhiteSpace(trimmedContent) ||
		    !trimmedContent.StartsWith("[", StringComparison.Ordinal) ||
		    !trimmedContent.EndsWith("]", StringComparison.Ordinal)) {
			result.IsValid = false;
			result.Errors.Add($"Invalid handler section in {SchemaValidationService.SchemaHandlersMarker}: section must remain an array literal.");
			return result;
		}

		SchemaValidationResult syntaxResult = SchemaValidationService.ValidateJsSyntax($"const __clioHandlers = {trimmedContent};");
		if (!syntaxResult.IsValid) {
			result.IsValid = false;
			result.Errors.Add($"Invalid handler section in {SchemaValidationService.SchemaHandlersMarker}: {string.Join("; ", syntaxResult.Errors)}");
			return result;
		}

		ValidateHandlerEntries(trimmedContent, result);
		return result;
	}

	private static void ValidateHandlerEntries(string handlersContent, SchemaValidationResult result) {
		int index = 1;
		int entryIndex = 0;
		while (TryAdvanceToNextMeaningfulCharacter(handlersContent, ref index)) {
			if (index >= handlersContent.Length || handlersContent[index] == ']') {
				return;
			}

			if (handlersContent[index] != '{') {
				result.IsValid = false;
				result.Errors.Add(
					$"Handler entry at index {entryIndex} in {SchemaValidationService.SchemaHandlersMarker} must be an object literal.");
				return;
			}

			if (!TryExtractBalancedJavaScriptObject(handlersContent, index, out string handlerBody, out int nextIndex)) {
				result.IsValid = false;
				result.Errors.Add(
					$"Handler entry at index {entryIndex} in {SchemaValidationService.SchemaHandlersMarker} contains unbalanced JavaScript object syntax.");
				return;
			}

			ValidateHandlerEntry(handlerBody, entryIndex, result);
			if (!result.IsValid) {
				return;
			}

			index = nextIndex;
			entryIndex++;
		}
	}

	private static void ValidateHandlerEntry(string handlerBody, int entryIndex, SchemaValidationResult result) {
		HashSet<string> propertyNames = ExtractTopLevelObjectPropertyNames(handlerBody);
		if (!propertyNames.Contains(RequestPropertyName)) {
			result.IsValid = false;
			result.Errors.Add(
				$"Handler entry at index {entryIndex} in {SchemaValidationService.SchemaHandlersMarker} must declare a string '{RequestPropertyName}' property.");
			return;
		}

		if (!propertyNames.Contains(HandlerPropertyName)) {
			result.IsValid = false;
			result.Errors.Add(
				$"Handler entry at index {entryIndex} in {SchemaValidationService.SchemaHandlersMarker} must declare a '{HandlerPropertyName}' property.");
			return;
		}

		if (!TryGetTopLevelPropertyValueExpression(handlerBody, RequestPropertyName, out string requestExpression) ||
		    !IsStringLiteral(requestExpression) ||
		    !TryUnquoteStringLiteral(requestExpression, out string requestType) ||
		    string.IsNullOrWhiteSpace(requestType)) {
			result.IsValid = false;
			result.Errors.Add(
				$"Handler entry at index {entryIndex} in {SchemaValidationService.SchemaHandlersMarker} must declare a string '{RequestPropertyName}' property.");
			return;
		}

		if (!TryGetTopLevelPropertyValueExpression(handlerBody, HandlerPropertyName, out string handlerExpression) ||
		    !IsCallableHandlerExpression(handlerExpression)) {
			result.IsValid = false;
			result.Errors.Add(
				$"Handler entry at index {entryIndex} in {SchemaValidationService.SchemaHandlersMarker} must declare a callable '{HandlerPropertyName}' property.");
		}
	}

	private static bool TryAdvanceToNextMeaningfulCharacter(string content, ref int index) {
		while (index < content.Length) {
			char current = content[index];
			if (char.IsWhiteSpace(current) || current == ',') {
				index++;
				continue;
			}

			if (current == '/' && index + 1 < content.Length) {
				if (content[index + 1] == '/') {
					index = SkipLineComment(content, index, content.Length);
					continue;
				}
				if (content[index + 1] == '*') {
					index = SkipBlockCommentWithoutValidation(content, index, content.Length);
					continue;
				}
			}

			return true;
		}

		return false;
	}

	private static int SkipBlockCommentWithoutValidation(string content, int index, int length) {
		index += 2;
		while (index + 1 < length) {
			if (content[index] == '*' && content[index + 1] == '/') {
				return index + 2;
			}
			index++;
		}
		return length;
	}

	private static bool TryExtractBalancedJavaScriptObject(
		string content,
		int braceStart,
		out string objectContent,
		out int nextIndex) {
		int depth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = braceStart;
		while (index < content.Length) {
			if (inString) {
				index = ConsumeStringLiteralCharacter(content, index, ref inString, stringChar);
				continue;
			}

			if (content[index] == '/' && index + 1 < content.Length) {
				if (content[index + 1] == '/') {
					index = SkipLineComment(content, index, content.Length);
					continue;
				}
				if (content[index + 1] == '*') {
					index = SkipBlockCommentWithoutValidation(content, index, content.Length);
					continue;
				}
			}

			char current = content[index];
			if (current is '"' or '\'' or '`') {
				inString = true;
				stringChar = current;
				index++;
				continue;
			}

			if (current == '{') {
				depth++;
			} else if (current == '}') {
				depth--;
				if (depth == 0) {
					objectContent = content.Substring(braceStart, index - braceStart + 1);
					nextIndex = index + 1;
					return true;
				}
			}

			index++;
		}

		objectContent = string.Empty;
		nextIndex = braceStart;
		return false;
	}

	private static bool TryGetTopLevelPropertyValueExpression(
		string objectContent,
		string propertyName,
		out string expression) {
		expression = string.Empty;
		if (string.IsNullOrWhiteSpace(objectContent) || objectContent[0] != '{') {
			return false;
		}

		int depth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = 0;
		while (index < objectContent.Length) {
			if (TryConsumeStringLiteralCharacter(objectContent, ref index, ref inString, stringChar)) {
				continue;
			}

			if (TrySkipTriviaAndComments(objectContent, ref index)) {
				continue;
			}

			char current = objectContent[index];
			if (depth == 1 &&
			    TryReadPropertyName(objectContent, index, current, out string candidatePropertyName, out int valueStartIndex) &&
			    string.Equals(candidatePropertyName, propertyName, StringComparison.Ordinal)) {
				return TryReadTopLevelValueExpression(objectContent, valueStartIndex, out expression);
			}

			if (TryHandleStructuralCharacter(current, ref index, ref depth, ref inString, ref stringChar)) {
				continue;
			}

			index++;
		}

		return false;
	}

	private static bool TrySkipTriviaAndComments(string content, ref int index) {
		if (index >= content.Length) {
			return false;
		}

		if (char.IsWhiteSpace(content[index])) {
			index++;
			return true;
		}

		if (content[index] == '/' && index + 1 < content.Length) {
			if (content[index + 1] == '/') {
				index = SkipLineComment(content, index, content.Length);
				return true;
			}

			if (content[index + 1] == '*') {
				index = SkipBlockCommentWithoutValidation(content, index, content.Length);
				return true;
			}
		}

		return false;
	}

	private static bool TryReadPropertyName(
		string content,
		int startIndex,
		char current,
		out string propertyName,
		out int valueStartIndex) {
		propertyName = string.Empty;
		valueStartIndex = startIndex;

		if (current is '"' or '\'' or '`') {
			return TryReadQuotedMethodShorthandProperty(content, startIndex, current, out propertyName, out valueStartIndex) ||
			       TryReadQuotedPropertyName(content, startIndex, current, out propertyName, out valueStartIndex);
		}

		if (IsIdentifierStart(current)) {
			return TryReadMethodShorthandProperty(content, startIndex, current, out propertyName, out valueStartIndex) ||
			       TryReadIdentifierPropertyName(content, startIndex, out propertyName, out valueStartIndex);
		}

		return false;
	}

	private static bool TryReadTopLevelValueExpression(
		string content,
		int valueStartIndex,
		out string expression) {
		int startIndex = SkipWhitespace(content, valueStartIndex);
		int index = startIndex;
		int braceDepth = 0;
		int bracketDepth = 0;
		int parenDepth = 0;
		bool inString = false;
		char stringChar = '"';

		while (index < content.Length) {
			if (TryConsumeStringLiteralCharacter(content, ref index, ref inString, stringChar)) {
				continue;
			}

			if (TrySkipTriviaAndComments(content, ref index)) {
				continue;
			}

			char current = content[index];
			if (current is '"' or '\'' or '`') {
				inString = true;
				stringChar = current;
				index++;
				continue;
			}

			switch (current) {
				case '{':
					braceDepth++;
					index++;
					continue;
				case '}':
					if (braceDepth == 0 && bracketDepth == 0 && parenDepth == 0) {
						expression = content[startIndex..index].TrimEnd();
						return !string.IsNullOrWhiteSpace(expression);
					}
					braceDepth--;
					index++;
					continue;
				case '[':
					bracketDepth++;
					index++;
					continue;
				case ']':
					bracketDepth--;
					index++;
					continue;
				case '(':
					parenDepth++;
					index++;
					continue;
				case ')':
					parenDepth--;
					index++;
					continue;
				case ',':
					if (braceDepth == 0 && bracketDepth == 0 && parenDepth == 0) {
						expression = content[startIndex..index].TrimEnd();
						return !string.IsNullOrWhiteSpace(expression);
					}
					index++;
					continue;
				default:
					index++;
					continue;
			}
		}

		expression = content[startIndex..].TrimEnd();
		return !string.IsNullOrWhiteSpace(expression);
	}

	private static bool IsStringLiteral(string expression) {
		string trimmed = expression.Trim();
		return trimmed.Length >= 2 &&
		       ((trimmed[0] == '"' && trimmed[^1] == '"') ||
		        (trimmed[0] == '\'' && trimmed[^1] == '\'') ||
		        (trimmed[0] == '`' && trimmed[^1] == '`' && !trimmed.Contains("${", StringComparison.Ordinal)));
	}

	private static bool TryUnquoteStringLiteral(string expression, out string value) {
		string trimmed = expression.Trim();
		if (!IsStringLiteral(trimmed)) {
			value = string.Empty;
			return false;
		}

		value = trimmed[1..^1];
		return true;
	}

	private static bool IsCallableHandlerExpression(string expression) {
		string trimmed = expression.Trim();
		if (string.IsNullOrWhiteSpace(trimmed)) {
			return false;
		}

		if (IsStringLiteral(trimmed)) {
			return false;
		}

		if (trimmed.StartsWith("async ", StringComparison.Ordinal)) {
			trimmed = trimmed["async ".Length..].TrimStart();
		}

		return trimmed.StartsWith("function", StringComparison.Ordinal)
			|| LooksLikeMethodShorthandExpression(trimmed)
			|| ContainsTopLevelArrowToken(trimmed);
	}

	private static bool LooksLikeMethodShorthandExpression(string expression) {
		if (string.IsNullOrWhiteSpace(expression)) {
			return false;
		}

		int index = SkipWhitespace(expression, 0);
		if (index >= expression.Length) {
			return false;
		}

		char current = expression[index];
		if (current is '"' or '\'' or '`') {
			return TryReadQuotedMethodShorthandProperty(expression, index, current, out _, out _);
		}

		return IsIdentifierStart(current) &&
		       TryReadMethodShorthandProperty(expression, index, current, out _, out _);
	}

	private static bool ContainsTopLevelArrowToken(string expression) {
		int braceDepth = 0;
		int bracketDepth = 0;
		int parenDepth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = 0;

		while (index < expression.Length) {
			if (TryConsumeStringLiteralCharacter(expression, ref index, ref inString, stringChar)) {
				continue;
			}

			if (TrySkipTriviaAndComments(expression, ref index)) {
				continue;
			}

			char current = expression[index];
			if (TrySkipRegexLiteral(expression, ref index)) {
				continue;
			}

			if (current is '"' or '\'' or '`') {
				inString = true;
				stringChar = current;
				index++;
				continue;
			}

			if (current == '{') {
				braceDepth++;
				index++;
				continue;
			}

			if (current == '}') {
				braceDepth--;
				index++;
				continue;
			}

			if (current == '[') {
				bracketDepth++;
				index++;
				continue;
			}

			if (current == ']') {
				bracketDepth--;
				index++;
				continue;
			}

			if (current == '(') {
				parenDepth++;
				index++;
				continue;
			}

			if (current == ')') {
				parenDepth--;
				index++;
				continue;
			}

			if (current == '=' &&
			    index + 1 < expression.Length &&
			    expression[index + 1] == '>' &&
			    braceDepth == 0 &&
			    bracketDepth == 0 &&
			    parenDepth == 0) {
				return true;
			}

			index++;
		}

		return false;
	}

	private static bool TrySkipRegexLiteral(string expression, ref int index) {
		if (!LooksLikeRegexLiteralStart(expression, index)) {
			return false;
		}

		index = SkipRegexLiteral(expression, index);
		return true;
	}

	private static bool LooksLikeRegexLiteralStart(string expression, int index) {
		if (expression[index] != '/' || index + 1 >= expression.Length) {
			return false;
		}

		char next = expression[index + 1];
		if (next is '/' or '*') {
			return false;
		}

		int previousIndex = index - 1;
		while (previousIndex >= 0 && char.IsWhiteSpace(expression[previousIndex])) {
			previousIndex--;
		}

		if (previousIndex < 0) {
			return true;
		}

		char previous = expression[previousIndex];
		return previous is '(' or '[' or '{' or ',' or ':' or ';' or '=' or '!' or '?' or '&' or '|' or '^' or '~' or '+' or '-' or '*' or '%' or '<' or '>';
	}

	private static int SkipRegexLiteral(string expression, int index) {
		index++;
		bool inCharClass = false;

		while (index < expression.Length) {
			if (expression[index] == '\\') {
				index = index + 1 < expression.Length ? index + 2 : index + 1;
				continue;
			}

			if (expression[index] == '[') {
				inCharClass = true;
				index++;
				continue;
			}

			if (expression[index] == ']' && inCharClass) {
				inCharClass = false;
				index++;
				continue;
			}

			if (expression[index] == '/' && !inCharClass) {
				index++;
				break;
			}

			index++;
		}

		while (index < expression.Length && IsIdentifierPart(expression[index])) {
			index++;
		}

		return index;
	}

	private static HashSet<string> ExtractTopLevelObjectPropertyNames(string objectContent) {
		var props = new HashSet<string>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(objectContent) || objectContent[0] != '{') {
			return props;
		}

		int depth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = 0;
		while (index < objectContent.Length) {
			if (TryConsumeStringLiteralCharacter(objectContent, ref index, ref inString, stringChar)) {
				continue;
			}

			char current = objectContent[index];
			if (TryReadTopLevelPropertyName(objectContent, depth, current, ref index, props)) {
				continue;
			}

			if (TryHandleStructuralCharacter(current, ref index, ref depth, ref inString, ref stringChar)) {
				continue;
			}

			index++;
		}

		return props;
	}

	private static bool TryConsumeStringLiteralCharacter(
		string content,
		ref int index,
		ref bool inString,
		char stringChar) {
		if (!inString) {
			return false;
		}

		index = ConsumeStringLiteralCharacter(content, index, ref inString, stringChar);
		return true;
	}

	private static bool TryReadTopLevelPropertyName(
		string content,
		int depth,
		char current,
		ref int index,
		HashSet<string> props) {
		if (current is '"' or '\'' or '`' &&
		    depth == 1 &&
		    TryReadQuotedMethodShorthandPropertyName(content, index, current, out string methodPropertyName, out int methodNextIndex)) {
			props.Add(methodPropertyName);
			index = methodNextIndex;
			return true;
		}

		if (current is '"' or '\'' or '`' &&
		    depth == 1 &&
		    TryReadQuotedPropertyName(content, index, current, out string propertyName, out int nextIndex)) {
			props.Add(propertyName);
			index = nextIndex;
			return true;
		}

		if (depth == 1 &&
		    IsIdentifierStart(current) &&
		    TryReadMethodShorthandPropertyName(content, index, current, out string methodIdentifierName, out int methodIdentifierNextIndex)) {
			props.Add(methodIdentifierName);
			index = methodIdentifierNextIndex;
			return true;
		}

		if (depth == 1 &&
		    IsIdentifierStart(current) &&
		    TryReadIdentifierPropertyName(content, index, out string identifierName, out int identifierNextIndex)) {
			props.Add(identifierName);
			index = identifierNextIndex;
			return true;
		}

		return false;
	}

	private static bool TryHandleStructuralCharacter(
		char current,
		ref int index,
		ref int depth,
		ref bool inString,
		ref char stringChar) {
		if (current is '"' or '\'' or '`') {
			inString = true;
			stringChar = current;
			index++;
			return true;
		}

		if (current == '{') {
			depth++;
			index++;
			return true;
		}

		if (current == '}') {
			depth--;
			index++;
			return true;
		}

		return false;
	}

	private static bool TryReadQuotedPropertyName(
		string content,
		int startIndex,
		char quote,
		out string propertyName,
		out int nextIndex) {
		propertyName = string.Empty;
		nextIndex = startIndex;
		int endIndex = startIndex + 1;
		while (endIndex < content.Length) {
			if (content[endIndex] == '\\') {
				endIndex += endIndex + 1 < content.Length ? 2 : 1;
				continue;
			}

			if (content[endIndex] == quote) {
				break;
			}

			endIndex++;
		}

		if (endIndex >= content.Length || content[endIndex] != quote) {
			return false;
		}

		int colonIndex = SkipWhitespace(content, endIndex + 1);
		if (colonIndex >= content.Length || content[colonIndex] != ':') {
			return false;
		}

		propertyName = content.Substring(startIndex + 1, endIndex - startIndex - 1);
		nextIndex = colonIndex + 1;
		return !string.IsNullOrWhiteSpace(propertyName);
	}

	private static bool TryReadQuotedMethodShorthandProperty(
		string content,
		int startIndex,
		char quote,
		out string propertyName,
		out int valueStartIndex) {
		propertyName = string.Empty;
		valueStartIndex = startIndex;
		if (!TryReadQuotedToken(content, startIndex, quote, out propertyName, out int nameEndIndex)) {
			return false;
		}

		int methodStartIndex = SkipWhitespace(content, nameEndIndex);
		if (!TryReadMethodShorthandContinuation(content, methodStartIndex)) {
			return false;
		}

		valueStartIndex = methodStartIndex;
		return true;
	}

	private static bool TryReadQuotedMethodShorthandPropertyName(
		string content,
		int startIndex,
		char quote,
		out string propertyName,
		out int nextIndex) {
		propertyName = string.Empty;
		nextIndex = startIndex;
		if (!TryReadQuotedToken(content, startIndex, quote, out propertyName, out int nameEndIndex)) {
			return false;
		}

		int methodStartIndex = SkipWhitespace(content, nameEndIndex);
		if (!TryReadMethodShorthandContinuation(content, methodStartIndex)) {
			return false;
		}

		nextIndex = nameEndIndex;
		return true;
	}

	private static bool TryReadIdentifierPropertyName(
		string content,
		int startIndex,
		out string propertyName,
		out int nextIndex) {
		propertyName = string.Empty;
		nextIndex = startIndex;
		if (!TryReadIdentifierToken(content, startIndex, out propertyName, out int index)) {
			return false;
		}

		int colonIndex = SkipWhitespace(content, index);
		if (colonIndex >= content.Length || content[colonIndex] != ':') {
			return false;
		}

		nextIndex = colonIndex + 1;
		return true;
	}

	private static bool TryReadMethodShorthandProperty(
		string content,
		int startIndex,
		char current,
		out string propertyName,
		out int valueStartIndex) {
		propertyName = string.Empty;
		valueStartIndex = startIndex;
		if (!TryReadMethodShorthandHeader(content, startIndex, current, out propertyName, out int nameEndIndex, out int methodValueStartIndex)) {
			return false;
		}

		valueStartIndex = methodValueStartIndex;
		return true;
	}

	private static bool TryReadMethodShorthandPropertyName(
		string content,
		int startIndex,
		char current,
		out string propertyName,
		out int nextIndex) {
		propertyName = string.Empty;
		nextIndex = startIndex;
		if (!TryReadMethodShorthandHeader(content, startIndex, current, out propertyName, out int nameEndIndex, out _)) {
			return false;
		}

		nextIndex = nameEndIndex;
		return true;
	}

	private static bool TryReadMethodShorthandHeader(
		string content,
		int startIndex,
		char current,
		out string propertyName,
		out int nameEndIndex,
		out int valueStartIndex) {
		propertyName = string.Empty;
		nameEndIndex = startIndex;
		valueStartIndex = startIndex;
		if (current is '"' or '\'' or '`') {
			if (!TryReadQuotedToken(content, startIndex, current, out propertyName, out nameEndIndex)) {
				return false;
			}

			return TryReadMethodShorthandContinuation(content, SkipWhitespace(content, nameEndIndex));
		}

		if (!TryReadIdentifierToken(content, startIndex, out string firstToken, out int firstTokenEndIndex)) {
			return false;
		}

		int nextTokenIndex = SkipWhitespace(content, firstTokenEndIndex);
		if (string.Equals(firstToken, "async", StringComparison.Ordinal) &&
		    nextTokenIndex < content.Length &&
		    IsIdentifierStart(content[nextTokenIndex]) &&
		    TryReadIdentifierToken(content, nextTokenIndex, out propertyName, out nameEndIndex)) {
			valueStartIndex = startIndex;
			return TryReadMethodShorthandContinuation(content, SkipWhitespace(content, nameEndIndex));
		}

		propertyName = firstToken;
		nameEndIndex = firstTokenEndIndex;
		valueStartIndex = startIndex;
		return TryReadMethodShorthandContinuation(content, nextTokenIndex);
	}

	private static bool TryReadMethodShorthandContinuation(string content, int startIndex) {
		if (startIndex >= content.Length || content[startIndex] != '(') {
			return false;
		}

		if (!TrySkipBalancedParentheses(content, startIndex, out int afterParametersIndex)) {
			return false;
		}

		int bodyStartIndex = SkipWhitespace(content, afterParametersIndex);
		return bodyStartIndex < content.Length && content[bodyStartIndex] == '{';
	}

	private static bool TrySkipBalancedParentheses(string content, int startIndex, out int nextIndex) {
		nextIndex = startIndex;
		if (startIndex >= content.Length || content[startIndex] != '(') {
			return false;
		}

		int parenDepth = 0;
		int braceDepth = 0;
		int bracketDepth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = startIndex;

		while (index < content.Length) {
			if (TryConsumeStringLiteralCharacter(content, ref index, ref inString, stringChar)) {
				continue;
			}

			if (TrySkipTriviaAndComments(content, ref index) || TrySkipRegexLiteral(content, ref index)) {
				continue;
			}

			char current = content[index];
			if (current is '"' or '\'' or '`') {
				inString = true;
				stringChar = current;
				index++;
				continue;
			}

			switch (current) {
				case '(':
					parenDepth++;
					index++;
					continue;
				case ')':
					parenDepth--;
					index++;
					if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0) {
						nextIndex = index;
						return true;
					}
					continue;
				case '{':
					braceDepth++;
					index++;
					continue;
				case '}':
					braceDepth--;
					index++;
					continue;
				case '[':
					bracketDepth++;
					index++;
					continue;
				case ']':
					bracketDepth--;
					index++;
					continue;
				default:
					index++;
					continue;
			}
		}

		return false;
	}

	private static bool TryReadQuotedToken(
		string content,
		int startIndex,
		char quote,
		out string token,
		out int nextIndex) {
		token = string.Empty;
		nextIndex = startIndex;
		int endIndex = startIndex + 1;
		while (endIndex < content.Length) {
			if (content[endIndex] == '\\') {
				endIndex += endIndex + 1 < content.Length ? 2 : 1;
				continue;
			}

			if (content[endIndex] == quote) {
				token = content.Substring(startIndex + 1, endIndex - startIndex - 1);
				nextIndex = endIndex + 1;
				return true;
			}

			endIndex++;
		}

		return false;
	}

	private static bool TryReadIdentifierToken(
		string content,
		int startIndex,
		out string token,
		out int nextIndex) {
		token = string.Empty;
		nextIndex = startIndex;
		if (startIndex >= content.Length || !IsIdentifierStart(content[startIndex])) {
			return false;
		}

		int index = startIndex + 1;
		while (index < content.Length && IsIdentifierPart(content[index])) {
			index++;
		}

		token = content.Substring(startIndex, index - startIndex);
		nextIndex = index;
		return true;
	}

	private static int SkipWhitespace(string content, int startIndex) {
		int index = startIndex;
		while (index < content.Length && char.IsWhiteSpace(content[index])) {
			index++;
		}
		return index;
	}

	private static bool IsIdentifierStart(char c) =>
		char.IsLetter(c) || c is '_' or '$';

	private static bool IsIdentifierPart(char c) =>
		char.IsLetterOrDigit(c) || c is '_' or '$';

	private static int ConsumeStringLiteralCharacter(
		string content,
		int index,
		ref bool inString,
		char stringChar) {
		if (content[index] == '\\') {
			return index + 1 < content.Length ? index + 2 : index + 1;
		}

		if (content[index] == stringChar) {
			inString = false;
		}

		return index + 1;
	}

	private static int SkipLineComment(string content, int index, int length) {
		index += 2;
		while (index < length && content[index] != '\n' && content[index] != '\r') {
			index++;
		}
		return index;
	}
}
