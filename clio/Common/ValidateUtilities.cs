using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common
{
	public static class ValidateUtilities
	{
		public static void CheckArgumentNull(this object source, string argumentName) {
			if (source == null) {
				throw new ArgumentNullException(argumentName ?? string.Empty);
			}
		}

		public static void CheckArgumentNullOrEmptyCollection<T>(this IEnumerable<T> source, string argumentName) {
			source.CheckArgumentNull(argumentName);
			if (!source.Any()) {
				throw new ArgumentOutOfRangeException(argumentName ?? string.Empty);
			}
		}

		public static void CheckArgumentNullOrWhiteSpace(this string source, string argumentName) {
			if (string.IsNullOrWhiteSpace(source)) {
				throw new ArgumentNullException(argumentName ?? string.Empty);
			}
		}

	}
}