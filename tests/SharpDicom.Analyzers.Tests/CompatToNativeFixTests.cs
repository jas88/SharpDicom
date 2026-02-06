using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpDicom.Analyzers.Analyzers;
using SharpDicom.Analyzers.CodeFixes;

namespace SharpDicom.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="CompatToNativeFix"/> which rewrites compat layer using directives
/// to native SharpDicom namespaces (step 2 migration).
/// </summary>
[TestFixture]
public sealed class CompatToNativeFixTests
{
    /// <summary>
    /// Rewrites FoDicom5 compat FellowOakDicom root to SharpDicom.Data.
    /// </summary>
    [Test]
    public async Task FixesCompat5ToNative()
    {
        var test = new CSharpCodeFixTest<CompatUsageAnalyzer, CompatToNativeFix, DefaultVerifier>
        {
            TestCode = "using SharpDicom.FoDicom5.Compat.FellowOakDicom;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.Data;\n\nclass Test { }\n",
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
    /// Rewrites FoDicom5 compat Network to SharpDicom.Network.
    /// </summary>
    [Test]
    public async Task FixesCompat5NetworkToNative()
    {
        var test = new CSharpCodeFixTest<CompatUsageAnalyzer, CompatToNativeFix, DefaultVerifier>
        {
            TestCode = "using SharpDicom.FoDicom5.Compat.FellowOakDicom.Network;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.Network;\n\nclass Test { }\n",
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
    /// Rewrites FoDicom5 compat Network.Client to SharpDicom.Network.
    /// </summary>
    [Test]
    public async Task FixesCompat5ClientToNative()
    {
        var test = new CSharpCodeFixTest<CompatUsageAnalyzer, CompatToNativeFix, DefaultVerifier>
        {
            TestCode = "using SharpDicom.FoDicom5.Compat.FellowOakDicom.Network.Client;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.Network;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0010", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
                .WithLocation(1, 1)
                .WithArguments("SharpDicom.FoDicom5.Compat.FellowOakDicom.Network.Client"));
        await test.RunAsync();
    }

    /// <summary>
    /// Rewrites FoDicom4 compat Dicom root to SharpDicom.Data.
    /// </summary>
    [Test]
    public async Task FixesCompat4ToNative()
    {
        var test = new CSharpCodeFixTest<CompatUsageAnalyzer, CompatToNativeFix, DefaultVerifier>
        {
            TestCode = "using SharpDicom.FoDicom4.Compat.Dicom;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.Data;\n\nclass Test { }\n",
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
    /// Rewrites FoDicom4 compat Dicom.Network to SharpDicom.Network.
    /// </summary>
    [Test]
    public async Task FixesCompat4NetworkToNative()
    {
        var test = new CSharpCodeFixTest<CompatUsageAnalyzer, CompatToNativeFix, DefaultVerifier>
        {
            TestCode = "using SharpDicom.FoDicom4.Compat.Dicom.Network;\n\nclass Test { }\n",
            FixedCode = "using SharpDicom.Network;\n\nclass Test { }\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("SD0010", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
                .WithLocation(1, 1)
                .WithArguments("SharpDicom.FoDicom4.Compat.Dicom.Network"));
        await test.RunAsync();
    }
}
