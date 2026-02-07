using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpDicom.Analyzers.Analyzers;

/// <summary>
/// Detects fo-dicom usage patterns for step 1 migration (fo-dicom to compat layer).
/// </summary>
/// <remarks>
/// Supports both fo-dicom 5.x (<c>FellowOakDicom</c>) and fo-dicom 4.x (<c>Dicom</c>) namespaces.
/// Uses semantic analysis where possible to avoid false positives on user types named "Dicom".
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FoDicomUsageAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor UsingDirectiveRule = new(
        id: DiagnosticIds.FoDicomUsingDirective,
        title: "fo-dicom using directive detected",
        messageFormat: "Using directive '{0}' references fo-dicom. Consider migrating to SharpDicom compat layer.",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Detects using directives that reference fo-dicom namespaces (FellowOakDicom or Dicom). " +
                     "Apply the code fix to rewrite to SharpDicom compat layer as a first migration step.");

    private static readonly DiagnosticDescriptor TypeInstantiationRule = new(
        id: DiagnosticIds.FoDicomTypeInstantiation,
        title: "fo-dicom type usage detected",
        messageFormat: "Type '{0}' belongs to fo-dicom namespace '{1}'. Consider migrating to SharpDicom.",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Detects usage of types from fo-dicom namespaces. " +
                     "Migrate to SharpDicom compat layer first, then to native SharpDicom.");

    private static readonly DiagnosticDescriptor StaticMethodCallRule = new(
        id: DiagnosticIds.FoDicomStaticMethodCall,
        title: "fo-dicom static method call detected",
        messageFormat: "Method '{0}' belongs to fo-dicom type '{1}'. Consider migrating to SharpDicom.",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Detects calls to static methods on fo-dicom types (e.g., DicomFile.Open). " +
                     "Migrate to SharpDicom compat layer first, then to native SharpDicom.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UsingDirectiveRule, TypeInstantiationRule, StaticMethodCallRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Detect using directives for fo-dicom namespaces
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);

        // Detect fo-dicom type usage via semantic analysis on syntax nodes
        // (SymbolKind.NamedType only fires on declarations, not usage sites in user code)
        context.RegisterSyntaxNodeAction(AnalyzeTypeUsage, SyntaxKind.IdentifierName);

        // Detect fo-dicom static method calls
        context.RegisterSyntaxNodeAction(AnalyzeInvocationExpression, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not UsingDirectiveSyntax usingDirective)
            return;

        var nameText = usingDirective.Name?.ToString();
        if (nameText == null)
            return;

        if (IsFoDicomNamespace(nameText))
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

        // Skip if this is part of a using directive (handled by SD0001)
        if (node.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken);
        var typeSymbol = symbolInfo.Symbol as ITypeSymbol
                     ?? (symbolInfo.Symbol as IMethodSymbol)?.ContainingType;

        // If GetSymbolInfo didn't yield a type, try GetTypeInfo (covers cast expressions, etc.)
        if (typeSymbol is null)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(node, context.CancellationToken);
            typeSymbol = typeInfo.Type;
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
            return;

        var containingNamespace = GetFullNamespaceName(namedType.ContainingNamespace);
        if (containingNamespace == null || !IsFoDicomNamespace(containingNamespace))
            return;

        var diagnostic = Diagnostic.Create(
            TypeInstantiationRule,
            node.GetLocation(),
            namedType.Name,
            containingNamespace);
        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        // Use semantic analysis to get the method symbol
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Only flag static methods (instance methods are caught via type usage)
        if (!methodSymbol.IsStatic)
            return;

        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return;

        var containingNamespace = GetFullNamespaceName(containingType.ContainingNamespace);
        if (containingNamespace == null || !IsFoDicomNamespace(containingNamespace))
            return;

        var diagnostic = Diagnostic.Create(
            StaticMethodCallRule,
            invocation.GetLocation(),
            methodSymbol.Name,
            containingType.Name);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Determines whether the given namespace string is a fo-dicom namespace.
    /// </summary>
    internal static bool IsFoDicomNamespace(string namespaceName)
    {
        // fo-dicom 5.x: FellowOakDicom or FellowOakDicom.*
        if (namespaceName.Equals("FellowOakDicom", System.StringComparison.Ordinal) ||
            namespaceName.StartsWith("FellowOakDicom.", System.StringComparison.Ordinal))
        {
            return true;
        }

        // fo-dicom 4.x: Dicom or Dicom.*
        // Be careful: "Dicom" is specific enough as a top-level namespace
        if (namespaceName.Equals("Dicom", System.StringComparison.Ordinal) ||
            namespaceName.StartsWith("Dicom.", System.StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string? GetFullNamespaceName(INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace)
            return null;

        return namespaceSymbol.ToDisplayString();
    }
}
