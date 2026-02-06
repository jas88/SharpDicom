using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpDicom.Analyzers.Analyzers;

/// <summary>
/// Detects SharpDicom compat layer usage for step 2 migration (compat to native SharpDicom).
/// </summary>
/// <remarks>
/// Reports informational diagnostics on compat layer using directives and type usage.
/// These are not errors -- they indicate code that has completed step 1 migration
/// (fo-dicom to compat) and is ready for step 2 (compat to native SharpDicom).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompatUsageAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor UsingDirectiveRule = new(
        id: DiagnosticIds.CompatUsingDirective,
        title: "Compat layer using directive detected",
        messageFormat: "Using directive '{0}' references SharpDicom compat layer. Consider migrating to native SharpDicom.",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Detects using directives that reference SharpDicom compat layer namespaces. " +
                     "Apply the code fix to rewrite to native SharpDicom as the final migration step.");

    private static readonly DiagnosticDescriptor TypeUsageRule = new(
        id: DiagnosticIds.CompatTypeUsage,
        title: "Compat layer type usage detected",
        messageFormat: "Type '{0}' is from the SharpDicom compat layer. Consider migrating to native SharpDicom.",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Detects usage of types from SharpDicom compat layer namespaces. " +
                     "These types wrap native SharpDicom types; consider using the native types directly.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UsingDirectiveRule, TypeUsageRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Detect compat layer using directives
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);

        // Detect compat layer type usage via semantic analysis on syntax nodes
        // (SymbolKind.NamedType only fires on declarations, not usage sites in user code)
        context.RegisterSyntaxNodeAction(
            AnalyzeTypeUsage,
            SyntaxKind.IdentifierName,
            SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not UsingDirectiveSyntax usingDirective)
            return;

        var nameText = usingDirective.Name?.ToString();
        if (nameText == null)
            return;

        if (IsCompatNamespace(nameText))
        {
            var diagnostic = Diagnostic.Create(
                UsingDirectiveRule,
                usingDirective.GetLocation(),
                nameText);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeTypeUsage(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;

        // For ObjectCreationExpression, inspect the type being constructed.
        // Skip the IdentifierName that is a child of ObjectCreationExpression
        // to avoid double-reporting on "new SomeCompatType()".
        ITypeSymbol? typeSymbol;
        if (node is ObjectCreationExpressionSyntax creation)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken);
            typeSymbol = typeInfo.Type;
        }
        else
        {
            // IdentifierName -- skip if parent is an ObjectCreationExpression (already handled above)
            if (node.Parent is ObjectCreationExpressionSyntax)
                return;

            // Also skip if this is part of a using directive (handled by SD0010)
            if (node.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
                return;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken);
            typeSymbol = symbolInfo.Symbol as ITypeSymbol
                         ?? (symbolInfo.Symbol as IMethodSymbol)?.ContainingType;

            // If GetSymbolInfo didn't yield a type, try GetTypeInfo (covers cast expressions, etc.)
            if (typeSymbol is null)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(node, context.CancellationToken);
                typeSymbol = typeInfo.Type;
            }
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
            return;

        var containingNamespace = namedType.ContainingNamespace?.ToDisplayString();
        if (containingNamespace == null || !IsCompatNamespace(containingNamespace))
            return;

        var diagnostic = Diagnostic.Create(
            TypeUsageRule,
            node.GetLocation(),
            namedType.Name);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Determines whether the given namespace string is a SharpDicom compat layer namespace.
    /// </summary>
    internal static bool IsCompatNamespace(string namespaceName)
    {
        // FoDicom5 compat: SharpDicom.FoDicom5.Compat or SharpDicom.FoDicom5.Compat.*
        if (namespaceName.Equals("SharpDicom.FoDicom5.Compat", System.StringComparison.Ordinal) ||
            namespaceName.StartsWith("SharpDicom.FoDicom5.Compat.", System.StringComparison.Ordinal))
        {
            return true;
        }

        // FoDicom4 compat: SharpDicom.FoDicom4.Compat or SharpDicom.FoDicom4.Compat.*
        if (namespaceName.Equals("SharpDicom.FoDicom4.Compat", System.StringComparison.Ordinal) ||
            namespaceName.StartsWith("SharpDicom.FoDicom4.Compat.", System.StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
