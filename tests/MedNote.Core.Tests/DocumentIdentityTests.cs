using Microsoft.VisualStudio.TestTools.UnitTesting;
using MedNote.Core;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class DocumentIdentityTests
{
    [DataTestMethod]
    [DataRow("Harrison.pdf", 123_456_789L, 1_788_234_000_000L, "doc-1qukjtp")]
    [DataRow("Nội tiết.pdf", 2_048L, 1_788_234_000_123L, "doc-8lspuc")]
    [DataRow("a.pdf", 0L, 0L, "doc-giruek")]
    public void Create_MatchesWebStableId(string name, long size, long lastModified, string expected)
    {
        Assert.AreEqual(expected, DocumentIdentity.Create(name, size, lastModified));
    }
}
