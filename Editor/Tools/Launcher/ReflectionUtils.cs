using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace MorulabTools.Launcher
{
    public static class ReflectionUtils
    {
        public static List<ToolCommandData> FindCommands(string rootPathFilter = "Morulab")
        {
            var commands = new List<ToolCommandData>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }

                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var method in methods)
                    {
                        var menuItemAttrs = method.GetCustomAttributes<MenuItem>(false);
                        var descAttr = method.GetCustomAttribute<MenuDescriptionAttribute>(false);
                        var locAttrs = method.GetCustomAttributes<ToolLocalizeAttribute>(false);

                        foreach (var menuItemAttr in menuItemAttrs)
                        {
                            string menuPath = menuItemAttr.menuItem;
                            if (menuItemAttr.validate) continue;

                            if (!string.IsNullOrEmpty(rootPathFilter) && !menuPath.StartsWith(rootPathFilter))
                            {
                                continue;
                            }

                            string relativePath = menuPath;
                            if (menuPath.StartsWith(rootPathFilter))
                            {
                                relativePath = menuPath.Substring(rootPathFilter.Length).TrimStart('/');
                            }

                            var parts = relativePath.Split('/');
                            string autoCategory = "General";
                            if (parts.Length > 1) autoCategory = parts[0];
                            string autoTitle = parts.Last();

                            var cmd = new ToolCommandData
                            {
                                Path = menuPath,
                                OriginalTitle = autoTitle,
                                TargetMethod = method,
                                IconName = descAttr?.IconName
                            };

                            // Default (EN) from MenuDescription or Auto
                            cmd.LocalizedInfos["en"] = new LocalizedInfo
                            {
                                Title = autoTitle,
                                Description = descAttr?.Description ?? "No description available.",
                                Category = descAttr?.Category ?? autoCategory
                            };

                            // Multi-lang overrides
                            foreach (var attr in locAttrs)
                            {
                                cmd.LocalizedInfos[attr.Lang] = new LocalizedInfo
                                {
                                    Title = attr.Title,
                                    Description = attr.Description,
                                    Category = attr.Category ?? cmd.LocalizedInfos["en"].Category
                                };
                            }

                            commands.Add(cmd);
                        }
                    }
                }
            }
            // 並び替えはUI側で言語決定後に行うのがベターだが、ここではデフォルト(EN)順で返す
            return commands.OrderBy(c => c.GetInfo("en").Category).ThenBy(c => c.GetInfo("en").Title).ToList();
        }
    }
}
