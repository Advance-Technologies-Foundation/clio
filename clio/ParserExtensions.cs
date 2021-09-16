using System;
using System.Collections.Generic;
using CommandLine;

namespace Clio
{

	#region Class: ParserExtensions

	public static class ParserExtensions
	{

		#region Methods: Public

		public static ParserResult<object> ParseArguments<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34>(
			this Parser parser,
			IEnumerable<string> args)
		{
			if (parser == null)
				throw new ArgumentNullException(nameof (parser));
			return parser.ParseArguments(args, typeof (T1), typeof (T2), typeof (T3), typeof (T4), typeof (T5), typeof (T6), typeof (T7), typeof (T8), typeof (T9), typeof (T10), typeof (T11), typeof (T12), typeof (T13), typeof (T14), typeof (T15), typeof (T16), typeof (T17), typeof (T18), typeof (T19), typeof (T20), typeof (T21), typeof (T22), typeof (T23), typeof (T24), typeof (T25), typeof (T26), typeof (T27), typeof (T28), typeof (T29), typeof (T30), typeof (T31), typeof (T32), typeof(T33), typeof(T34));
		}

		#endregion

	}

	#endregion

}