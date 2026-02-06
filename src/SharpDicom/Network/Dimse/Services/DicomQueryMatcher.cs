using System;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Provides DICOM wildcard matching and query utilities for C-FIND SCP operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class implements matching rules defined in DICOM PS3.4 C.2.2.2:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Wildcard matching: * matches any sequence, ? matches single character</description></item>
    ///   <item><description>SQL LIKE translation for database queries</description></item>
    ///   <item><description>Return key filtering per PS3.4 C.2.2</description></item>
    /// </list>
    /// <para>
    /// Matching is case-sensitive for all VRs except PN (Person Name) which is case-insensitive.
    /// UIDs (UI VR) do not support wildcard matching per the DICOM standard.
    /// </para>
    /// </remarks>
    public static class DicomQueryMatcher
    {
        /// <summary>
        /// Translates a DICOM wildcard pattern to a SQL LIKE pattern.
        /// </summary>
        /// <param name="dicomPattern">
        /// The DICOM pattern where * matches any sequence and ? matches a single character.
        /// </param>
        /// <returns>
        /// A tuple of (sqlLikePattern, hasWildcard) where sqlLikePattern uses % and _ for wildcards,
        /// and literal %, _, and \ characters are escaped with backslash.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Use the returned pattern with SQL LIKE and ESCAPE '\'.
        /// Example: <c>WHERE column LIKE $pattern ESCAPE '\'</c>
        /// </para>
        /// <para>
        /// Per DICOM PS3.4 C.2.2.2.4, DICOM wildcards are:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>* - matches zero or more characters</description></item>
        ///   <item><description>? - matches exactly one character</description></item>
        /// </list>
        /// </remarks>
        public static (string SqlLikePattern, bool HasWildcard) DicomWildcardToSqlLike(string dicomPattern)
        {
            if (string.IsNullOrEmpty(dicomPattern))
                return (dicomPattern ?? string.Empty, false);

            var sb = new StringBuilder(dicomPattern.Length + 4);
            bool hasWildcard = false;

            foreach (char c in dicomPattern)
            {
                switch (c)
                {
                    case '*':
                        sb.Append('%');
                        hasWildcard = true;
                        break;
                    case '?':
                        sb.Append('_');
                        hasWildcard = true;
                        break;
                    case '%':
                        sb.Append(@"\%");
                        break;
                    case '_':
                        sb.Append(@"\_");
                        break;
                    case '\\':
                        sb.Append(@"\\");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return (sb.ToString(), hasWildcard);
        }

        /// <summary>
        /// Determines whether a DICOM value contains wildcard characters (* or ?).
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value contains * or ?; false otherwise.</returns>
        public static bool HasDicomWildcard(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int i = 0; i < value!.Length; i++)
            {
                if (value[i] == '*' || value[i] == '?')
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Filters a matching dataset to contain only tags requested in the query identifier.
        /// </summary>
        /// <param name="match">The full matching dataset from the data source.</param>
        /// <param name="requestIdentifier">
        /// The query identifier from the C-FIND request, containing matching keys (with values)
        /// and return keys (zero-length values).
        /// </param>
        /// <returns>
        /// A new <see cref="DicomDataset"/> containing only the tags present in the request identifier,
        /// plus QueryRetrieveLevel which is always included.
        /// </returns>
        /// <remarks>
        /// Per DICOM PS3.4 C.2.2, the SCP response should contain only:
        /// <list type="bullet">
        ///   <item><description>All Required Keys from the request</description></item>
        ///   <item><description>Supported Optional Keys from the request</description></item>
        ///   <item><description>QueryRetrieveLevel (always included)</description></item>
        /// </list>
        /// Tags not present in the request identifier are excluded from the response.
        /// </remarks>
        public static DicomDataset FilterReturnKeys(DicomDataset match, DicomDataset requestIdentifier)
        {
            var result = new DicomDataset();

            // Always include QueryRetrieveLevel
            var qrLevel = match[DicomTag.QueryRetrieveLevel];
            if (qrLevel != null)
            {
                result.Add(qrLevel);
            }

            // Include only tags present in the request identifier
            foreach (var element in requestIdentifier)
            {
                // Skip QueryRetrieveLevel since we already added it
                if (element.Tag == DicomTag.QueryRetrieveLevel)
                    continue;

                var matchElement = match[element.Tag];
                if (matchElement != null)
                {
                    result.Add(matchElement);
                }
                else
                {
                    // Return zero-length element for requested tags not in the match
                    // This indicates the SCP does not have a value for this attribute
                    var vrInfo = DicomVRInfo.GetInfo(element.VR);
                    if (vrInfo.IsStringVR)
                    {
                        result.Add(new DicomStringElement(element.Tag, element.VR, Array.Empty<byte>()));
                    }
                    else
                    {
                        result.Add(new DicomNumericElement(element.Tag, element.VR, Array.Empty<byte>()));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Performs in-memory wildcard matching of a value against a DICOM pattern.
        /// </summary>
        /// <param name="value">The value to match.</param>
        /// <param name="pattern">The DICOM wildcard pattern (* and ? wildcards).</param>
        /// <param name="caseInsensitive">
        /// True for case-insensitive matching (used for PN VR);
        /// false for case-sensitive matching (all other VRs).
        /// </param>
        /// <returns>True if the value matches the pattern; false otherwise.</returns>
        /// <remarks>
        /// Per DICOM PS3.4 C.2.2.2.4:
        /// <list type="bullet">
        ///   <item><description>* matches zero or more characters</description></item>
        ///   <item><description>? matches exactly one character</description></item>
        ///   <item><description>Case-insensitive for PN VR only</description></item>
        ///   <item><description>UIDs (UI VR) do not support wildcards</description></item>
        /// </list>
        /// Uses an iterative two-pointer algorithm for O(n*m) worst-case matching.
        /// </remarks>
        public static bool MatchesWildcard(string value, string pattern, bool caseInsensitive)
        {
            if (string.IsNullOrEmpty(pattern))
                return string.IsNullOrEmpty(value);

            if (string.IsNullOrEmpty(value))
            {
                // Empty value only matches all-star pattern
                for (int i = 0; i < pattern.Length; i++)
                {
                    if (pattern[i] != '*')
                        return false;
                }
                return true;
            }

            // Iterative two-pointer algorithm
            int vi = 0, pi = 0;
            int starIdx = -1, matchIdx = 0;

            while (vi < value.Length)
            {
                if (pi < pattern.Length && (pattern[pi] == '?' || CharsEqual(value[vi], pattern[pi], caseInsensitive)))
                {
                    vi++;
                    pi++;
                }
                else if (pi < pattern.Length && pattern[pi] == '*')
                {
                    starIdx = pi;
                    matchIdx = vi;
                    pi++;
                }
                else if (starIdx >= 0)
                {
                    pi = starIdx + 1;
                    matchIdx++;
                    vi = matchIdx;
                }
                else
                {
                    return false;
                }
            }

            // Check remaining pattern characters are all *
            while (pi < pattern.Length && pattern[pi] == '*')
            {
                pi++;
            }

            return pi == pattern.Length;
        }

        private static bool CharsEqual(char a, char b, bool caseInsensitive)
        {
            if (a == b) return true;
            if (!caseInsensitive) return false;
            return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
        }
    }
}
