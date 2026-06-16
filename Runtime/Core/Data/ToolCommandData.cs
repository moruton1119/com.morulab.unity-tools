using System.Collections.Generic;
using System.Reflection;

namespace MorulabTools.Launcher
{
    public class ToolCommandData
    {
        public string Path;        // MenuItem path
        public string OriginalTitle; // Fallback
        public string Title => OriginalTitle; // Compatibility alias
        
        // Localized Data (Key: "ja", "en", "ko")
        public Dictionary<string, LocalizedInfo> LocalizedInfos = new Dictionary<string, LocalizedInfo>();

        public MethodInfo TargetMethod;
        public string IconName;

        // Helper to get localized info
        public LocalizedInfo GetInfo(string lang)
        {
            if (LocalizedInfos.TryGetValue(lang, out var info)) return info;
            if (LocalizedInfos.TryGetValue("en", out var enInfo)) return enInfo; // Fallback to EN
            
            // Last resort: Auto-generated defaults
            return new LocalizedInfo 
            { 
                Title = OriginalTitle, 
                Description = "No description.", 
                Category = "General" 
            };
        }
    }
}
