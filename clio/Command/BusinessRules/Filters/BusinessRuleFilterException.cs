using System;

namespace Clio.Command.BusinessRules.Filters;

public sealed class BusinessRuleFilterException : Exception {
	public BusinessRuleFilterException(string errorCode, string fieldPath, string message)
		: base(message) {
		ErrorCode = errorCode;
		FieldPath = fieldPath;
	}

	public string ErrorCode { get; }
	public string FieldPath { get; }
}
