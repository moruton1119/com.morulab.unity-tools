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

        public List<BoothPackage> packages = new List<BoothPackage>();
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
}
