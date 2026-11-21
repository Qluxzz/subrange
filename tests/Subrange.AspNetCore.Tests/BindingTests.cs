using System.Net;
using System.Text;

namespace Subranges.AspNetCore.Tests;

/// <summary>
/// Values outside the bounds must be rejected with 400 before a handler runs, whether they arrive
/// in the route, the query or a JSON body, in minimal APIs and in MVC controllers alike.
/// </summary>
public class BindingTests
{
    private const string ValidBody = """
        {
          "port": 502,
          "fallback": null,
          "temperature": 21.5,
          "allowed": [80, 443],
          "named": { "https": 443 },
          "services": { "443": "https" }
        }
        """;

    [Theory]
    [MemberData(nameof(ValueCases))]
    public async Task RouteAndQueryValues_AreAcceptedOnlyWithinTheBounds(
        string style,
        string port,
        HttpStatusCode expected
    )
    {
        var client = await TestApp.Start(TestContext.Current.CancellationToken);

        var route = await client.GetAsync(
            $"{style}/ports/{port}",
            TestContext.Current.CancellationToken
        );
        var query = await client.GetAsync(
            $"{style}/ports?port={port}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, route.StatusCode);
        Assert.Equal(expected, query.StatusCode);
    }

    public static TheoryData<string, string, HttpStatusCode> ValueCases()
    {
        var cases = new TheoryData<string, string, HttpStatusCode>();
        foreach (var style in new[] { "/minimal", "/controller" })
        {
            cases.Add(style, "502", HttpStatusCode.OK);
            cases.Add(style, "1", HttpStatusCode.OK);
            cases.Add(style, "65535", HttpStatusCode.OK);
            cases.Add(style, "0", HttpStatusCode.BadRequest);
            cases.Add(style, "65536", HttpStatusCode.BadRequest);
            cases.Add(style, "abc", HttpStatusCode.BadRequest);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(ListCases))]
    public async Task RepeatedQueryValues_BindAsAList_EachWithinTheBounds(
        string style,
        string query,
        HttpStatusCode expected
    )
    {
        var client = await TestApp.Start(TestContext.Current.CancellationToken);

        var response = await client.GetAsync(
            $"{style}/port-lists?{query}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
            Assert.Equal(
                "[80,443]",
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            );
    }

    public static TheoryData<string, string, HttpStatusCode> ListCases()
    {
        var cases = new TheoryData<string, string, HttpStatusCode>();
        foreach (var style in new[] { "/minimal", "/controller" })
        {
            cases.Add(style, "ports=80&ports=443", HttpStatusCode.OK);
            cases.Add(style, "ports=80&ports=0", HttpStatusCode.BadRequest);
            cases.Add(style, "ports=80&ports=abc", HttpStatusCode.BadRequest);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(TestApp.Styles), MemberType = typeof(TestApp))]
    public async Task ValidBody_ReachesTheHandler(string style)
    {
        var client = await TestApp.Start(TestContext.Current.CancellationToken);

        var response = await Post(client, style, ValidBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"port\":502", body);
        Assert.Contains("\"temperature\":21.5", body);
        Assert.Contains("\"allowed\":[80,443]", body);
        Assert.Contains("\"named\":{\"https\":443}", body);
        Assert.Contains("\"services\":{\"443\":\"https\"}", body);
    }

    [Theory]
    [MemberData(nameof(InvalidBodyCases))]
    public async Task InvalidBody_IsRejectedBeforeTheHandler(string style, string from, string to)
    {
        var client = await TestApp.Start(TestContext.Current.CancellationToken);
        Assert.Contains(from, ValidBody);

        var response = await Post(client, style, ValidBody.Replace(from, to));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string, string, string> InvalidBodyCases()
    {
        var cases = new TheoryData<string, string, string>();
        foreach (var style in new[] { "/minimal", "/controller" })
        {
            cases.Add(style, "502", "0");
            cases.Add(style, "502", "65536");
            cases.Add(style, "502", "1.5");
            cases.Add(style, "502", "\"502\"");
            cases.Add(style, "502", "null");
            cases.Add(style, "21.5", "200.1");
            cases.Add(style, "\"fallback\": null", "\"fallback\": 0");
            cases.Add(style, "[80, 443]", "[80, 0]");
            cases.Add(style, "{ \"https\": 443 }", "{ \"https\": 70000 }");
            cases.Add(style, "{ \"443\": \"https\" }", "{ \"0\": \"none\" }");
        }
        return cases;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string style, string json) =>
        client.PostAsync(
            $"{style}/settings",
            new StringContent(json, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );
}
