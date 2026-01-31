using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Parses DICOM Part 15 (Security) XML to extract de-identification action definitions.
    /// </summary>
    internal static class Part15Parser
    {
        private static readonly XNamespace DocBookNs = "http://docbook.org/ns/docbook";
        private static readonly Regex TagPattern = new Regex(@"\(([0-9A-Fa-fxX]{4}),([0-9A-Fa-fxX]{4})\)", RegexOptions.Compiled);

        /// <summary>
        /// Parses de-identification action definitions from Part 15 XML document.
        /// </summary>
        /// <param name="doc">The parsed XML document.</param>
        /// <returns>Enumerable of de-identification action definitions.</returns>
        public static IEnumerable<DeidentificationActionDefinition> ParseDeidentificationActions(XDocument doc)
        {
            // Find the de-identification table by looking for xml:id="table_E.1-1"
            // or caption containing "Application Level Confidentiality Profile Attributes"
            var tables = doc.Descendants(DocBookNs + "table");
            var deidTable = tables.FirstOrDefault(t =>
            {
                var xmlId = t.Attribute(XNamespace.Xml + "id")?.Value;
                if (xmlId == "table_E.1-1")
                    return true;

                var caption = t.Element(DocBookNs + "caption")?.Value;
                return caption != null && caption.Contains("Application Level Confidentiality Profile Attributes");
            });

            if (deidTable == null)
            {
                yield break; // No table found
            }

            // Find tbody
            var tbody = deidTable.Descendants(DocBookNs + "tbody").FirstOrDefault();
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

                // Column 0: Attribute Name
                var attributeName = GetCellText(cells[0]);
                if (string.IsNullOrWhiteSpace(attributeName))
                {
                    continue;
                }

                // Column 1: Tag (GGGG,EEEE)
                var tagText = GetCellText(cells[1]);
                var tagMatch = TagPattern.Match(tagText);
                if (!tagMatch.Success)
                {
                    continue;
                }

                // Parse tag group/element
                var groupText = tagMatch.Groups[1].Value;
                var elementText = tagMatch.Groups[2].Value;

                // Handle masked tags (with 'x')
                if (groupText.Contains('x') || groupText.Contains('X') ||
                    elementText.Contains('x') || elementText.Contains('X'))
                {
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

                // Column 2: Retired (Y/N)
                var isRetired = GetCellText(cells[2]).Equals("Y", StringComparison.OrdinalIgnoreCase);

                // Column 3: In Std IOD (Y/N)
                var inStandardIOD = GetCellText(cells[3]).Equals("Y", StringComparison.OrdinalIgnoreCase);

                // Column 4: Basic Profile action
                var basicProfile = ParseAction(GetCellText(cells[4]));
                if (string.IsNullOrEmpty(basicProfile))
                {
                    basicProfile = "X"; // Default to remove if no action specified
                }

                // Columns 5-14: Optional profile actions (may not all be present)
                var retainSafePrivate = GetActionOrNull(cells, 5);
                var retainUids = GetActionOrNull(cells, 6);
                var retainDeviceId = GetActionOrNull(cells, 7);
                var retainInstitutionId = GetActionOrNull(cells, 8);
                var retainPatientChars = GetActionOrNull(cells, 9);
                var retainLongFull = GetActionOrNull(cells, 10);
                var retainLongModified = GetActionOrNull(cells, 11);
                var cleanDescriptors = GetActionOrNull(cells, 12);
                var cleanStructured = GetActionOrNull(cells, 13);
                var cleanGraphics = GetActionOrNull(cells, 14);

                yield return new DeidentificationActionDefinition(
                    group,
                    element,
                    CleanKeyword(attributeName),
                    isRetired,
                    inStandardIOD,
                    basicProfile,
                    retainSafePrivate ?? string.Empty,
                    retainUids ?? string.Empty,
                    retainDeviceId ?? string.Empty,
                    retainInstitutionId ?? string.Empty,
                    retainPatientChars ?? string.Empty,
                    retainLongFull ?? string.Empty,
                    retainLongModified ?? string.Empty,
                    cleanDescriptors ?? string.Empty,
                    cleanStructured ?? string.Empty,
                    cleanGraphics ?? string.Empty
                );
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

        private static string? GetActionOrNull(List<XElement> cells, int index)
        {
            if (index >= cells.Count)
            {
                return null;
            }

            var text = GetCellText(cells[index]);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return ParseAction(text);
        }

        private static string ParseAction(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            // Clean the text and normalize action codes
            text = text.Trim().ToUpperInvariant();

            // Handle compound actions
            text = text.Replace(" ", "");

            // Map common action patterns
            return text switch
            {
                "D" => "D",
                "Z" => "Z",
                "X" => "X",
                "K" => "K",
                "C" => "C",
                "U" => "U",
                "U*" => "U",
                "Z/D" => "ZD",
                "X/Z" => "XZ",
                "X/D" => "XD",
                "X/Z/D" => "XZD",
                "X/Z/U" => "XZU",
                "X/Z/U*" => "XZU",
                _ => text.Length <= 5 ? text : string.Empty // Allow short codes, skip long text
            };
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
    }
}
