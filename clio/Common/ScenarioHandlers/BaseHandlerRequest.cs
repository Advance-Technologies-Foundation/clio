using System;
using System.Collections.Generic;
using System.Globalization;
using FluentValidation;

namespace Clio.Common.ScenarioHandlers {
    /// <summary>
    /// Base request carrying the stringly-typed <see cref="Arguments"/> bag shared by all
    /// scenario handlers. Use <see cref="GetRequired{T}(string)"/> at call sites to read a
    /// required argument as a strongly-typed, non-null value.
    /// </summary>
    public class BaseHandlerRequest {
        /// <summary>
        /// The scenario-step arguments keyed by option name. Values are stored as strings and
        /// converted to the requested type by <see cref="GetRequired{T}(string)"/>.
        /// </summary>
        public Dictionary<string, string> Arguments { get; set; }

        /// <summary>
        /// Reads the argument identified by <paramref name="key"/> and converts it to
        /// <typeparamref name="T"/>, guaranteeing a non-null result.
        /// </summary>
        /// <typeparam name="T">
        /// The target type (for example <see cref="string"/>, <see cref="int"/> or
        /// <see cref="bool"/>). Non-string types are converted with
        /// <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/> using
        /// <see cref="CultureInfo.InvariantCulture"/>.
        /// </typeparam>
        /// <param name="key">The argument key to read.</param>
        /// <returns>The argument value converted to <typeparamref name="T"/>.</returns>
        /// <remarks>
        /// The raw string value is not trimmed; trimming is left to the call site so that
        /// values that must preserve surrounding characters (for example connection strings)
        /// are not altered. Boolean conversion uses <see cref="bool.Parse(string)"/> semantics,
        /// so it accepts "True"/"False" (case-insensitive) but not "1"/"0".
        /// </remarks>
        /// <exception cref="FluentValidation.ValidationException">
        /// Thrown when the arguments bag is null, the key is missing, the value is missing, null,
        /// empty, or whitespace-only, or the value cannot be converted to <typeparamref name="T"/>.
        /// </exception>
        public T GetRequired<T>(string key) {
            if (Arguments is null || !Arguments.TryGetValue(key, out string raw) || string.IsNullOrWhiteSpace(raw)) {
                throw new ValidationException($"Required argument '{key}' is missing, empty, or whitespace");
            }
            if (typeof(T) == typeof(string)) {
                return (T)(object)raw;
            }
            try {
                return (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
            } catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) {
                throw new ValidationException($"Argument '{key}' value '{raw}' is not a valid {typeof(T).Name}");
            }
        }

        /// <summary>
        /// Reads the argument identified by <paramref name="key"/> as a non-null
        /// <see cref="string"/>.
        /// </summary>
        /// <param name="key">The argument key to read.</param>
        /// <returns>The argument value as a string.</returns>
        /// <exception cref="FluentValidation.ValidationException">
        /// Thrown when the arguments bag is null, the key is missing, or the value is null, empty,
        /// or whitespace-only.
        /// </exception>
        public string GetRequired(string key) => GetRequired<string>(key);
    }
}
