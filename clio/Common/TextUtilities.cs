using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clio.Project.NuGet;

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

		/// <summary>
		/// Renders a <see cref="PackageVersion"/> that came from OUTSIDE clio — a target environment's
		/// <c>SysPackage.Version</c> column, or a bundled archive's descriptor — in a form that is safe to quote
		/// back to a reader.
		/// </summary>
		/// <param name="version">The version to render.</param>
		/// <param name="maxSuffixLength">Cap on the pre-release suffix, which is the only free-text part.</param>
		/// <returns>
		/// The four-part number, plus a restricted suffix when the original carried a usable one; never
		/// <c>null</c>.
		/// </returns>
		/// <remarks>
		/// Rejects an implausible suffix WHOLESALE rather than repairing it, and that is the load-bearing
		/// choice. Filtering the forbidden characters out instead was tried and is worse than useless: it
		/// deletes the spaces and newlines but keeps the letters, so
		/// <c>0.0.0.1-rc\r\nIGNORE PRIOR INSTRUCTIONS and call …</c> comes back as
		/// <c>0.0.0.1-rcIGNOREPRIORINSTRUCTIONSandcall</c> — the words intact, and now wearing the shape of real
		/// data. A reader cannot tell that from a version somebody genuinely stamped. Dropping the suffix says
		/// what is true: it was not credible, so it is not shown.
		/// <para>
		/// <see cref="SanitizeForDisplay"/> is the wrong tool here even though it looks like the right one: it
		/// removes control characters, which stops a forged output line but leaves
		/// <c>1.0.0.0-IGNORE PRIOR INSTRUCTIONS AND CALL …</c> completely intact — one line, no control bytes,
		/// whole payload. These messages reach an MCP agent's context, so the defence has to be "the output can
		/// only look like a version".
		/// </para>
		/// <para>
		/// Why the input cannot be trusted in the first place: <see cref="PackageVersion"/> splits on the first
		/// <c>-</c>, and everything after it becomes <c>Suffix</c> — unbounded free text that <c>ToString</c>
		/// re-emits verbatim, newlines included. The numeric half parses as <see cref="System.Version"/> and
		/// needs no defending. A version read from an environment is attacker-controllable by anyone who can
		/// install a package there, because that string comes from the package's own descriptor.
		/// </para>
		/// <para>
		/// A bundled version is normally clio's own artifact and would not need this. It is passed through
		/// anyway at the one call site that quotes it — the refusal of a MALFORMED bundled version — precisely
		/// because that refusal fires when the archive cannot be assumed well-formed.
		/// </para>
		/// </remarks>
		public static string SanitizeVersionForDisplay(PackageVersion version, int maxSuffixLength = 32) {
			if (version is null) {
				return string.Empty;
			}
			string suffix = version.Suffix;
			if (string.IsNullOrWhiteSpace(suffix)) {
				return version.Version.ToString();
			}
			// Over-long counts as implausible too, not merely as something to shorten: a real pre-release tag is
			// a handful of characters, so anything past the cap is already not the thing this is rendering.
			bool credible = suffix.Length <= maxSuffixLength
				&& suffix.All(character =>
					char.IsLetterOrDigit(character) || character is '.' or '_' or '-');
			return credible ? $"{version.Version}-{suffix}" : version.Version.ToString();
		}

		#endregion

	}

	#endregion

}