using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Parses DICOM Part 15 (Security and System Management Profiles) XML to extract confidentiality action definitions.
    /// </summary>
    internal static class Part15Parser
    {
        private static readonly XNamespace DocBookNs = "http://docbook.org/ns/docbook";
        private static readonly Regex TagPattern = new Regex(@"\(([0-9A-Fa-fxX]{4}),([0-9A-Fa-fxX]{4})\)", RegexOptions.Compiled);

        /// <summary>
        /// Parses confidentiality action definitions from Part 15 XML document (Table E.1-1).
        /// </summary>
        /// <param name="doc">The parsed XML document.</param>
        /// <returns>Enumerable of confidentiality action definitions.</returns>
        public static IEnumerable<ConfidentialityActionDefinition> ParseConfidentialityActions(XDocument doc)
        {
            // Find the table with xml:id="table_E.1-1" or caption containing "Application Level Confidentiality Profile Attributes"
            var tables = doc.Descendants(DocBookNs + "table");
            var actionTable = tables.FirstOrDefault(t =>
            {
                var xmlId = t.Attribute(XNamespace.Xml + "id")?.Value;
                if (xmlId == "table_E.1-1")
                    return true;

                var caption = t.Element(DocBookNs + "caption")?.Value;
                return caption != null && caption.Contains("Application Level Confidentiality Profile Attributes");
            });

            if (actionTable == null)
            {
                yield break; // No table found, return empty
            }

            // Find tbody
            var tbody = actionTable.Descendants(DocBookNs + "tbody").FirstOrDefault();
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
                var name = GetCellText(cells[0]);
                if (string.IsNullOrWhiteSpace(name))
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

                // Handle masked tags (with 'x') - use 00 in place of xx
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

                // Table structure (15 columns total):
                // 0: Attribute Name
                // 1: Tag
                // 2: Retired (Y/N)
                // 3: In Std Composite IOD (Y/N)
                // 4: Basic Profile
                // 5: Retain Safe Private Option
                // 6: Retain UIDs Option
                // 7: Retain Device Identity Option
                // 8: Retain Institution Identity Option
                // 9: Retain Patient Characteristics Option
                // 10: Retain Long Full Dates Option
                // 11: Retain Long Modified Dates Option
                // 12: Clean Description Option
                // 13: Clean Structured Content Option
                // 14: Clean Graphics Option

                var basicAction = GetActionCode(cells, 4);
                var retainSafePrivate = GetActionCode(cells, 5);
                var retainUids = GetActionCode(cells, 6);
                var retainDeviceIdentity = GetActionCode(cells, 7);
                var retainInstitutionIdentity = GetActionCode(cells, 8);
                var retainPatientCharacteristics = GetActionCode(cells, 9);
                var retainLongFullDates = GetActionCode(cells, 10);
                var retainLongModifDates = GetActionCode(cells, 11);
                var cleanDesc = GetActionCode(cells, 12);
                var cleanStructuredContent = GetActionCode(cells, 13);
                var cleanGraph = GetActionCode(cells, 14);

                yield return new ConfidentialityActionDefinition(
                    group,
                    element,
                    name,
                    basicAction,
                    retainSafePrivate,
                    retainUids,
                    retainDeviceIdentity,
                    retainInstitutionIdentity,
                    retainPatientCharacteristics,
                    retainLongFullDates,
                    retainLongModifDates,
                    cleanDesc,
                    cleanStructuredContent,
                    cleanGraph);
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

        private static string GetActionCode(List<XElement> cells, int index)
        {
            if (index >= cells.Count)
            {
                return string.Empty;
            }

            var text = GetCellText(cells[index]);
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            // Clean up action codes - remove footnote markers like *, strip whitespace
            // Valid codes: D, Z, X, K, C, U, and combinations like Z/D, X/Z, X/D, X/Z/D, X/Z/U*
            var cleaned = new StringBuilder();
            foreach (var ch in text)
            {
                if (char.IsLetter(ch) || ch == '/')
                {
                    cleaned.Append(ch);
                }
            }

            return cleaned.ToString();
        }
    }
}
