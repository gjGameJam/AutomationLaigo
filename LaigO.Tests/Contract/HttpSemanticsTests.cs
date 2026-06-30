using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Contract;

/// <summary>
/// HTTP-method hygiene (Part B §4). Unlike base Starlette, FastAPI's APIRoute
/// does NOT auto-derive HEAD from a GET route (see fastapi/routing.py: methods
/// is set to exactly the declared verbs). So a plain <c>@app.get</c> route is
/// GET-only and rejects every other verb — including HEAD — with 405 + Allow.
/// These lock that contract so a future custom route that silently accepts the
/// wrong verb, or drops the Allow header, would regress.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("HttpSemantics")]
public class HttpSemanticsTests : LaigOTestBase
{
    [Test]
    public async Task Health_HeadRequest_Returns405NotAutoDerived()
    {
        var response = await Client.FetchRawAsync("/health", "HEAD");

        // /health is declared @app.get only; FastAPI does not add HEAD, so the
        // route rejects HEAD exactly as it rejects any other undeclared verb.
        response.Status.Should().Be(405,
            "FastAPI does not auto-derive HEAD from a GET-only route, so HEAD is rejected like any undeclared method");

        // RFC 7231 §6.5.5: a 405 must advertise the supported methods.
        response.Headers.TryGetValue("allow", out var allow);
        allow.Should().NotBeNullOrWhiteSpace("a 405 must include an Allow header listing valid methods");
        allow.Should().Contain("GET", "GET is the only valid method on /health and must appear in Allow");
    }

    [Test]
    public async Task Health_DisallowedMethod_Returns405WithAllowHeader()
    {
        var response = await Client.FetchRawAsync("/health", "PUT");

        response.Status.Should().Be(405,
            "an undeclared method must be rejected with 405 Method Not Allowed, not silently accepted");

        // RFC 7231 §6.5.5: a 405 must advertise the supported methods.
        response.Headers.TryGetValue("allow", out var allow);
        allow.Should().NotBeNullOrWhiteSpace("a 405 must include an Allow header listing valid methods");
        allow.Should().Contain("GET", "GET is a valid method on /health and must appear in Allow");
    }
}
