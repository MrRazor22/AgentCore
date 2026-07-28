using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CodeSharp.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class WebToolsTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        private readonly string _responseHtml;

        public MockHttpMessageHandler(string responseHtml)
        {
            _responseHtml = responseHtml;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            LastRequest = request;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseHtml)
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task SearchWeb_DirectUrl_CallsFetchUrlAndExtractsText()
    {
        var rawHtml = "<html><body><div class='content'>Hello mock web page</div></body></html>";
        var mockHandler = new MockHttpMessageHandler(rawHtml);
        var httpClient = new HttpClient(mockHandler);
        var tools = new WebTools(httpClient);

        var result = await tools.SearchWeb("https://test.local/page.html");

        Assert.Equal(1, mockHandler.SendCount);
        Assert.Equal("https://test.local/page.html", mockHandler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal("Hello mock web page", result.Trim());
    }

    [Fact]
    public async Task SearchWeb_SearchQuery_BuildsCorrectDuckDuckGoUrlAndParsesResults()
    {
        var mockDdgHtml = @"
            <div class=""result body"">
                <a class=""result__a"" href=""/l/?uddg=https%3A%2F%2Fexample.com%2Ftarget"">Example Target Title</a>
                <a class=""result__snippet"">This is a mock snippet from search.</a>
            </div>
            </div>
        ";
        var mockHandler = new MockHttpMessageHandler(mockDdgHtml);
        var httpClient = new HttpClient(mockHandler);
        var tools = new WebTools(httpClient);

        var result = await tools.SearchWeb("unit tests", domain: "example.com");

        Assert.Equal(1, mockHandler.SendCount);
        // Verify Site Domain and Query parameters in URL
        var requestUrl = mockHandler.LastRequest!.RequestUri!.AbsoluteUri;
        Assert.Contains("html.duckduckgo.com", requestUrl);
        Assert.Contains("site%3Aexample.com%20unit%20tests", requestUrl);

        // Verify parsing output
        Assert.Contains("Example Target Title", result);
        Assert.Contains("https://example.com/target", result);
        Assert.Contains("This is a mock snippet from search.", result);
    }
}
