using System;

namespace MorulabTools.Launcher
{
    /// <summary>
    /// Define localized title and description for each language.
    /// lang: "en", "ja", "ko", etc.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ToolLocalizeAttribute : Attribute
    {
        public string Lang { get; }
        public string Title { get; }
        public string Description { get; }
        public string Category { get; }

        public ToolLocalizeAttribute(string lang, string title, string description, string category = null)
        {
            Lang = lang;
            Title = title;
            Description = description;
            Category = category;
        }
    }
}
