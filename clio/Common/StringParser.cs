using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Clio.Common
{
	public class StringParser
	{
		public static IEnumerable<string> ParseArray(string input) {
			return input.Split(',').Select(p => p.Trim()).ToList();
		}
	}
}
