using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpDicom.Tests.Generators;

/// <summary>
/// Snapshot assertions for source generator output.
/// </summary>
/// <remarks>
/// Each generated source is compared against
/// <c>{TestClass}.{TestMethod}#{HintName}.verified.cs</c> next to the calling test file.
/// On mismatch a <c>.received.cs</c> file is written alongside for review; accept a change
/// by renaming it to <c>.verified.cs</c>.
/// </remarks>
internal static class GeneratorSnapshot
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Asserts that the generator run produced no diagnostics or exceptions and that
    /// its output matches the verified snapshots for the current test.
    /// </summary>
    /// <param name="driver">The generator driver after <c>RunGenerators</c>.</param>
    /// <param name="callerFilePath">Path of the calling test file (supplied by the compiler).</param>
    public static void Verify(GeneratorDriver driver, [CallerFilePath] string callerFilePath = "")
    {
        var runResult = driver.GetRunResult();
        var exceptions = runResult.Results.Where(r => r.Exception is not null).Select(r => r.Exception!.ToString());
        Assert.That(exceptions, Is.Empty, "Generator threw an exception");
        Assert.That(runResult.Diagnostics.Select(d => d.ToString()), Is.Empty, "Generator reported diagnostics");

        var directory = Path.GetDirectoryName(callerFilePath)!;
        var test = TestContext.CurrentContext.Test;
        var className = test.ClassName!.Substring(test.ClassName.LastIndexOf('.') + 1);
        var prefix = $"{className}.{test.MethodName}#";

        var failures = new List<string>();
        var expectedFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var generated in runResult.Results.SelectMany(r => r.GeneratedSources))
        {
            var baseName = prefix + Path.GetFileNameWithoutExtension(generated.HintName);
            var verifiedPath = Path.Combine(directory, baseName + ".verified.cs");
            var receivedPath = Path.Combine(directory, baseName + ".received.cs");
            expectedFiles.Add(Path.GetFileName(verifiedPath));

            var actual = Normalize($"//HintName: {generated.HintName}\n{generated.SourceText}");
            var expected = File.Exists(verifiedPath) ? Normalize(File.ReadAllText(verifiedPath)) : null;

            if (actual == expected)
            {
                if (File.Exists(receivedPath))
                {
                    File.Delete(receivedPath);
                }

                continue;
            }

            File.WriteAllText(receivedPath, actual + "\n", Utf8WithBom);
            failures.Add(expected is null
                ? $"Missing snapshot {Path.GetFileName(verifiedPath)}; wrote {Path.GetFileName(receivedPath)}"
                : $"Snapshot mismatch for {Path.GetFileName(verifiedPath)}; wrote {Path.GetFileName(receivedPath)}");
        }

        foreach (var stale in Directory.GetFiles(directory, prefix + "*.verified.cs"))
        {
            if (!expectedFiles.Contains(Path.GetFileName(stale)))
            {
                failures.Add($"Snapshot {Path.GetFileName(stale)} has no corresponding generated source");
            }
        }

        Assert.That(failures, Is.Empty, "Generator output does not match snapshots");
    }

    private static string Normalize(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n").TrimEnd();
}
