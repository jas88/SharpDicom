using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Parses DICOM Part 6 (Data Dictionary) XML to extract tag and UID definitions.
    /// </summary>
    internal static class Part6Parser
    {
        private static readonly XNamespace DocBookNs = "http://docbook.org/ns/docbook";
        private static readonly Regex TagPattern = new Regex(@"\(([0-9A-Fa-fxX]{4}),([0-9A-Fa-fxX]{4})\)", RegexOptions.Compiled);

        /// <summary>
        /// Parses tag definitions from Part 6 XML document.
        /// </summary>
        /// <param name="doc">The parsed XML document.</param>
        /// <returns>Enumerable of tag definitions.</returns>
        public static IEnumerable<TagDefinition> ParseTags(XDocument doc)
        {
            // Find the data dictionary table by looking for "Registry of DICOM Data Elements"
            // in either <title> or <caption> elements
            var tables = doc.Descendants(DocBookNs + "table");
            var dataTable = tables.FirstOrDefault(t =>
            {
                var title = t.Element(DocBookNs + "title")?.Value;
                if (title != null && title.Contains("Registry of DICOM Data Elements"))
                    return true;

                var caption = t.Element(DocBookNs + "caption")?.Value;
                return caption != null && caption.Contains("Registry of DICOM Data Elements");
            });

            if (dataTable == null)
            {
                yield break; // No table found, return empty
            }

            // Find tbody
            var tbody = dataTable.Descendants(DocBookNs + "tbody").FirstOrDefault();
            if (tbody == null)
            {
                yield break;
            }

            // Parse each row
            foreach (var row in tbody.Elements(DocBookNs + "tr"))
            {
                var cells = row.Elements(DocBookNs + "td").ToList();
                if (cells.Count < 5)
                {
                    continue; // Skip malformed rows
                }

                // Column 0: Tag (GGGG,EEEE)
                var tagText = GetCellText(cells[0]);
                var tagMatch = TagPattern.Match(tagText);
                if (!tagMatch.Success)
                {
                    continue;
                }

                // Column 1: Name
                var name = GetCellText(cells[1]);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // Column 2: Keyword (with zero-width spaces)
                var keyword = GetCellText(cells[2]);
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                // Remove zero-width spaces and other special chars
                keyword = CleanKeyword(keyword);

                // Column 3: VR
                var vrText = GetCellText(cells[3]);
                var vrs = ParseVRs(vrText);
                if (vrs.Length == 0)
                {
                    continue;
                }

                // Column 4: VM
                var vm = GetCellText(cells[4]);

                // Column 5 (optional): Retired indicator
                var isRetired = IsRetiredRow(row);

                // Parse tag group/element
                var groupText = tagMatch.Groups[1].Value;
                var elementText = tagMatch.Groups[2].Value;

                // Handle masked tags (with 'x')
                if (groupText.Contains('x') || groupText.Contains('X') ||
                    elementText.Contains('x') || elementText.Contains('X'))
                {
                    // For masked tags, use 00 in place of xx
                    groupText = groupText.Replace('x', '0').Replace('X', '0');
                    elementText = elementText.Replace('x', '0').Replace('X', '0');
                }

                if (!ushort.TryParse(groupText, System.Globalization.NumberStyles.HexNumber, null, out var group))
                {
                    continue;
                }

                if (!ushort.TryParse(elementText, System.Globalization.NumberStyles.HexNumber, null, out var element))
                {
                    continue;
                }

                yield return new TagDefinition(group, element, keyword, name, vrs, vm, isRetired);
            }
        }

        /// <summary>
        /// Parses UID definitions from Part 6 XML document.
        /// </summary>
        /// <param name="doc">The parsed XML document.</param>
        /// <returns>Enumerable of UID definitions.</returns>
        public static IEnumerable<UidDefinition> ParseUids(XDocument doc)
        {
            // Find the UID registry table by looking for "UID Values"
            var tables = doc.Descendants(DocBookNs + "table");
            var uidTable = tables.FirstOrDefault(t =>
            {
                var caption = t.Descendants(DocBookNs + "caption").FirstOrDefault()?.Value;
                // Look for the main UID Values table, not Context Group or Template
                return caption != null && caption == "UID Values";
            });

            if (uidTable == null)
            {
                yield break; // No table found, return empty
            }

            // Find tbody
            var tbody = uidTable.Descendants(DocBookNs + "tbody").FirstOrDefault();
            if (tbody == null)
            {
                yield break;
            }

            // Parse each row
            foreach (var row in tbody.Elements(DocBookNs + "tr"))
            {
                var cells = row.Elements(DocBookNs + "td").ToList();
                if (cells.Count < 4)
                {
                    continue; // Skip malformed rows
                }

                // Column 0: UID Value
                var uidValue = GetCellText(cells[0]);
                if (string.IsNullOrWhiteSpace(uidValue))
                {
                    continue;
                }

                // Remove zero-width spaces
                uidValue = CleanUidValue(uidValue);

                // Column 1: UID Name
                var name = GetCellText(cells[1]);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // Column 2: UID Keyword (with zero-width spaces)
                var keyword = GetCellText(cells[2]);
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                // Remove zero-width spaces
                keyword = CleanKeyword(keyword);

                // Column 3: UID Type
                var type = GetCellText(cells[3]);
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                // Check if retired
                var isRetired = IsRetiredRow(row);

                yield return new UidDefinition(uidValue, keyword, name, type, isRetired);
            }
        }

        private static string GetCellText(XElement cell)
        {
            // Get all para elements' text and concatenate
            var paras = cell.Descendants(DocBookNs + "para");
            var sb = new StringBuilder();
            foreach (var para in paras)
            {
                sb.Append(para.Value);
            }
            return sb.ToString().Trim();
        }

        private static string[] ParseVRs(string vrText)
        {
            if (string.IsNullOrWhiteSpace(vrText))
            {
                return Array.Empty<string>();
            }

            // Handle "US or SS", "OB or OW", etc.
            var parts = vrText.Split(new[] { " or ", " Or ", " OR " }, StringSplitOptions.RemoveEmptyEntries);
            var vrs = new List<string>();

            foreach (var part in parts)
            {
                var cleaned = part.Trim();
                if (cleaned.Length == 2 && char.IsUpper(cleaned[0]) && char.IsUpper(cleaned[1]))
                {
                    vrs.Add(cleaned);
                }
                else if (cleaned.Length == 2 && char.IsUpper(cleaned[0]) && char.IsLower(cleaned[1]))
                {
                    // Handle "Ox" patterns
                    vrs.Add(cleaned.ToUpperInvariant());
                }
            }

            return vrs.Count > 0 ? vrs.ToArray() : Array.Empty<string>();
        }

        private static bool IsRetiredRow(XElement row)
        {
            // Check if any cell in the row contains italic emphasis (retired items are italicized)
            var emphases = row.Descendants(DocBookNs + "emphasis");
            foreach (var emphasis in emphases)
            {
                var role = emphasis.Attribute("role")?.Value;
                if (role == "italic")
                {
                    return true;
                }
            }

            // Also check cell text for "(Retired)" marker
            foreach (var cell in row.Elements(DocBookNs + "td"))
            {
                var text = GetCellText(cell);
                if (text.Contains("(Retired)") || text.Contains("Retired"))
                {
                    return true;
                }
            }

            return false;
        }

        private static string CleanKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return string.Empty;
            }

            // Remove zero-width spaces (U+200B and similar)
            var cleaned = new StringBuilder(keyword.Length);
            foreach (var ch in keyword)
            {
                // Keep only normal characters, skip zero-width and control chars
                if (ch >= 32 && ch != '\u200B' && ch != '\u200C' && ch != '\u200D' && ch != '\uFEFF')
                {
                    cleaned.Append(ch);
                }
            }

            return cleaned.ToString();
        }

        private static string CleanUidValue(string uidValue)
        {
            if (string.IsNullOrWhiteSpace(uidValue))
            {
                return string.Empty;
            }

            // Remove zero-width spaces
            var cleaned = new StringBuilder(uidValue.Length);
            foreach (var ch in uidValue)
            {
                // Keep only digits, dots, and normal spaces
                if (char.IsDigit(ch) || ch == '.')
                {
                    cleaned.Append(ch);
                }
            }

            return cleaned.ToString();
        }
    }
}
