using System;
using System.Collections.Generic;

namespace MorulabTools.Core
{
    /// <summary>
    /// SemVer 2.0 準拠のバージョンパーサー。
    /// 例: 1.0.0, 1.0.0-beta.1, 1.0.0+build.123
    /// </summary>
    public readonly struct SemVer : IComparable<SemVer>, IEquatable<SemVer>
    {
        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;
        public readonly string PreRelease;  // null or "" = stable, "beta.1" = prerelease
        public readonly string Build;       // +build metadata (比較に使わない)

        public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

        public SemVer(int major, int minor, int patch, string preRelease = null, string build = null)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease ?? "";
            Build = build ?? "";
        }

        /// <summary>
        /// 文字列から SemVer をパースする。
        /// 失敗した場合は例外を投げず false を返す。
        /// </summary>
        public static bool TryParse(string input, out SemVer result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            input = input.Trim();
            if (input.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(1);

            // build metadata を分離
            string build = "";
            int plusIdx = input.IndexOf('+');
            if (plusIdx >= 0)
            {
                build = input.Substring(plusIdx + 1);
                input = input.Substring(0, plusIdx);
            }

            // prerelease を分離
            string preRelease = "";
            int dashIdx = input.IndexOf('-');
            if (dashIdx >= 0)
            {
                preRelease = input.Substring(dashIdx + 1);
                input = input.Substring(0, dashIdx);
            }

            // major.minor.patch をパース
            string[] parts = input.Split('.');
            if (parts.Length < 1) return false;

            if (!int.TryParse(parts[0], out int major)) return false;
            int minor = 0, patch = 0;

            if (parts.Length >= 2 && !int.TryParse(parts[1], out minor)) return false;
            if (parts.Length >= 3 && !int.TryParse(parts[2], out patch)) return false;

            result = new SemVer(major, minor, patch, preRelease, build);
            return true;
        }

        public static SemVer Parse(string input)
        {
            if (TryParse(input, out var result))
                return result;
            throw new FormatException($"Invalid SemVer: {input}");
        }

        public int CompareTo(SemVer other)
        {
            // 1. Major, Minor, Patch を数値で比較
            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            if (Patch != other.Patch) return Patch.CompareTo(other.Patch);

            // 2. Prerelease の比較
            // stable ( prereleaseなし ) > prerelease
            if (string.IsNullOrEmpty(PreRelease) && !string.IsNullOrEmpty(other.PreRelease))
                return 1;
            if (!string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease))
                return -1;

            // 両方 prerelease なら識別子ごとに比較
            if (!string.IsNullOrEmpty(PreRelease) && !string.IsNullOrEmpty(other.PreRelease))
                return ComparePreRelease(PreRelease, other.PreRelease);

            return 0; // 完全に同じ
        }

        private static int ComparePreRelease(string a, string b)
        {
            var partsA = a.Split('.');
            var partsB = b.Split('.');
            int len = Math.Max(partsA.Length, partsB.Length);

            for (int i = 0; i < len; i++)
            {
                string pa = i < partsA.Length ? partsA[i] : null;
                string pb = i < partsB.Length ? partsB[i] : null;

                // pa が null → a の方が短い → a < b
                if (pa == null) return -1;
                if (pb == null) return 1;

                // 両方数値なら数値比較
                bool aIsNum = int.TryParse(pa, out int na);
                bool bIsNum = int.TryParse(pb, out int nb);

                if (aIsNum && bIsNum)
                {
                    if (na != nb) return na.CompareTo(nb);
                }
                else if (aIsNum && !bIsNum)
                {
                    return -1; // 数値 < 文字列
                }
                else if (!aIsNum && bIsNum)
                {
                    return 1; // 文字列 > 数値
                }
                else
                {
                    // 両方文字列なら辞書順
                    int cmp = string.Compare(pa, pb, StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                }
            }

            return partsA.Length.CompareTo(partsB.Length);
        }

        public bool Equals(SemVer other)
            => Major == other.Major && Minor == other.Minor && Patch == other.Patch
               && PreRelease == other.PreRelease;

        public override bool Equals(object obj) => obj is SemVer other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

        public override string ToString()
        {
            string s = $"{Major}.{Minor}.{Patch}";
            if (!string.IsNullOrEmpty(PreRelease)) s += $"-{PreRelease}";
            if (!string.IsNullOrEmpty(Build)) s += $"+{Build}";
            return s;
        }

        public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
        public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
        public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
        public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
        public static bool operator ==(SemVer a, SemVer b) => a.Equals(b);
        public static bool operator !=(SemVer a, SemVer b) => !a.Equals(b);
    }
}
