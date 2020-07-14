using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Clio.Common
{

	#region Class: TextUtilities

	public class TextUtilities
	{

		#region Methods: Public

		public static string ConvertTableToString(IEnumerable<string[]> table, int distanceBetweenColumns = 5, 
				char pad = ' ', string beginPad = "") {
			if (!table.Any()) {
				return string.Empty;
			}
			int columnsCount = table.First().Length;
			var columnMaxValueLength = new int[columnsCount];
			for (int i = 0; i < columnsCount; i++) {
				columnMaxValueLength[i] = table.Max(p => p[i].Length);
			}
			var sb = new StringBuilder();
			foreach (string[] selectedPackage in table) {
				for (int i = 0; i < columnsCount; i++) {
					sb.Append(beginPad);
					sb.Append(selectedPackage[i].PadRight(columnMaxValueLength[i] + distanceBetweenColumns, pad));
				}
				sb.AppendLine();
			}
			return sb.ToString();
		}

		#endregion

	}

	#endregion

}