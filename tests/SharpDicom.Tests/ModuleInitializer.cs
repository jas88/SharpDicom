using System.Runtime.CompilerServices;
using VerifyTests;

namespace SharpDicom.Tests;

/// <summary>
/// Module initializer for Verify.SourceGenerators setup.
/// </summary>
public static class ModuleInitializer
{
    /// <summary>
    /// Initializes Verify.SourceGenerators settings.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifySourceGenerators.Initialize();
    }
}
