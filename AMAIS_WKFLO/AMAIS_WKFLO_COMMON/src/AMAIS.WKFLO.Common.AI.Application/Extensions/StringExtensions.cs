using System.Text.RegularExpressions;

namespace AMAIS.WKFLO.Common.AI.Application.Extensions
{
    /// <summary>
    /// Provides extension methods for string operations.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Sanitizes a string to be used as an agent name by removing invalid characters
        /// that are not allowed in Microsoft Semantic Kernel's ChatCompletionAgent.
        /// </summary>
        /// <param name="input">The input string to sanitize.</param>
        /// <returns>A sanitized string that matches the pattern ^[^\s<|\\/>]+$</returns>
        public static string SanitizeForAgentName(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove spaces, <, >, |, \, / characters
            return Regex.Replace(input, @"[\s<|\\/>]", "_");
        }

        /// <summary>
        /// Sanitizes a string to be used as a plugin name in Microsoft Semantic Kernel.
        /// Plugin names can only contain ASCII letters, digits, and underscores.
        /// </summary>
        /// <param name="input">The input string to sanitize</param>
        /// <returns>A sanitized string that only contains valid characters</returns>
        public static string SanitizePluginName(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Replace any character that is not an ASCII letter, digit, or underscore with an underscore
            return Regex.Replace(input, @"[^a-zA-Z0-9_]", "_");
        }
    }
}