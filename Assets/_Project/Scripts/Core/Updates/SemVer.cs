using System;

namespace Vent.Core.Updates
{
    /// <summary>
    /// The subset of semantic versioning the updater needs: three numbers and an optional
    /// prerelease tag. Engine-free and unit tested, like every other rule in the project —
    /// getting the comparison wrong either hides a release or offers one forever.
    /// </summary>
    [Serializable]
    public readonly struct SemVer : IComparable<SemVer>, IEquatable<SemVer>
    {
        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;

        /// <summary>The bit after '-', empty for a normal release. "0.2.0-rc1" → "rc1".</summary>
        public readonly string PreRelease;

        public SemVer(int major, int minor, int patch, string preRelease = "")
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease ?? string.Empty;
        }

        public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

        /// <summary>Accepts "0.2.0", "v0.2.0" and "0.2.0-rc1". Anything else fails rather than guessing.</summary>
        public static bool TryParse(string text, out SemVer version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string s = text.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            {
                s = s.Substring(1);
            }

            string pre = string.Empty;
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                pre = s.Substring(dash + 1);
                s = s.Substring(0, dash);
                if (pre.Length == 0)
                {
                    return false;
                }
            }

            // Build metadata ("+abc") carries no ordering, so drop it.
            int plus = pre.IndexOf('+');
            if (plus >= 0)
            {
                pre = pre.Substring(0, plus);
            }

            string[] parts = s.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!TryPart(parts[0], out int major) || !TryPart(parts[1], out int minor) || !TryPart(parts[2], out int patch))
            {
                return false;
            }

            version = new SemVer(major, minor, patch, pre);
            return true;
        }

        private static bool TryPart(string text, out int value)
        {
            return int.TryParse(text, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        public int CompareTo(SemVer other)
        {
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;

            // 1.0.0-rc1 precedes 1.0.0. Two prereleases compare as plain text, which is
            // enough for "rc1" < "rc2" and is not worth more than that here.
            if (IsPreRelease && !other.IsPreRelease) return -1;
            if (!IsPreRelease && other.IsPreRelease) return 1;
            return string.CompareOrdinal(PreRelease, other.PreRelease);
        }

        public bool Equals(SemVer other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is SemVer other && Equals(other);
        public override int GetHashCode() => (Major, Minor, Patch, PreRelease).GetHashCode();

        public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
        public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
        public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
        public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
        public static bool operator ==(SemVer a, SemVer b) => a.CompareTo(b) == 0;
        public static bool operator !=(SemVer a, SemVer b) => a.CompareTo(b) != 0;

        public override string ToString() =>
            IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
    }
}
