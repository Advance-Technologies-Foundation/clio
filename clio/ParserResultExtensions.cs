using System;
using System.Collections.Generic;
using CommandLine;

namespace Clio
{

	#region Class: ParserResultExtensions

	public static class ParserResultExtensions
	{

		#region Methods: Public

		public static TResult MapResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18,
			T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, TResult>(
			this ParserResult<object> result,
			Func<T1, TResult> parsedFunc1,
			Func<T2, TResult> parsedFunc2,
			Func<T3, TResult> parsedFunc3,
			Func<T4, TResult> parsedFunc4,
			Func<T5, TResult> parsedFunc5,
			Func<T6, TResult> parsedFunc6,
			Func<T7, TResult> parsedFunc7,
			Func<T8, TResult> parsedFunc8,
			Func<T9, TResult> parsedFunc9,
			Func<T10, TResult> parsedFunc10,
			Func<T11, TResult> parsedFunc11,
			Func<T12, TResult> parsedFunc12,
			Func<T13, TResult> parsedFunc13,
			Func<T14, TResult> parsedFunc14,
			Func<T15, TResult> parsedFunc15,
			Func<T16, TResult> parsedFunc16,
			Func<T17, TResult> parsedFunc17,
			Func<T18, TResult> parsedFunc18,
			Func<T19, TResult> parsedFunc19,
			Func<T20, TResult> parsedFunc20,
			Func<T21, TResult> parsedFunc21,
			Func<T22, TResult> parsedFunc22,
			Func<T23, TResult> parsedFunc23,
			Func<T24, TResult> parsedFunc24,
			Func<T25, TResult> parsedFunc25,
			Func<T26, TResult> parsedFunc26,
			Func<T27, TResult> parsedFunc27,
			Func<T28, TResult> parsedFunc28,
			Func<T29, TResult> parsedFunc29,
			Func<T30, TResult> parsedFunc30,
			Func<T31, TResult> parsedFunc31,
			Func<T32, TResult> parsedFunc32,
			Func<T33, TResult> parsedFunc33,
			Func<T34, TResult> parsedFunc34,
			Func<IEnumerable<Error>, TResult> notParsedFunc)
		{
			if (!(result is Parsed<object> parsed))
				return notParsedFunc(((NotParsed<object>) result).Errors);
			if (parsed.Value is T1)
				return parsedFunc1((T1) parsed.Value);
			if (parsed.Value is T2)
				return parsedFunc2((T2) parsed.Value);
			if (parsed.Value is T3)
				return parsedFunc3((T3) parsed.Value);
			if (parsed.Value is T4)
				return parsedFunc4((T4) parsed.Value);
			if (parsed.Value is T5)
				return parsedFunc5((T5) parsed.Value);
			if (parsed.Value is T6)
				return parsedFunc6((T6) parsed.Value);
			if (parsed.Value is T7)
				return parsedFunc7((T7) parsed.Value);
			if (parsed.Value is T8)
				return parsedFunc8((T8) parsed.Value);
			if (parsed.Value is T9)
				return parsedFunc9((T9) parsed.Value);
			if (parsed.Value is T10)
				return parsedFunc10((T10) parsed.Value);
			if (parsed.Value is T11)
				return parsedFunc11((T11) parsed.Value);
			if (parsed.Value is T12)
				return parsedFunc12((T12) parsed.Value);
			if (parsed.Value is T13)
				return parsedFunc13((T13) parsed.Value);
			if (parsed.Value is T14)
				return parsedFunc14((T14) parsed.Value);
			if (parsed.Value is T15)
				return parsedFunc15((T15) parsed.Value);
			if (parsed.Value is T16)
				return parsedFunc16((T16) parsed.Value);
			if (parsed.Value is T17)
				return parsedFunc17((T17) parsed.Value);
			if (parsed.Value is T18)
				return parsedFunc18((T18) parsed.Value);
			if (parsed.Value is T19)
				return parsedFunc19((T19) parsed.Value);
			if (parsed.Value is T20)
				return parsedFunc20((T20) parsed.Value);
			if (parsed.Value is T21)
				return parsedFunc21((T21) parsed.Value);
			if (parsed.Value is T22)
				return parsedFunc22((T22) parsed.Value);
			if (parsed.Value is T23)
				return parsedFunc23((T23) parsed.Value);
			if (parsed.Value is T24)
				return parsedFunc24((T24) parsed.Value);
			if (parsed.Value is T25)
				return parsedFunc25((T25) parsed.Value);
			if (parsed.Value is T26)
				return parsedFunc26((T26) parsed.Value);
			if (parsed.Value is T27)
				return parsedFunc27((T27) parsed.Value);
			if (parsed.Value is T28)
				return parsedFunc28((T28) parsed.Value);
			if (parsed.Value is T29)
				return parsedFunc29((T29) parsed.Value);
			if (parsed.Value is T30)
				return parsedFunc30((T30) parsed.Value);
			if (parsed.Value is T31)
				return parsedFunc31((T31) parsed.Value);
			if (parsed.Value is T32)
				return parsedFunc32((T32) parsed.Value);
			if (parsed.Value is T33)
				return parsedFunc33((T33)parsed.Value);
			if (parsed.Value is T34)
				return parsedFunc34((T34)parsed.Value);
			throw new InvalidOperationException();
		}

		#endregion

	}

	#endregion

}
