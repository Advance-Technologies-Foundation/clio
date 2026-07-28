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
				char paddingChar = ' ', string beginPadding = "") {
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
				sb.Append(beginPadding);
				for (int i = 0; i < columnsCount; i++) {
					int totalWidth = columnMaxValueLength[i] + distanceBetweenColumns;
					sb.Append(selectedPackage[i].PadRight(totalWidth, paddingChar));
				}
				sb.AppendLine();
			}
			return sb.ToString();
		}

		/// <summary>
		/// Prepares untrusted text (typically a raw HTTP response body from a Creatio service) for safe inclusion
		/// in a user-facing message, log line, or MCP tool result. Replaces every control character with a space so
		/// a hostile or misbehaving endpoint cannot forge extra output lines or inject terminal escape sequences,
		/// and caps the result at <paramref name="maxLength"/> characters (appending an ellipsis) so a large
		/// non-JSON payload — for example a whole HTML login page — cannot flood the output.
		/// </summary>
		/// <param name="text">The untrusted text to sanitize.</param>
		/// <param name="maxLength">The maximum length of the sanitized text before it is truncated.</param>
		/// <returns>A single-line, length-capped, control-character-free rendering of <paramref name="text"/>;
		/// the input unchanged when it is <c>null</c> or empty.</returns>
		public static string SanitizeForDisplay(string text, int maxLength = 500) {
			if (string.IsNullOrEmpty(text)) {
				return text;
			}
			var sb = new StringBuilder(text.Length);
			foreach (char character in text) {
				sb.Append(char.IsControl(character) ? ' ' : character);
			}
			string sanitized = sb.ToString();
			if (sanitized.Length > maxLength) {
				return sanitized.Substring(0, maxLength) + "...";
			}
			return sanitized;
		}

		#endregion

	}

	#endregion

}