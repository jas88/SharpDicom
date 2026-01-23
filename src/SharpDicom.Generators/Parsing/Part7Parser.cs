using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Parses DICOM Part 7 (Message Exchange) XML to extract command field tag definitions.
    /// </summary>
    internal static class Part7Parser
    {
        private static readonly XNamespace DocBookNs = "http://docbook.org/ns/docbook";
        private static readonly Regex TagPattern = new Regex(@"\(([0-9A-Fa-f]{4}),([0-9A-Fa-f]{4})\)", RegexOptions.Compiled);

        /// <summary>
        /// Parses command field tag definitions (Group 0000) from Part 7 XML document.
        /// </summary>
        /// <param name="doc">The parsed XML document.</param>
        /// <returns>Enumerable of command tag definitions.</returns>
        public static IEnumerable<TagDefinition> ParseCommandTags(XDocument doc)
        {
            // Find tables in Part 7 that contain command field definitions
            // Look for tables with "Command Fields" in title or caption
            var tables = doc.Descendants(DocBookNs + "table");
            var commandTables = tables.Where(t =>
            {
                var title = t.Element(DocBookNs + "title")?.Value ?? string.Empty;
                var caption = t.Descendants(DocBookNs + "caption").FirstOrDefault()?.Value ?? string.Empty;
                return title.Contains("Command Fields") || caption.Contains("Command Fields");
            });

            foreach (var table in commandTables)
            {
                var tbody = table.Descendants(DocBookNs + "tbody").FirstOrDefault();
                if (tbody == null)
                {
                    continue;
                }

                // Parse each row
                foreach (var row in tbody.Elements(DocBookNs + "tr"))
                {
                    var cells = row.Elements(DocBookNs + "td").ToList();
                    if (cells.Count < 4)
                    {
                        continue; // Skip malformed rows
                    }

                    // Column 0: Tag
                    var tagText = GetCellText(cells[0]);
                    var tagMatch = TagPattern.Match(tagText);
                    if (!tagMatch.Success)
                    {
                        continue;
                    }

                    // Column 1: Message Field Name
                    var name = GetCellText(cells[1]);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    // Generate keyword from name (remove spaces, camel case)
                    var keyword = GenerateKeyword(name);
                    if (string.IsNullOrWhiteSpace(keyword))
                    {
                        continue;
                    }

                    // Column 2: VR
                    var vrText = GetCellText(cells[2]);
                    var vrs = ParseVRs(vrText);
                    if (vrs.Length == 0)
                    {
                        continue;
                    }

                    // Column 3: VM (or requirement - use "1" as default)
                    var vm = "1";
                    if (cells.Count > 3)
                    {
                        var vmText = GetCellText(cells[3]);
                        if (!string.IsNullOrWhiteSpace(vmText) && char.IsDigit(vmText[0]))
                        {
                            vm = vmText;
                        }
                    }

                    // Parse tag group/element
                    var groupText = tagMatch.Groups[1].Value;
                    var elementText = tagMatch.Groups[2].Value;

                    if (!ushort.TryParse(groupText, System.Globalization.NumberStyles.HexNumber, null, out var group))
                    {
                        continue;
                    }

                    if (!ushort.TryParse(elementText, System.Globalization.NumberStyles.HexNumber, null, out var element))
                    {
                        continue;
                    }

                    // Command tags are Group 0000, not retired
                    yield return new TagDefinition(group, element, keyword, name, vrs, vm, false);
                }
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
                return new[] { "UN" }; // Default to UN if unknown
            }

            // Handle single VRs
            var cleaned = vrText.Trim();
            if (cleaned.Length == 2)
            {
                return new[] { cleaned };
            }

            // Handle comma-separated or space-separated
            var parts = cleaned.Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var vrs = new List<string>();

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 2 && char.IsUpper(trimmed[0]))
                {
                    vrs.Add(trimmed);
                }
            }

            return vrs.Count > 0 ? vrs.ToArray() : new[] { "UN" };
        }

        private static string GenerateKeyword(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            // Remove special characters and split into words
            var words = name.Split(new[] { ' ', '-', '/', '(', ')', '[', ']' }, System.StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();

            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word))
                {
                    continue;
                }

                // Capitalize first letter, keep rest as-is
                if (char.IsLower(word[0]))
                {
                    sb.Append(char.ToUpperInvariant(word[0]));
                    if (word.Length > 1)
                    {
                        sb.Append(word.Substring(1));
                    }
                }
                else
                {
                    sb.Append(word);
                }
            }

            return sb.ToString();
        }
    }
}
