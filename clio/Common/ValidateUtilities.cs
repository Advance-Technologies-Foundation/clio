using System;

namespace Clio.Common
{
	public static class ValidateUtilities
	{
		public static void CheckArgumentNull(this object source, string argumentName) {
			if (source == null) {
				throw new ArgumentNullException(argumentName ?? string.Empty);
			}
		}

		public static void CheckArgumentNullOrWhiteSpace(this string source, string argumentName) {
			if (string.IsNullOrWhiteSpace(source)) {
				throw new ArgumentNullException(argumentName ?? string.Empty);
			}
		}

	}
}