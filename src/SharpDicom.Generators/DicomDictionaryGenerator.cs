using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SharpDicom.Generators
{
    /// <summary>
    /// Incremental source generator that parses NEMA DICOM standard XML files
    /// and generates DicomTag and DicomUID classes with static dictionary members.
    /// </summary>
    [Generator]
    public class DicomDictionaryGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Initializes the incremental generator pipeline.
        /// </summary>
        /// <param name="context">The generator initialization context.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Filter for part06.xml additional file
            var part6Xml = context.AdditionalTextsProvider
                .Where(static file => file.Path.EndsWith("part06.xml", System.StringComparison.Ordinal));

            // Parse tags from Part 6
            var tags = part6Xml
                .Select(static (text, ct) =>
                {
                    try
                    {
                        var content = text.GetText(ct)?.ToString();
                        if (string.IsNullOrEmpty(content))
                        {
                            return ImmutableArray<Parsing.TagDefinition>.Empty;
                        }

                        var doc = XDocument.Parse(content);
                        return Parsing.Part6Parser.ParseTags(doc).ToImmutableArray();
                    }
                    catch
                    {
                        // Return empty on parse error - don't fail build
                        return ImmutableArray<Parsing.TagDefinition>.Empty;
                    }
                })
                .Where(static tags => !tags.IsEmpty)
                .SelectMany(static (tags, _) => tags)
                .Collect();

            // Parse UIDs from Part 6
            var uids = part6Xml
                .Select(static (text, ct) =>
                {
                    try
                    {
                        var content = text.GetText(ct)?.ToString();
                        if (string.IsNullOrEmpty(content))
                        {
                            return ImmutableArray<Parsing.UidDefinition>.Empty;
                        }

                        var doc = XDocument.Parse(content);
                        return Parsing.Part6Parser.ParseUids(doc).ToImmutableArray();
                    }
                    catch
                    {
                        // Return empty on parse error - don't fail build
                        return ImmutableArray<Parsing.UidDefinition>.Empty;
                    }
                })
                .Where(static uids => !uids.IsEmpty)
                .SelectMany(static (uids, _) => uids)
                .Collect();

            // Filter for part07.xml additional file
            var part7Xml = context.AdditionalTextsProvider
                .Where(static file => file.Path.EndsWith("part07.xml", System.StringComparison.Ordinal));

            // Parse command tags from Part 7
            var commandTags = part7Xml
                .Select(static (text, ct) =>
                {
                    try
                    {
                        var content = text.GetText(ct)?.ToString();
                        if (string.IsNullOrEmpty(content))
                        {
                            return ImmutableArray<Parsing.TagDefinition>.Empty;
                        }

                        var doc = XDocument.Parse(content);
                        return Parsing.Part7Parser.ParseCommandTags(doc).ToImmutableArray();
                    }
                    catch
                    {
                        // Return empty on parse error - don't fail build
                        return ImmutableArray<Parsing.TagDefinition>.Empty;
                    }
                })
                .Where(static tags => !tags.IsEmpty)
                .SelectMany(static (tags, _) => tags)
                .Collect();

            // Combine Part 6 and Part 7 tags
            var allTags = tags.Combine(commandTags)
                .Select(static (combined, _) =>
                {
                    var (part6Tags, part7Tags) = combined;
                    return part6Tags.AddRange(part7Tags);
                });

            // Register DicomTag.Generated.cs output
            context.RegisterSourceOutput(allTags,
                static (spc, tagArray) =>
                {
                    if (tagArray.IsEmpty)
                    {
                        return;
                    }

                    var source = Emitters.TagEmitter.Emit(tagArray);
                    spc.AddSource("DicomTag.Generated.cs",
                        SourceText.From(source, Encoding.UTF8));
                });

            // Register DicomUID.Generated.cs output
            context.RegisterSourceOutput(uids,
                static (spc, uidArray) =>
                {
                    if (uidArray.IsEmpty)
                    {
                        return;
                    }

                    var source = Emitters.UidEmitter.Emit(uidArray);
                    spc.AddSource("DicomUID.Generated.cs",
                        SourceText.From(source, Encoding.UTF8));
                });

            // Register TransferSyntax.Generated.cs output (filter UIDs by type)
            context.RegisterSourceOutput(uids,
                static (spc, uidArray) =>
                {
                    if (uidArray.IsEmpty)
                    {
                        return;
                    }

                    var transferSyntaxes = uidArray
                        .Where(u => u.Type == "Transfer Syntax")
                        .ToImmutableArray();

                    if (transferSyntaxes.IsEmpty)
                    {
                        return;
                    }

                    var source = Emitters.TransferSyntaxEmitter.Emit(transferSyntaxes);
                    spc.AddSource("TransferSyntax.Generated.cs",
                        SourceText.From(source, Encoding.UTF8));
                });

            // Register DicomDictionary.Generated.cs output (needs both tags and UIDs)
            var combined = allTags.Combine(uids);
            context.RegisterSourceOutput(combined,
                static (spc, data) =>
                {
                    var (tagArray, uidArray) = data;
                    if (tagArray.IsEmpty || uidArray.IsEmpty)
                    {
                        return;
                    }

                    var source = Emitters.DictionaryEmitter.Emit(tagArray, uidArray);
                    spc.AddSource("DicomDictionary.Generated.cs",
                        SourceText.From(source, Encoding.UTF8));
                });

            // Filter for private dictionary XML files
            var privateXmls = context.AdditionalTextsProvider
                .Where(static file =>
                    file.Path.Contains("dicom-private-dicts") &&
                    file.Path.EndsWith(".xml", System.StringComparison.Ordinal));

            // Parse private tags from all vendor XMLs
            var privateTags = privateXmls
                .Select(static (text, ct) =>
                {
                    try
                    {
                        var content = text.GetText(ct)?.ToString();
                        if (string.IsNullOrEmpty(content))
                        {
                            return ImmutableArray<Parsing.PrivateTagDefinition>.Empty;
                        }

                        var doc = XDocument.Parse(content);
                        return Parsing.PrivateDictParser.ParsePrivateTags(doc).ToImmutableArray();
                    }
                    catch
                    {
                        // Return empty on parse error - don't fail build
                        return ImmutableArray<Parsing.PrivateTagDefinition>.Empty;
                    }
                })
                .Where(static tags => !tags.IsEmpty)
                .SelectMany(static (tags, _) => tags)
                .Collect();

            // Register VendorDictionary.Generated.cs output
            context.RegisterSourceOutput(privateTags,
                static (spc, tagArray) =>
                {
                    if (tagArray.IsEmpty)
                    {
                        return;
                    }

                    var source = Emitters.VendorDictionaryEmitter.Emit(tagArray);
                    spc.AddSource("VendorDictionary.Generated.cs",
                        SourceText.From(source, Encoding.UTF8));
                });
        }
    }
}
