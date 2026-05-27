using System.Collections.Generic;

namespace Clio.Command.BusinessRules.Filters;

internal static class StaticFilterConstants {

	internal const string LogicalAnd = "AND";
	internal const string LogicalOr = "OR";

	internal const string Equal = "EQUAL";
	internal const string NotEqual = "NOT_EQUAL";
	internal const string IsNull = "IS_NULL";
	internal const string IsNotNull = "IS_NOT_NULL";
	internal const string Greater = "GREATER";
	internal const string GreaterOrEqual = "GREATER_OR_EQUAL";
	internal const string Less = "LESS";
	internal const string LessOrEqual = "LESS_OR_EQUAL";
	internal const string Contain = "CONTAIN";
	internal const string NotContain = "NOT_CONTAIN";
	internal const string StartWith = "START_WITH";
	internal const string NotStartWith = "NOT_START_WITH";
	internal const string EndWith = "END_WITH";
	internal const string NotEndWith = "NOT_END_WITH";
	internal const string Exists = "EXISTS";
	internal const string NotExists = "NOT_EXISTS";

	internal static readonly IReadOnlySet<string> AllLeafComparisons = new HashSet<string> {
		Equal, NotEqual, IsNull, IsNotNull,
		Greater, GreaterOrEqual, Less, LessOrEqual,
		Contain, NotContain, StartWith, NotStartWith, EndWith, NotEndWith
	};

	internal static readonly IReadOnlySet<string> UnaryComparisons = new HashSet<string> {
		IsNull, IsNotNull
	};

	internal static readonly IReadOnlySet<string> RelationalComparisons = new HashSet<string> {
		Greater, GreaterOrEqual, Less, LessOrEqual
	};

	internal static readonly IReadOnlySet<string> EqualityComparisons = new HashSet<string> {
		Equal, NotEqual
	};

	internal static readonly IReadOnlySet<string> TextComparisons = new HashSet<string> {
		Contain, NotContain, StartWith, NotStartWith, EndWith, NotEndWith
	};

	internal static readonly IReadOnlySet<string> BackwardReferenceComparisons = new HashSet<string> {
		Exists, NotExists
	};
}
