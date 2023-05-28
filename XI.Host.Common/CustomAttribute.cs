using System;

namespace XI.Host.Common
{
    /// <summary>
    /// Use with find all references from the IDE.
    /// </summary>
    public class CustomAttribute : Attribute
    {
        public string Description { get; set; }

        public CustomAttribute() { }

        public CustomAttribute(string description)
        {
            Description = description;
        }
    }
}
