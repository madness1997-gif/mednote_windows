using System.Net;
using MedNote.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class ReaderDictionaryServiceTests
{
    [TestMethod]
    public async Task Lookup_DecodesDeduplicatesAndCombinesProviders()
    {
        using var handler = new StubHandler(request => request.RequestUri!.Host == "api.mymemory.translated.net"
            ? Json("""{"responseStatus":200,"responseData":{"translatedText":"tim &amp; mạch"},"matches":[{"translation":"tim &amp; mạch"},{"translation":"trái tim"},{"translation":"trái tim"}]}""")
            : Json("""[{"phonetic":"/hɑːt/","phonetics":[{"audio":"https://api.dictionaryapi.dev/media/heart.mp3"}],"meanings":[{"partOfSpeech":"noun","definitions":[{"definition":"An organ."}]}]}]"""));
        using var http = new HttpClient(handler);
        var result = await new ReaderDictionaryService(http).LookupAsync(" heart ", CancellationToken.None);
        Assert.AreEqual("tim & mạch", result.Translation);
        CollectionAssert.AreEqual(new[] { "trái tim" }, result.Alternatives.ToArray());
        Assert.AreEqual("/hɑːt/", result.Phonetic);
        Assert.AreEqual(1, result.Definitions.Count);
        Assert.IsNotNull(result.AudioUrl);
        Assert.AreEqual(2, handler.Count);
    }

    [TestMethod]
    public async Task Lookup_DictionaryFailureKeepsTranslation()
    {
        using var handler = new StubHandler(request => request.RequestUri!.Host == "api.mymemory.translated.net"
            ? Json("""{"responseStatus":200,"responseData":{"translatedText":"tim"}}""")
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        var result = await new ReaderDictionaryService(http).LookupAsync("heart", CancellationToken.None);
        Assert.AreEqual("tim", result.Translation);
        Assert.AreEqual(0, result.Definitions.Count);
    }

    [TestMethod]
    public async Task Lookup_PhraseDoesNotRequestWordDictionary()
    {
        using var handler = new StubHandler(_ => Json("""{"responseStatus":200,"responseData":{"translatedText":"suy tim"}}"""));
        using var http = new HttpClient(handler);
        await new ReaderDictionaryService(http).LookupAsync("heart failure", CancellationToken.None);
        Assert.AreEqual(1, handler.Count);
    }

    [TestMethod]
    public async Task Lookup_CancellationPropagates()
    {
        using var handler = new StubHandler(_ => throw new OperationCanceledException());
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ReaderDictionaryService(http).LookupAsync("heart", CancellationToken.None));
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return Task.FromResult(respond(request));
        }
    }
}
