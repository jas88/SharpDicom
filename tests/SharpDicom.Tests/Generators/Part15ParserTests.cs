using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpDicom.Generators;
using VerifyNUnit;

namespace SharpDicom.Tests.Generators;

/// <summary>
/// Tests for Part15Parser de-identification action parsing.
/// </summary>
[TestFixture]
public class Part15ParserTests
{
    [Test]
    public Task ParsesBasicProfileActions()
    {
        // Table E.1-1 structure with common attributes
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook" xmlns:xml="http://www.w3.org/XML/1998/namespace">
              <chapter>
                <table xml:id="table_E.1-1">
                  <caption>Application Level Confidentiality Profile Attributes</caption>
                  <thead>
                    <tr>
                      <th>Attribute Name</th>
                      <th>Tag</th>
                      <th>Retired</th>
                      <th>In Std IOD</th>
                      <th>Basic Profile</th>
                      <th>Rtn. Safe Priv.</th>
                      <th>Rtn. UIDs</th>
                      <th>Rtn. Dev. Id.</th>
                      <th>Rtn. Inst. Id.</th>
                      <th>Rtn. Pat. Chars.</th>
                      <th>Rtn. Long. Full Dates</th>
                      <th>Rtn. Long. Modif. Dates</th>
                      <th>Clean Desc.</th>
                      <th>Clean Struct. Cont.</th>
                      <th>Clean Graph.</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td><para>Accession Number</para></td>
                      <td><para>(0008,0050)</para></td>
                      <td><para>N</para></td>
                      <td><para>Y</para></td>
                      <td><para>Z</para></td>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml);
    }

    [Test]
    public Task ParsesPatientName()
    {
        // Patient Name with Z action
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook" xmlns:xml="http://www.w3.org/XML/1998/namespace">
              <chapter>
                <table xml:id="table_E.1-1">
                  <caption>Application Level Confidentiality Profile Attributes</caption>
                  <tbody>
                    <tr>
                      <td><para>Patient's Name</para></td>
                      <td><para>(0010,0010)</para></td>
                      <td><para>N</para></td>
                      <td><para>Y</para></td>
                      <td><para>Z</para></td>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml);
    }

    [Test]
    public Task ParsesSOPInstanceUID()
    {
        // SOP Instance UID with U action and RetainUIDs option
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook" xmlns:xml="http://www.w3.org/XML/1998/namespace">
              <chapter>
                <table xml:id="table_E.1-1">
                  <caption>Application Level Confidentiality Profile Attributes</caption>
                  <tbody>
                    <tr>
                      <td><para>SOP Instance UID</para></td>
                      <td><para>(0008,0018)</para></td>
                      <td><para>N</para></td>
                      <td><para>Y</para></td>
                      <td><para>U</para></td>
                      <td/>
                      <td><para>K</para></td>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml);
    }

    [Test]
    public Task ParsesRetiredAttribute()
    {
        // Retired attribute (Acquisition Comments)
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook" xmlns:xml="http://www.w3.org/XML/1998/namespace">
              <chapter>
                <table xml:id="table_E.1-1">
                  <caption>Application Level Confidentiality Profile Attributes</caption>
                  <tbody>
                    <tr>
                      <td><para>Acquisition Comments</para></td>
                      <td><para>(0018,4000)</para></td>
                      <td><para>Y</para></td>
                      <td><para>N</para></td>
                      <td><para>X</para></td>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td><para>C</para></td>
                      <td/>
                      <td/>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml);
    }

    [Test]
    public Task ParsesCompoundActions()
    {
        // Compound action X/Z and X/Z/D
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook" xmlns:xml="http://www.w3.org/XML/1998/namespace">
              <chapter>
                <table xml:id="table_E.1-1">
                  <caption>Application Level Confidentiality Profile Attributes</caption>
                  <tbody>
                    <tr>
                      <td><para>Acquisition Context Sequence</para></td>
                      <td><para>(0040,0555)</para></td>
                      <td><para>N</para></td>
                      <td><para>Y</para></td>
                      <td><para>X/Z</para></td>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td><para>C</para></td>
                      <td/>
                    </tr>
                    <tr>
                      <td><para>Acquisition DateTime</para></td>
                      <td><para>(0008,002A)</para></td>
                      <td><para>N</para></td>
                      <td><para>Y</para></td>
                      <td><para>X/Z/D</para></td>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td><para>K</para></td>
                      <td><para>C</para></td>
                      <td/>
                      <td/>
                      <td/>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml);
    }

    [Test]
    public Task ParsesAllProfileOptions()
    {
        // Institution Name with multiple profile options
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <book xmlns="http://docbook.org/ns/docbook" xmlns:xml="http://www.w3.org/XML/1998/namespace">
              <chapter>
                <table xml:id="table_E.1-1">
                  <caption>Application Level Confidentiality Profile Attributes</caption>
                  <tbody>
                    <tr>
                      <td><para>Institution Name</para></td>
                      <td><para>(0008,0080)</para></td>
                      <td><para>N</para></td>
                      <td><para>Y</para></td>
                      <td><para>X/Z/D</para></td>
                      <td/>
                      <td/>
                      <td/>
                      <td><para>K</para></td>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                      <td/>
                    </tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        return VerifyGenerator(xml);
    }

    private static Task VerifyGenerator(string xmlContent)
    {
        // Create compilation
#pragma warning disable IL3000 // Required for source generator tests
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

        // Create driver with part15.xml additional file
        var additionalText = new InMemoryAdditionalText("part15.xml", xmlContent);
        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(additionalText));

        // Run generator
        driver = driver.RunGenerators(compilation);

        // Verify snapshot
        return Verifier.Verify(driver);
    }
}
