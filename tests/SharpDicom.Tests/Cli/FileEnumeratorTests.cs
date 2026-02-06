using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SharpDicom.Cli.Helpers;

namespace SharpDicom.Tests.Cli;

[TestFixture]
public class FileEnumeratorTests
{
    private string? _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sharpdcm_filenum_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Test]
    public void EnumerateFiles_FiltersDcmOnly()
    {
        File.WriteAllText(Path.Combine(_tempDir!, "test1.dcm"), "");
        File.WriteAllText(Path.Combine(_tempDir!, "test2.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir!, "test3.dcm"), "");

        var inputs = new FileSystemInfo[] { new DirectoryInfo(_tempDir!) };
        var files = FileEnumerator.EnumerateFiles(inputs).ToList();

        Assert.That(files, Has.Count.EqualTo(2));
        Assert.That(files, Has.All.EndsWith(".dcm"));
    }

    [Test]
    public void EnumerateFiles_RecursiveDiscovery()
    {
        var subDir = Path.Combine(_tempDir!, "sub");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(_tempDir!, "root.dcm"), "");
        File.WriteAllText(Path.Combine(subDir, "nested.dcm"), "");

        var inputs = new FileSystemInfo[] { new DirectoryInfo(_tempDir!) };
        var files = FileEnumerator.EnumerateFiles(inputs, recursive: true).ToList();

        Assert.That(files, Has.Count.EqualTo(2));
    }

    [Test]
    public void EnumerateFiles_NonRecursive_TopLevelOnly()
    {
        var subDir = Path.Combine(_tempDir!, "sub");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(_tempDir!, "root.dcm"), "");
        File.WriteAllText(Path.Combine(subDir, "nested.dcm"), "");

        var inputs = new FileSystemInfo[] { new DirectoryInfo(_tempDir!) };
        var files = FileEnumerator.EnumerateFiles(inputs, recursive: false).ToList();

        Assert.That(files, Has.Count.EqualTo(1));
        Assert.That(files[0], Does.Contain("root.dcm"));
    }

    [Test]
    public void EnumerateFiles_NonExistentPath_Throws()
    {
        var missing = Path.Combine(_tempDir!, "nonexistent");
        var inputs = new FileSystemInfo[] { new DirectoryInfo(missing) };

        Assert.Throws<FileNotFoundException>(() =>
            FileEnumerator.EnumerateFiles(inputs).ToList());
    }

    [Test]
    public void EnumerateFiles_SingleFileInput_ReturnsThatFile()
    {
        var filePath = Path.Combine(_tempDir!, "single.dcm");
        File.WriteAllText(filePath, "");

        var inputs = new FileSystemInfo[] { new FileInfo(filePath) };
        var files = FileEnumerator.EnumerateFiles(inputs).ToList();

        Assert.That(files, Has.Count.EqualTo(1));
        Assert.That(files[0], Does.Contain("single.dcm"));
    }

    [Test]
    public void EnumerateFiles_NonExistentFile_Throws()
    {
        var missing = Path.Combine(_tempDir!, "missing.dcm");
        var inputs = new FileSystemInfo[] { new FileInfo(missing) };

        Assert.Throws<FileNotFoundException>(() =>
            FileEnumerator.EnumerateFiles(inputs).ToList());
    }

    [Test]
    public void EnumerateFiles_AllFiles_AcceptsAnyExtension()
    {
        File.WriteAllText(Path.Combine(_tempDir!, "test1.dcm"), "");
        File.WriteAllText(Path.Combine(_tempDir!, "test2.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir!, "test3.dat"), "");

        var inputs = new FileSystemInfo[] { new DirectoryInfo(_tempDir!) };
        var files = FileEnumerator.EnumerateFiles(inputs, allFiles: true).ToList();

        Assert.That(files, Has.Count.EqualTo(3));
    }
}
