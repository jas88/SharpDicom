using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpDicom.Analyzers.Analyzers;

namespace SharpDicom.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="FoDicomUsageAnalyzer"/> which detects fo-dicom using directives
/// for step 1 migration (fo-dicom to compat layer).
/// </summary>
[TestFixture]
public sealed class FoDicomUsageAnalyzerTests
{
    /// <summary>
    /// Detects fo-dicom 5.x root namespace using directive.
    /// </summary>
    [Test]
    public async Task DetectsFoDicom5UsingDirective()
    {
        var test = new CSharpAnalyzerTest<FoDicomUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using FellowOakDicom;\n\nclass Test { }\n",
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
    /// Detects fo-dicom 5.x Network sub-namespace using directive.
    /// </summary>
    [Test]
    public async Task DetectsFoDicom5NetworkUsing()
    {
        var test = new CSharpAnalyzerTest<FoDicomUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using FellowOakDicom.Network;\n\nclass Test { }\n",
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
    /// Detects fo-dicom 5.x Network.Client sub-namespace using directive.
    /// </summary>
    [Test]
    public async Task DetectsFoDicom5ClientUsing()
    {
        var test = new CSharpAnalyzerTest<FoDicomUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using FellowOakDicom.Network.Client;\n\nclass Test { }\n",
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
    /// Detects fo-dicom 4.x root namespace using directive.
    /// </summary>
    [Test]
    public async Task DetectsFoDicom4UsingDirective()
    {
        var test = new CSharpAnalyzerTest<FoDicomUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using Dicom;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(1, 1)
                .WithArguments("Dicom"));
        await test.RunAsync();
    }

    /// <summary>
    /// Does not flag unrelated namespaces.
    /// </summary>
    [Test]
    public async Task IgnoresUnrelatedNamespace()
    {
        var test = new CSharpAnalyzerTest<FoDicomUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using System.Diagnostics;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        // No diagnostics expected
        await test.RunAsync();
    }

    /// <summary>
    /// Does not flag SharpDicom namespaces.
    /// </summary>
    [Test]
    public async Task IgnoresSharpDicomNamespace()
    {
        var test = new CSharpAnalyzerTest<FoDicomUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using SharpDicom.Data;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        // No diagnostics expected
        await test.RunAsync();
    }

    /// <summary>
    /// Does not flag user-defined namespaces that contain "Dicom" as a substring.
    /// </summary>
    [Test]
    public async Task IgnoresUserDicomNamespace()
    {
        var test = new CSharpAnalyzerTest<FoDicomUsageAnalyzer, DefaultVerifier>
        {
            TestCode = "using MyCompany.DicomTools;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        // No diagnostics expected - "MyCompany.DicomTools" does not start with "Dicom."
        // or equal "Dicom", nor match FellowOakDicom patterns
        await test.RunAsync();
    }
}
