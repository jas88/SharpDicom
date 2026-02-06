using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpDicom.Analyzers.Analyzers;

namespace SharpDicom.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="CompatUsageAnalyzer"/> which detects compat layer using directives
/// for step 2 migration (compat to native SharpDicom).
/// </summary>
[TestFixture]
public sealed class CompatUsageAnalyzerTests
{
    /// <summary>
    /// Detects FoDicom5 compat layer using directive with FellowOakDicom suffix.
    /// </summary>
    [Test]
    public async Task DetectsCompat5FellowOakDicomUsing()
    {
        var test = new CSharpAnalyzerTest<CompatUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using SharpDicom.FoDicom5.Compat.FellowOakDicom;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0010", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
                .WithLocation(1, 1)
                .WithArguments("SharpDicom.FoDicom5.Compat.FellowOakDicom"));
        await test.RunAsync();
    }

    /// <summary>
    /// Detects FoDicom5 compat layer Network sub-namespace using directive.
    /// </summary>
    [Test]
    public async Task DetectsCompat5NetworkUsing()
    {
        var test = new CSharpAnalyzerTest<CompatUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using SharpDicom.FoDicom5.Compat.FellowOakDicom.Network;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0010", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
                .WithLocation(1, 1)
                .WithArguments("SharpDicom.FoDicom5.Compat.FellowOakDicom.Network"));
        await test.RunAsync();
    }

    /// <summary>
    /// Detects FoDicom4 compat layer using directive with Dicom suffix.
    /// </summary>
    [Test]
    public async Task DetectsCompat4DicomUsing()
    {
        var test = new CSharpAnalyzerTest<CompatUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using SharpDicom.FoDicom4.Compat.Dicom;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0010", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
                .WithLocation(1, 1)
                .WithArguments("SharpDicom.FoDicom4.Compat.Dicom"));
        await test.RunAsync();
    }

    /// <summary>
    /// Does not flag native SharpDicom namespaces.
    /// </summary>
    [Test]
    public async Task IgnoresNativeSharpDicom()
    {
        var test = new CSharpAnalyzerTest<CompatUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using SharpDicom.Data;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        // No diagnostics expected
        await test.RunAsync();
    }

    /// <summary>
    /// Does not flag unrelated namespaces.
    /// </summary>
    [Test]
    public async Task IgnoresUnrelatedNamespace()
    {
        var test = new CSharpAnalyzerTest<CompatUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using System.Collections.Generic;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        // No diagnostics expected
        await test.RunAsync();
    }
}
