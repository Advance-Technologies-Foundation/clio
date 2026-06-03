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

	internal const string Count = "COUNT";
	internal const string Sum = "SUM";
	internal const string Avg = "AVG";
	internal const string Min = "MIN";
	internal const string Max = "MAX";

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

	/// <summary>All supported aggregation functions for backward-reference filters.</summary>
	internal static readonly IReadOnlySet<string> AggregationTypes = new HashSet<string> {
		Count, Sum, Avg, Min, Max
	};

	/// <summary>Aggregations that operate on a numeric column (require aggregationColumnPath).</summary>
	internal static readonly IReadOnlySet<string> ScalarAggregationTypes = new HashSet<string> {
		Sum, Avg, Min, Max
	};

	/// <summary>Comparison tokens allowed for an aggregation threshold (e.g. COUNT GREATER 10).</summary>
	internal static readonly IReadOnlySet<string> AggregationComparisons = new HashSet<string> {
		Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual
	};
}
