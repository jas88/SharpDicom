using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
public class ActionResolverTests
{
    [Test]
    public void Resolve_Keep_ReturnsKeep()
    {
        var result = ActionResolver.Resolve(DeidentificationAction.Keep);
        Assert.That(result, Is.EqualTo(ResolvedAction.Keep));
    }

    [Test]
    public void Resolve_Remove_ReturnsRemove()
    {
        var result = ActionResolver.Resolve(DeidentificationAction.Remove);
        Assert.That(result, Is.EqualTo(ResolvedAction.Remove));
    }

    [Test]
    public void Resolve_ZeroOrDummy_ReturnsReplaceWithEmpty()
    {
        var result = ActionResolver.Resolve(DeidentificationAction.ZeroOrDummy);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithEmpty));
    }

    [Test]
    public void Resolve_Dummy_ReturnsReplaceWithDummy()
    {
        var result = ActionResolver.Resolve(DeidentificationAction.Dummy);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithDummy));
    }

    [Test]
    public void Resolve_Clean_ReturnsClean()
    {
        var result = ActionResolver.Resolve(DeidentificationAction.Clean);
        Assert.That(result, Is.EqualTo(ResolvedAction.Clean));
    }

    [Test]
    public void Resolve_RemapUid_ReturnsRemapUid()
    {
        var result = ActionResolver.Resolve(DeidentificationAction.RemapUid);
        Assert.That(result, Is.EqualTo(ResolvedAction.RemapUid));
    }

    [Test]
    public void Resolve_ZeroOrDummyConditional_Type1_ReturnsDummy()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.ZeroOrDummyConditional,
            DicomAttributeType.Type1);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithDummy));
    }

    [Test]
    public void Resolve_ZeroOrDummyConditional_Type2_ReturnsEmpty()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.ZeroOrDummyConditional,
            DicomAttributeType.Type2);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithEmpty));
    }

    [Test]
    public void Resolve_ZeroOrDummyConditional_Type3_ReturnsEmpty()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.ZeroOrDummyConditional,
            DicomAttributeType.Type3);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithEmpty));
    }

    [Test]
    public void Resolve_RemoveOrZeroConditional_Type2_ReturnsEmpty()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.RemoveOrZeroConditional,
            DicomAttributeType.Type2);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithEmpty));
    }

    [Test]
    public void Resolve_RemoveOrZeroConditional_Type3_ReturnsRemove()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.RemoveOrZeroConditional,
            DicomAttributeType.Type3);
        Assert.That(result, Is.EqualTo(ResolvedAction.Remove));
    }

    [Test]
    public void Resolve_RemoveOrDummyConditional_Type1_ReturnsDummy()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.RemoveOrDummyConditional,
            DicomAttributeType.Type1);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithDummy));
    }

    [Test]
    public void Resolve_RemoveOrDummyConditional_Type3_ReturnsRemove()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.RemoveOrDummyConditional,
            DicomAttributeType.Type3);
        Assert.That(result, Is.EqualTo(ResolvedAction.Remove));
    }

    [Test]
    public void Resolve_RemoveOrZeroOrDummyConditional_Type1_ReturnsDummy()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.RemoveOrZeroOrDummyConditional,
            DicomAttributeType.Type1);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithDummy));
    }

    [Test]
    public void Resolve_RemoveOrZeroOrDummyConditional_Type2_ReturnsEmpty()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.RemoveOrZeroOrDummyConditional,
            DicomAttributeType.Type2);
        Assert.That(result, Is.EqualTo(ResolvedAction.ReplaceWithEmpty));
    }

    [Test]
    public void Resolve_RemoveOrZeroOrDummyConditional_Type3_ReturnsRemove()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.RemoveOrZeroOrDummyConditional,
            DicomAttributeType.Type3);
        Assert.That(result, Is.EqualTo(ResolvedAction.Remove));
    }

    [Test]
    public void Resolve_WithVR_UidVR_RemapAction_ReturnsRemapUid()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.RemapUid,
            DicomVR.UI,
            hasValue: true);
        Assert.That(result, Is.EqualTo(ResolvedAction.RemapUid));
    }

    [Test]
    public void Resolve_WithVR_EmptyValue_EmptyAction_ReturnsKeep()
    {
        var result = ActionResolver.Resolve(
            DeidentificationAction.ZeroOrDummy,
            DicomVR.LO,
            hasValue: false);
        Assert.That(result, Is.EqualTo(ResolvedAction.Keep));
    }
}
