using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpDicom.Analyzers.Analyzers;

namespace SharpDicom.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider that rewrites compat layer using directives to native SharpDicom namespaces (step 2 migration).
/// </summary>
/// <remarks>
/// Rewriting patterns:
/// <list type="bullet">
/// <item><c>using SharpDicom.FoDicom5.Compat.FellowOakDicom;</c> becomes <c>using SharpDicom.Data;</c></item>
/// <item><c>using SharpDicom.FoDicom5.Compat.FellowOakDicom.Network;</c> becomes <c>using SharpDicom.Network;</c></item>
/// <item><c>using SharpDicom.FoDicom5.Compat.FellowOakDicom.Network.Client;</c> becomes <c>using SharpDicom.Network;</c></item>
/// <item><c>using SharpDicom.FoDicom4.Compat.Dicom;</c> becomes <c>using SharpDicom.Data;</c></item>
/// <item><c>using SharpDicom.FoDicom4.Compat.Dicom.Network;</c> becomes <c>using SharpDicom.Network;</c></item>
/// </list>
/// This is a simplified namespace rewrite. Full API-level migration (e.g., GetSingleValue to GetString)
/// would need per-method fixes, which is a future enhancement.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CompatToNativeFix))]
[Shared]
public sealed class CompatToNativeFix : CodeFixProvider
{
    /// <summary>
    /// Mapping from compat namespace suffixes to native SharpDicom namespaces.
    /// Order matters: more specific patterns first.
    /// </summary>
    private static readonly KeyValuePair<string, string>[] NamespaceMappings = new[]
    {
        // FoDicom5 compat mappings
        new KeyValuePair<string, string>("SharpDicom.FoDicom5.Compat.FellowOakDicom.Network.Client", "SharpDicom.Network"),
        new KeyValuePair<string, string>("SharpDicom.FoDicom5.Compat.FellowOakDicom.Network", "SharpDicom.Network"),
        new KeyValuePair<string, string>("SharpDicom.FoDicom5.Compat.FellowOakDicom", "SharpDicom.Data"),
        // FoDicom4 compat mappings
        new KeyValuePair<string, string>("SharpDicom.FoDicom4.Compat.Dicom.Network", "SharpDicom.Network"),
        new KeyValuePair<string, string>("SharpDicom.FoDicom4.Compat.Dicom", "SharpDicom.Data"),
    };

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.CompatUsingDirective);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        var usingDirective = node as UsingDirectiveSyntax
            ?? node.FirstAncestorOrSelf<UsingDirectiveSyntax>();
        if (usingDirective == null)
            return;

        var nameText = usingDirective.Name?.ToString();
        if (nameText == null)
            return;

        // Find the matching namespace mapping
        string? nativeNamespace = null;
        foreach (var mapping in NamespaceMappings)
        {
            if (nameText.Equals(mapping.Key, StringComparison.Ordinal) ||
                nameText.StartsWith(mapping.Key + ".", StringComparison.Ordinal))
            {
                if (nameText.Equals(mapping.Key, StringComparison.Ordinal))
                {
                    nativeNamespace = mapping.Value;
                }
                else
                {
                    // Sub-namespace: map the prefix and keep the suffix
                    var suffix = nameText.Substring(mapping.Key.Length);
                    nativeNamespace = mapping.Value + suffix;
                }
                break;
            }
        }

        if (nativeNamespace == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace with native SharpDicom",
                createChangedDocument: ct => ReplaceUsingDirectiveAsync(context.Document, usingDirective, nativeNamespace, ct),
                equivalenceKey: nameof(CompatToNativeFix)),
            diagnostic);
    }

    private static async Task<Document> ReplaceUsingDirectiveAsync(
        Document document,
        UsingDirectiveSyntax usingDirective,
        string nativeNamespace,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newName = SyntaxFactory.ParseName(nativeNamespace)
            .WithTriviaFrom(usingDirective.Name!);

        var newUsingDirective = usingDirective.WithName(newName);
        var newRoot = root.ReplaceNode(usingDirective, newUsingDirective);

        return document.WithSyntaxRoot(newRoot);
    }
}
