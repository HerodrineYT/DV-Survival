using System;

namespace DVSurvival.Core
{
    public static class VersionUtil
    {
        public static int Compare(string left, string right)
        {
            Version a;
            Version b;
            if (!TryParse(left, out a)) return TryParse(right, out b) ? -1 : 0;
            if (!TryParse(right, out b)) return 1;
            return a.CompareTo(b);
        }

        public static bool TryParse(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var clean = value.Split('-', '+')[0].Trim();
            var parts = clean.Split('.');
            for (var index = parts.Length; index < 4; index++) clean += ".0";
            return Version.TryParse(clean, out version);
        }
    }
}
