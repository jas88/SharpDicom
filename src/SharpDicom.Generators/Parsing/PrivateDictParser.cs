using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Parses private tag definitions from malaterre/dicom-private-dicts XML format.
    /// </summary>
    internal static class PrivateDictParser
    {
        /// <summary>
        /// Parses private tag definitions from an XML document.
        /// </summary>
        /// <param name="doc">The XML document containing private tag entries.</param>
        /// <returns>An enumerable of private tag definitions.</returns>
        public static IEnumerable<PrivateTagDefinition> ParsePrivateTags(XDocument doc)
        {
            // Parse <entry> elements at document root or under <dict>
            var entries = doc.Descendants("entry");

            foreach (var entry in entries)
            {
                var owner = entry.Attribute("owner")?.Value;
                var groupStr = entry.Attribute("group")?.Value;
                var elementStr = entry.Attribute("element")?.Value;
                var vr = entry.Attribute("vr")?.Value;
                var vm = entry.Attribute("vm")?.Value ?? "1";
                var name = entry.Attribute("name")?.Value;

                if (string.IsNullOrEmpty(owner) ||
                    string.IsNullOrEmpty(groupStr) ||
                    string.IsNullOrEmpty(elementStr) ||
                    string.IsNullOrEmpty(vr) ||
                    string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // Parse group
                if (!ushort.TryParse(groupStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var group))
                {
                    continue;
                }

                // Parse element offset from "xx04" or "1004" format
                // elementStr is validated non-null by the check above
                var offset = ParseElementOffset(elementStr!);
                if (offset == null)
                {
                    continue;
                }

                // Generate keyword from name (remove spaces, special chars)
                // name is validated non-null by the check above
                var keyword = GenerateKeyword(name!);

                // owner, vr, name are validated non-null by the check above
                yield return new PrivateTagDefinition(
                    owner!, group, offset.Value, vr!, vm, name!, keyword);
            }
        }

        private static byte? ParseElementOffset(string elementStr)
        {
            // Handle "xx04" format - extract last two hex digits
            if (elementStr.StartsWith("xx", StringComparison.OrdinalIgnoreCase))
            {
                var offsetPart = elementStr.Substring(2);
                if (byte.TryParse(offsetPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset))
                {
                    return offset;
                }
            }
            // Handle "1004" format - extract low byte
            else if (elementStr.Length == 4)
            {
                if (ushort.TryParse(elementStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var element))
                {
                    return (byte)(element & 0xFF);
                }
            }

            return null;
        }

        private static string GenerateKeyword(string name)
        {
            // Convert "Number of Images" to "NumberOfImages"
            var chars = new List<char>();
            bool capitalizeNext = true;

            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    chars.Add(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                    capitalizeNext = false;
                }
                else
                {
                    capitalizeNext = true;
                }
            }

            return chars.Count > 0 ? new string(chars.ToArray()) : name;
        }
    }
}
