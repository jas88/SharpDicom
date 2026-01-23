using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpDicom.Tests.Generators;

/// <summary>
/// In-memory implementation of AdditionalText for testing source generators.
/// </summary>
internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    /// <summary>
    /// Initializes a new instance with the specified path and content.
    /// </summary>
    /// <param name="path">The file path (used for matching in generators).</param>
    /// <param name="content">The file content.</param>
    public InMemoryAdditionalText(string path, string content)
    {
        Path = path;
        _text = SourceText.From(content, Encoding.UTF8);
    }

    /// <inheritdoc />
    public override string Path { get; }

    /// <inheritdoc />
    public override SourceText? GetText(CancellationToken cancellationToken = default)
        => _text;
}
