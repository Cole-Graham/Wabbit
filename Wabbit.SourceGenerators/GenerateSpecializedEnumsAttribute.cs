using System;

namespace Wabbit.Models
{
    /// <summary>
    /// Attribute to mark an enum for specialized enum generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]
    public class GenerateSpecializedEnumsAttribute : Attribute
    {
        /// <summary>
        /// Gets the prefixes to use for generating specialized enums.
        /// </summary>
        public string[] Prefixes { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GenerateSpecializedEnumsAttribute"/> class.
        /// </summary>
        /// <param name="prefixes">The prefixes to use for generating specialized enums.</param>
        public GenerateSpecializedEnumsAttribute(params string[] prefixes)
        {
            Prefixes = prefixes;
        }
    }
}