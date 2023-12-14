using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common
{
	public static class ValidateUtilities
	{
		
		/// <summary>
		/// Checks if the provided object is null.
		/// </summary>
		/// <param name="source">The object to check.</param>
		/// <param name="argumentName">The name of the argument that is checked.</param>
		/// <exception cref="System.ArgumentNullException">Thrown when the object is null.</exception>
		public static void CheckArgumentNull(this object source, string argumentName) {
			if (source == null) {
				throw new ArgumentNullException(argumentName ?? string.Empty);
			}
		}

		/// <summary>
		/// Checks if the provided collection is null or empty.
		/// </summary>
		/// <typeparam name="T">The type of the elements in the collection.</typeparam>
		/// <param name="source">The collection to check.</param>
		/// <param name="argumentName">The name of the argument that is checked.</param>
		/// <exception cref="System.ArgumentNullException">Thrown when the collection is null.</exception>
		/// <exception cref="System.ArgumentOutOfRangeException">Thrown when the collection is empty.</exception>
		public static void CheckArgumentNullOrEmptyCollection<T>(this IEnumerable<T> source, string argumentName) {
			source.CheckArgumentNull(argumentName);
			if (!source.Any()) {
				throw new ArgumentOutOfRangeException(argumentName ?? string.Empty);
			}
		}

		/// <summary>
		/// Checks if the provided string is null or consists only of white-space characters.
		/// </summary>
		/// <param name="source">The string to check.</param>
		/// <param name="argumentName">The name of the argument that is checked.</param>
		/// <exception cref="System.ArgumentNullException">Thrown when the string is null or consists only of white-space characters.</exception>
		public static void CheckArgumentNullOrWhiteSpace(this string source, string argumentName) {
			if (string.IsNullOrWhiteSpace(source)) {
				throw new ArgumentNullException(argumentName ?? string.Empty);
			}
		}
	}
}