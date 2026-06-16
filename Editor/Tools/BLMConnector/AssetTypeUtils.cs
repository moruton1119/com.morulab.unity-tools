using System.Collections.Generic;
using System.Linq;

namespace Moruton.BLMConnector
{
    public static class AssetTypeUtils
    {
        public static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd" };
        public static readonly string[] ModelExtensions = { ".fbx", ".obj" };
        public static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg" };
        public static readonly string[] UnityPackageExtensions = { ".unitypackage" };

        public static AssetType GetAssetType(string extension)
        {
            string ext = extension.ToLower();
            if (UnityPackageExtensions.Contains(ext)) return AssetType.UnityPackage;
            if (TextureExtensions.Contains(ext)) return AssetType.Texture;
            if (ModelExtensions.Contains(ext)) return AssetType.Model;
            if (AudioExtensions.Contains(ext)) return AssetType.Audio;
            return AssetType.Other;
        }

        public static bool IsTexture(string extension)
        {
            return TextureExtensions.Contains(extension.ToLower());
        }

        public static bool IsImageFile(string extension)
        {
            return TextureExtensions.Contains(extension.ToLower());
        }

        public static IEnumerable<string> GetAllSupportedExtensions()
        {
            foreach (var ext in UnityPackageExtensions) yield return ext;
            foreach (var ext in TextureExtensions) yield return ext;
            foreach (var ext in ModelExtensions) yield return ext;
            foreach (var ext in AudioExtensions) yield return ext;
        }

        public static string[] GetSearchPatterns()
        {
            return GetAllSupportedExtensions().Select(ext => $"*{ext}").ToArray();
        }
    }
}
