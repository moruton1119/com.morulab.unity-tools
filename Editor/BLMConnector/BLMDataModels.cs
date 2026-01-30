using System;
using System.Collections.Generic;

namespace Moruton.BLMConnector
{
    [Serializable]
    public class BoothProduct
    {
        public string id;
        public string name;
        public string shopName;
        public string shopUrl;
        public string thumbnailPath;
        public string thumbnailUrl;
        public string rootFolderPath;
        public string shopSubdomain;
        public string sourceType; // "BLM" or "Local"

        public List<BoothPackage> packages = new List<BoothPackage>();
        public List<BoothAsset> assets = new List<BoothAsset>();
    }

    [Serializable]
    public class BoothPackage
    {
        public string fileName;
        public string fullPath;
        public bool isImported;
        public PackageType type;
    }

    public enum PackageType
    {
        Unknown,
        Main,
        Material,
        FXT,
        Texture,
        Optional
    }

    public enum AssetType
    {
        UnityPackage,
        Texture,
        Model,
        Audio,
        Other
    }

    [Serializable]
    public class BoothAsset
    {
        public string fileName;
        public string fullPath;
        public AssetType assetType;
    }
}
