using System;
using System.Collections.Generic;
using System.IO;

namespace SharpDicom.Cli.Helpers;

/// <summary>
/// Lazily enumerates DICOM files from a mix of files and directories.
/// </summary>
internal static class FileEnumerator
{
    private static readonly StringComparison OrdinalIgnoreCase = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Enumerate DICOM files from the given inputs.
    /// </summary>
    /// <param name="inputs">Files or directories supplied on the command line.</param>
    /// <param name="recursive">Whether to recurse into subdirectories (default true).</param>
    /// <param name="allFiles">Accept any file extension, not just .dcm.</param>
    /// <returns>An enumeration of absolute file paths.</returns>
    public static IEnumerable<string> EnumerateFiles(
        FileSystemInfo[] inputs,
        bool recursive = true,
        bool allFiles = false)
    {
        foreach (var input in inputs)
        {
            if (input is FileInfo fi)
            {
                if (!fi.Exists)
                    throw new FileNotFoundException($"File not found: {fi.FullName}", fi.FullName);

                if (allFiles || fi.Extension.Equals(".dcm", OrdinalIgnoreCase))
                    yield return fi.FullName;
            }
            else if (input is DirectoryInfo di)
            {
                if (!di.Exists)
                    throw new FileNotFoundException($"Directory not found: {di.FullName}", di.FullName);

                var searchOption = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                var pattern = allFiles ? "*" : "*.dcm";

                foreach (var file in Directory.EnumerateFiles(di.FullName, pattern, searchOption))
                {
                    yield return file;
                }
            }
        }
    }
}
