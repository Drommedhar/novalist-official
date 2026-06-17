using System.Net;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class GrammarCheckServiceAuthTests
{
    [Fact]
    public async Task CheckAsync_IncludesCredentialsInForm_UsesPlusEndpointByDefault()
    {
        HttpRequestMessage? captured = null;
        string? capturedContent = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            if (req.Content != null)
                capturedContent = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"matches\":[]}") };
        });

        var http = new HttpClient(handler);
        var svc = new GrammarCheckService(http)
        {
            ApiKey = "test-key-123",
            Username = "user@example.com"
        };

        await svc.CheckAsync("text", "en-US");

        Assert.NotNull(captured);
        Assert.Contains("apiKey=test-key-123", capturedContent ?? string.Empty);
        Assert.Contains("username=user%40example.com", capturedContent ?? string.Empty);
        Assert.Equal("https://api.languagetoolplus.com/v2/check", captured.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CheckAsync_WithCustomApiUrl_UsesCustomUrlAndIncludesCredentials()
    {
        HttpRequestMessage? captured = null;
        string? capturedContent = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            if (req.Content != null)
                capturedContent = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"matches\":[]}") };
        });

        var http = new HttpClient(handler);
        var svc = new GrammarCheckService(http)
        {
            ApiKey = "k",
            Username = "u",
            ApiUrl = "https://custom.example/v2/check"
        };

        await svc.CheckAsync("text", "en-US");

        Assert.NotNull(captured);
        Assert.Contains("apiKey=k", capturedContent ?? string.Empty);
        Assert.Contains("username=u", capturedContent ?? string.Empty);
        Assert.Equal("https://custom.example/v2/check", captured.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_SetsIsPremiumAvailable_OnSuccess()
    {
        HttpRequestMessage? captured = null;
        string? capturedContent = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            if (req.Content != null)
                capturedContent = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"matches\":[]}") };
        });

        var http = new HttpClient(handler);
        var svc = new GrammarCheckService(http)
        {
            ApiKey = "k",
            Username = "u"
        };

        var ok = await svc.ValidateCredentialsAsync();

        Assert.True(ok);
        Assert.True(svc.IsPremiumAvailable);
        Assert.NotNull(captured);
        Assert.Contains("text=ping", capturedContent ?? string.Empty);
        Assert.Contains("apiKey=k", capturedContent ?? string.Empty);
        Assert.Contains("username=u", capturedContent ?? string.Empty);
        Assert.Equal("https://api.languagetoolplus.com/v2/check", captured.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_SetsIsPremiumAvailableToFalse_OnFailure()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });

        var http = new HttpClient(handler);
        var svc = new GrammarCheckService(http)
        {
            ApiKey = "k",
            Username = "u"
        };

        var ok = await svc.ValidateCredentialsAsync();

        Assert.False(ok);
        Assert.False(svc.IsPremiumAvailable);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WithEmptyCredentials_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var http = new HttpClient(handler);
        var svc = new GrammarCheckService(http);

        var ok = await svc.ValidateCredentialsAsync();

        Assert.False(ok);
        Assert.False(svc.IsPremiumAvailable);
    }

    [Fact]
    public async Task CheckAsync_WithPickyMode_AppendsPickyLevel()
    {
        HttpRequestMessage? captured = null;
        string? capturedContent = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            if (req.Content != null)
                capturedContent = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"matches\":[]}") };
        });

        var http = new HttpClient(handler);
        var svc = new GrammarCheckService(http)
        {
            PickyMode = true
        };

        await svc.CheckAsync("hello", "en-US");

        Assert.NotNull(captured);
        Assert.Contains("level=picky", capturedContent ?? string.Empty);
    }

    [Fact]
    public async Task CheckAsync_WithMotherTongue_AppendsMotherTongue()
    {
        HttpRequestMessage? captured = null;
        string? capturedContent = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            if (req.Content != null)
                capturedContent = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"matches\":[]}") };
        });

        var http = new HttpClient(handler);
        var svc = new GrammarCheckService(http)
        {
            MotherTongue = "de-DE"
        };

        await svc.CheckAsync("hello", "en-US");

        Assert.NotNull(captured);
        Assert.Contains("motherTongue=de-DE", capturedContent ?? string.Empty);
    }

    [Fact]
    public async Task AddToDictionaryAsync_SendsPostWithCredentialsAndWord()
    {
        HttpRequestMessage? captured = null;
        string? capturedContent = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            if (req.Content != null)
                capturedContent = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var http = new HttpClient(handler);
        var svc = new GrammarCheckService(http)
        {
            ApiKey = "my-key",
            Username = "user@mail.com"
        };

        var ok = await svc.AddToDictionaryAsync("fantasyword");

        Assert.True(ok);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("https://api.languagetoolplus.com/v2/words/add", captured.RequestUri!.AbsoluteUri);
        Assert.Contains("word=fantasyword", capturedContent ?? string.Empty);
        Assert.Contains("apiKey=my-key", capturedContent ?? string.Empty);
        Assert.Contains("username=user%40mail.com", capturedContent ?? string.Empty);
    }
}
