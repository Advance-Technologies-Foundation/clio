using CommandLine;
using System;
using System.Collections.Generic;

namespace Clio
{

	#region Class: ParserResultExtensions

	public static class ParserResultExtensions
	{

		#region Methods: Public

		public static TResult MapResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18,
				T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36, T37, T38, T39,
				T40, T41, T42, T43, T44, T45, T46, T47, T48, T49, T50, T51,T52, T53, T54, T55, T56, T57, T58, T59, T60,
				TResult>(
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
			Func<T35, TResult> parsedFunc35,
			Func<T36, TResult> parsedFunc36,
			Func<T37, TResult> parsedFunc37,
			Func<T38, TResult> parsedFunc38,
			Func<T39, TResult> parsedFunc39,
			Func<T40, TResult> parsedFunc40,
			Func<T41, TResult> parsedFunc41,
			Func<T42, TResult> parsedFunc42,
			Func<T43, TResult> parsedFunc43,
			Func<T44, TResult> parsedFunc44,
			Func<T45, TResult> parsedFunc45,
			Func<T46, TResult> parsedFunc46,
			Func<T47, TResult> parsedFunc47,
			Func<T48, TResult> parsedFunc48,
			Func<T49, TResult> parsedFunc49,
			Func<T50, TResult> parsedFunc50,
			Func<T51, TResult> parsedFunc51,
			Func<T52, TResult> parsedFunc52,
			Func<T53, TResult> parsedFunc53,
			Func<T54, TResult> parsedFunc54,
			Func<T55, TResult> parsedFunc55,
			Func<T56, TResult> parsedFunc56,
			Func<T57, TResult> parsedFunc57,
			Func<T58, TResult> parsedFunc58,
			Func<T59, TResult> parsedFunc59,
			Func<T60, TResult> parsedFunc60,
			Func<IEnumerable<Error>, TResult> notParsedFunc)
		{
			if (!(result is Parsed<object> parsed))
				return notParsedFunc(((NotParsed<object>)result).Errors);
			if (parsed.Value is T1)
				return parsedFunc1((T1)parsed.Value);
			if (parsed.Value is T2)
				return parsedFunc2((T2)parsed.Value);
			if (parsed.Value is T3)
				return parsedFunc3((T3)parsed.Value);
			if (parsed.Value is T4)
				return parsedFunc4((T4)parsed.Value);
			if (parsed.Value is T5)
				return parsedFunc5((T5)parsed.Value);
			if (parsed.Value is T6)
				return parsedFunc6((T6)parsed.Value);
			if (parsed.Value is T7)
				return parsedFunc7((T7)parsed.Value);
			if (parsed.Value is T8)
				return parsedFunc8((T8)parsed.Value);
			if (parsed.Value is T9)
				return parsedFunc9((T9)parsed.Value);
			if (parsed.Value is T10)
				return parsedFunc10((T10)parsed.Value);
			if (parsed.Value is T11)
				return parsedFunc11((T11)parsed.Value);
			if (parsed.Value is T12)
				return parsedFunc12((T12)parsed.Value);
			if (parsed.Value is T13)
				return parsedFunc13((T13)parsed.Value);
			if (parsed.Value is T14)
				return parsedFunc14((T14)parsed.Value);
			if (parsed.Value is T15)
				return parsedFunc15((T15)parsed.Value);
			if (parsed.Value is T16)
				return parsedFunc16((T16)parsed.Value);
			if (parsed.Value is T17)
				return parsedFunc17((T17)parsed.Value);
			if (parsed.Value is T18)
				return parsedFunc18((T18)parsed.Value);
			if (parsed.Value is T19)
				return parsedFunc19((T19)parsed.Value);
			if (parsed.Value is T20)
				return parsedFunc20((T20)parsed.Value);
			if (parsed.Value is T21)
				return parsedFunc21((T21)parsed.Value);
			if (parsed.Value is T22)
				return parsedFunc22((T22)parsed.Value);
			if (parsed.Value is T23)
				return parsedFunc23((T23)parsed.Value);
			if (parsed.Value is T24)
				return parsedFunc24((T24)parsed.Value);
			if (parsed.Value is T25)
				return parsedFunc25((T25)parsed.Value);
			if (parsed.Value is T26)
				return parsedFunc26((T26)parsed.Value);
			if (parsed.Value is T27)
				return parsedFunc27((T27)parsed.Value);
			if (parsed.Value is T28)
				return parsedFunc28((T28)parsed.Value);
			if (parsed.Value is T29)
				return parsedFunc29((T29)parsed.Value);
			if (parsed.Value is T30)
				return parsedFunc30((T30)parsed.Value);
			if (parsed.Value is T31)
				return parsedFunc31((T31)parsed.Value);
			if (parsed.Value is T32)
				return parsedFunc32((T32)parsed.Value);
			if (parsed.Value is T33)
				return parsedFunc33((T33)parsed.Value);
			if (parsed.Value is T34)
				return parsedFunc34((T34)parsed.Value);
			if (parsed.Value is T35)
				return parsedFunc35((T35)parsed.Value);
			if (parsed.Value is T36)
				return parsedFunc36((T36)parsed.Value);
			if (parsed.Value is T37)
				return parsedFunc37((T37)parsed.Value);
			if (parsed.Value is T38)
				return parsedFunc38((T38)parsed.Value);
			if (parsed.Value is T39)
				return parsedFunc39((T39)parsed.Value);
			if (parsed.Value is T40)
				return parsedFunc40((T40)parsed.Value);
			if (parsed.Value is T41)
				return parsedFunc41((T41)parsed.Value);
			if (parsed.Value is T42)
				return parsedFunc42((T42)parsed.Value);
			if (parsed.Value is T43)
				return parsedFunc43((T43)parsed.Value);
			if (parsed.Value is T44)
				return parsedFunc44((T44)parsed.Value);
			if (parsed.Value is T45)
				return parsedFunc45((T45)parsed.Value);
			if (parsed.Value is T46)
				return parsedFunc46((T46)parsed.Value);
			if (parsed.Value is T47)
				return parsedFunc47((T47)parsed.Value);
			if (parsed.Value is T48)
				return parsedFunc48((T48)parsed.Value);
			if (parsed.Value is T49)
				return parsedFunc49((T49)parsed.Value);
			if (parsed.Value is T50)
				return parsedFunc50((T50)parsed.Value);
			if (parsed.Value is T51)
				return parsedFunc51((T51)parsed.Value);
			if (parsed.Value is T52)
				return parsedFunc52((T52)parsed.Value);
			if (parsed.Value is T53)
				return parsedFunc53((T53)parsed.Value);
			if (parsed.Value is T54)
				return parsedFunc54((T54)parsed.Value);
			if (parsed.Value is T55)
				return parsedFunc55((T55)parsed.Value);
			if (parsed.Value is T56)
				return parsedFunc56((T56)parsed.Value);	
			if (parsed.Value is T57)
				return parsedFunc57((T57)parsed.Value);
			if (parsed.Value is T58)
				return parsedFunc58((T58)parsed.Value);
			if (parsed.Value is T59)
				return parsedFunc59((T59)parsed.Value);
			if (parsed.Value is T60)
				return parsedFunc60((T60)parsed.Value);
			throw new InvalidOperationException();
		}

		#endregion

	}

	#endregion

}
