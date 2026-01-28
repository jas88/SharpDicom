using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpDicom.Generators;
using VerifyNUnit;

namespace SharpDicom.Tests.Generators;

/// <summary>
/// Snapshot tests for the DicomDictionaryGenerator using Verify.SourceGenerators.
/// </summary>
[TestFixture]
public class DicomDictionaryGeneratorTests
{
    [Test]
    public Task GeneratesTagsFromMinimalXml()
    {
        // Minimal Part 6-like XML with one tag
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook">
              <chapter>
                <table>
                  <caption>Registry of DICOM Data Elements</caption>
                  <tbody>
                    <tr>
                      <td><para>(0010,0020)</para></td>
                      <td><para>Patient ID</para></td>
                      <td><para>PatientID</para></td>
                      <td><para>LO</para></td>
                      <td><para>1</para></td>
                      <td><para></para></td>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml, "part06.xml");
    }

    [Test]
    public Task GeneratesMultiVRTag()
    {
        // Tag with multiple VRs (US or SS)
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook">
              <chapter>
                <table>
                  <caption>Registry of DICOM Data Elements</caption>
                  <tbody>
                    <tr>
                      <td><para>(0028,0106)</para></td>
                      <td><para>Smallest Image Pixel Value</para></td>
                      <td><para>SmallestImagePixelValue</para></td>
                      <td><para>US or SS</para></td>
                      <td><para>1</para></td>
                      <td><para></para></td>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml, "part06.xml");
    }

    [Test]
    public Task GeneratesRetiredTag()
    {
        // Retired tag (should be marked with IsRetired=true)
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook">
              <chapter>
                <table>
                  <caption>Registry of DICOM Data Elements</caption>
                  <tbody>
                    <tr>
                      <td><para>(0008,0041)</para></td>
                      <td><para>Data Set Subtype</para></td>
                      <td><para>DataSetSubtype</para></td>
                      <td><para>LO</para></td>
                      <td><para>1</para></td>
                      <td><para>(Retired)</para></td>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml, "part06.xml");
    }

    [Test]
    public Task GeneratesUIDs()
    {
        // UID registry table
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook">
              <chapter>
                <table>
                  <caption>UID Values</caption>
                  <tbody>
                    <tr>
                      <td><para>1.2.840.10008.1.2</para></td>
                      <td><para>Implicit VR Little Endian</para></td>
                      <td><para>ImplicitVRLittleEndian</para></td>
                      <td><para>Transfer Syntax</para></td>
                      <td><para></para></td>
                    </tr>
                    <tr>
                      <td><para>1.2.840.10008.1.2.1</para></td>
                      <td><para>Explicit VR Little Endian</para></td>
                      <td><para>ExplicitVRLittleEndian</para></td>
                      <td><para>Transfer Syntax</para></td>
                      <td><para></para></td>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml, "part06.xml");
    }

    [Test]
    public Task HandlesMaskedTag()
    {
        // Masked tag (50xx,0010)
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook">
              <chapter>
                <table>
                  <caption>Registry of DICOM Data Elements</caption>
                  <tbody>
                    <tr>
                      <td><para>(50xx,0010)</para></td>
                      <td><para>Number of Points in ROI</para></td>
                      <td><para>NumberOfPointsInROI</para></td>
                      <td><para>US</para></td>
                      <td><para>1</para></td>
                      <td><para></para></td>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml, "part06.xml");
    }

    [Test]
    public Task HandlesEmptyXml()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook">
            </book>
            """;

        return VerifyGenerator(xml, "part06.xml");
    }

    [Test]
    public Task ReportsDiagnosticForInvalidXml()
    {
        var xml = "not valid xml";

        return VerifyGenerator(xml, "part06.xml");
    }

    [Test]
    public Task GeneratesTagsAndUIDsCombined()
    {
        // Combined tags and UIDs in same XML
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook">
              <chapter>
                <table>
                  <caption>Registry of DICOM Data Elements</caption>
                  <tbody>
                    <tr>
                      <td><para>(0008,0020)</para></td>
                      <td><para>Study Date</para></td>
                      <td><para>StudyDate</para></td>
                      <td><para>DA</para></td>
                      <td><para>1</para></td>
                      <td><para></para></td>
                    </tr>
                  </tbody>
                </table>
                <table>
                  <caption>UID Values</caption>
                  <tbody>
                    <tr>
                      <td><para>1.2.840.10008.5.1.4.1.1.2</para></td>
                      <td><para>CT Image Storage</para></td>
                      <td><para>CTImageStorage</para></td>
                      <td><para>SOP Class</para></td>
                      <td><para></para></td>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml, "part06.xml");
    }

    private static Task VerifyGenerator(string xmlContent, string fileName)
    {
        // Create compilation
        // IL3000: Assembly.Location is required for source generator tests to get metadata references
#pragma warning disable IL3000
        var compilation = CSharpCompilation.Create("TestAssembly",
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location)
            },
#pragma warning restore IL3000
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Create generator
        var generator = new DicomDictionaryGenerator();

        // Create driver with additional file
        var additionalText = new InMemoryAdditionalText(fileName, xmlContent);
        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(additionalText));

        // Run generator
        driver = driver.RunGenerators(compilation);

        // Verify snapshot
        return Verifier.Verify(driver);
    }
}
