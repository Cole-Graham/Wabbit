using System;

namespace Wabbit.Models
{
    [AttributeUsage(AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]
    public class GenerateSpecializedEnumsAttribute : Attribute
    {
        public string[] Prefixes { get; }

        public GenerateSpecializedEnumsAttribute(params string[] prefixes)
        {
            Prefixes = prefixes;
        }
    }
}