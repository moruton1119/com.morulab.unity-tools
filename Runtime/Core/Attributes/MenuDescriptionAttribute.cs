using System;

namespace MorulabTools.Launcher
{
    /// <summary>
    /// メニューアイテムに説明文やアイコン情報を付与するための属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class MenuDescriptionAttribute : Attribute
    {
        public string Description { get; }
        public string IconName { get; }
        public string Category { get; }

        public MenuDescriptionAttribute(string description, string category = "General", string iconName = null)
        {
            Description = description;
            Category = category;
            IconName = iconName;
        }
    }
}
