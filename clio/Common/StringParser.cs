using System.Collections.Generic;
using System.Linq;

namespace Clio.Common
{
	public class StringParser
	{
		public static IEnumerable<string> ParseArray(string input) {
			return input.Split(',').Select(p => p.Trim()).ToList();
		}
	}
}
