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
/// Code fix provider that rewrites fo-dicom using directives to compat layer namespaces (step 1 migration).
/// </summary>
/// <remarks>
/// Rewriting patterns:
/// <list type="bullet">
/// <item><c>using FellowOakDicom;</c> becomes <c>using SharpDicom.FoDicom5.Compat.FellowOakDicom;</c></item>
/// <item><c>using FellowOakDicom.Network;</c> becomes <c>using SharpDicom.FoDicom5.Compat.FellowOakDicom.Network;</c></item>
/// <item><c>using Dicom;</c> becomes <c>using SharpDicom.FoDicom4.Compat.Dicom;</c></item>
/// <item><c>using Dicom.Network;</c> becomes <c>using SharpDicom.FoDicom4.Compat.Dicom.Network;</c></item>
/// </list>
/// General rule: prepend <c>SharpDicom.FoDicom5.Compat.</c> for FellowOakDicom namespaces,
/// <c>SharpDicom.FoDicom4.Compat.</c> for Dicom namespaces.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FoDicomToCompatFix))]
[Shared]
public sealed class FoDicomToCompatFix : CodeFixProvider
{
    private const string FoDicom5Prefix = "SharpDicom.FoDicom5.Compat.";
    private const string FoDicom4Prefix = "SharpDicom.FoDicom4.Compat.";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.FoDicomUsingDirective);

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

        string prefix;
        string title;
        if (nameText.Equals("FellowOakDicom", System.StringComparison.Ordinal) ||
            nameText.StartsWith("FellowOakDicom.", System.StringComparison.Ordinal))
        {
            prefix = FoDicom5Prefix;
            title = "Replace with SharpDicom.FoDicom5.Compat";
        }
        else if (nameText.Equals("Dicom", System.StringComparison.Ordinal) ||
                 nameText.StartsWith("Dicom.", System.StringComparison.Ordinal))
        {
            prefix = FoDicom4Prefix;
            title = "Replace with SharpDicom.FoDicom4.Compat";
        }
        else
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: ct => ReplaceUsingDirectiveAsync(context.Document, usingDirective, prefix, ct),
                equivalenceKey: nameof(FoDicomToCompatFix)),
            diagnostic);
    }

    private static async Task<Document> ReplaceUsingDirectiveAsync(
        Document document,
        UsingDirectiveSyntax usingDirective,
        string prefix,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var originalName = usingDirective.Name?.ToString();
        if (originalName == null)
            return document;

        var newNameText = prefix + originalName;
        var newName = SyntaxFactory.ParseName(newNameText)
            .WithTriviaFrom(usingDirective.Name!);

        var newUsingDirective = usingDirective.WithName(newName);
        var newRoot = root.ReplaceNode(usingDirective, newUsingDirective);

        return document.WithSyntaxRoot(newRoot);
    }
}
