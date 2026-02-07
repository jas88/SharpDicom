using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpDicom.Analyzers.Analyzers;
using SharpDicom.Analyzers.CodeFixes;

namespace SharpDicom.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="FoDicomToCompatFix"/> which rewrites fo-dicom using directives
/// to compat layer namespaces (step 1 migration).
/// </summary>
[TestFixture]
public sealed class FoDicomToCompatFixTests
{
    /// <summary>
    /// Rewrites fo-dicom 5.x root using directive to FoDicom5 compat layer.
    /// </summary>
    [Test]
    public async Task FixesFoDicom5Using()
    {
        var test = new CSharpCodeFixTest<FoDicomUsageAnalyzer, FoDicomToCompatFix, DefaultVerifier>
        {
            TestCode = "using FellowOakDicom;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.FoDicom5.Compat.FellowOakDicom;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(1, 1)
                .WithArguments("FellowOakDicom"));
        await test.RunAsync();
    }

    /// <summary>
    /// Rewrites fo-dicom 5.x Network using directive to FoDicom5 compat layer.
    /// </summary>
    [Test]
    public async Task FixesFoDicom5NetworkUsing()
    {
        var test = new CSharpCodeFixTest<FoDicomUsageAnalyzer, FoDicomToCompatFix, DefaultVerifier>
        {
            TestCode = "using FellowOakDicom.Network;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.FoDicom5.Compat.FellowOakDicom.Network;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(1, 1)
                .WithArguments("FellowOakDicom.Network"));
        await test.RunAsync();
    }

    /// <summary>
    /// Rewrites fo-dicom 5.x Network.Client using directive to FoDicom5 compat layer.
    /// </summary>
    [Test]
    public async Task FixesFoDicom5ClientUsing()
    {
        var test = new CSharpCodeFixTest<FoDicomUsageAnalyzer, FoDicomToCompatFix, DefaultVerifier>
        {
            TestCode = "using FellowOakDicom.Network.Client;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.FoDicom5.Compat.FellowOakDicom.Network.Client;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(1, 1)
                .WithArguments("FellowOakDicom.Network.Client"));
        await test.RunAsync();
    }

    /// <summary>
    /// Rewrites fo-dicom 4.x root using directive to FoDicom4 compat layer.
    /// </summary>
    [Test]
    public async Task FixesFoDicom4Using()
    {
        var test = new CSharpCodeFixTest<FoDicomUsageAnalyzer, FoDicomToCompatFix, DefaultVerifier>
        {
            TestCode = "using Dicom;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.FoDicom4.Compat.Dicom;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(1, 1)
                .WithArguments("Dicom"));
        await test.RunAsync();
    }
}
