using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class GoogleOAuthPkceTests
{
    [TestMethod]
    public void AuthorizationRequest_UsesLoopbackPkceAndOnlyAppDataScope()
    {
        var request = GoogleDriveOAuth.CreateAuthorizationRequest(
            "desktop.apps.googleusercontent.com",
            new Uri("http://127.0.0.1:49152/oauth2/callback"),
            state: "fixed-state",
            verifier: new string('v', 64));
        var query = Uri.UnescapeDataString(request.AuthorizationUri.Query);

        Assert.AreEqual("fixed-state", request.State);
        Assert.AreEqual(43, request.CodeChallenge.Length);
        StringAssert.Contains(query, "code_challenge_method=S256");
        StringAssert.Contains(query, GoogleDriveOAuth.AppDataScope);
        Assert.IsFalse(query.Contains("drive.file", StringComparison.Ordinal));
        StringAssert.Contains(query, "access_type=offline");
    }

    [TestMethod]
    public void AuthorizationRequest_RejectsNonLoopbackRedirect()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            GoogleDriveOAuth.CreateAuthorizationRequest(
                "desktop.apps.googleusercontent.com",
                new Uri("https://example.com/callback")));
    }
}
