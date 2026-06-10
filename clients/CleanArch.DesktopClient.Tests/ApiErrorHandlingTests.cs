using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using CleanArch.DesktopClient.Api;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class ApiErrorHandlingTests
{
    private static HttpResponseMessage Problem(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };

    [Fact]
    public async Task Domain_problem_detail_is_surfaced_as_the_message()
    {
        using var response = Problem(HttpStatusCode.BadRequest,
            """{"title":"Bad request","detail":"A copy is available — borrow it directly instead of reserving.","status":400}""");

        var ex = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("A copy is available — borrow it directly instead of reserving.", ex.Message);
    }

    [Fact]
    public async Task Validation_errors_are_flattened_into_the_message()
    {
        using var response = Problem(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"PageSize":["'Page Size' must be between 1 and 100."],"Page":["'Page' must be >= 1."]}}""");

        var ex = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("'Page Size' must be between 1 and 100.", ex.Message);
        Assert.Contains("'Page' must be >= 1.", ex.Message);
    }

    [Fact]
    public async Task Unauthorized_without_a_body_gets_a_friendly_default()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var ex = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("not authorized", ex.Message);
    }

    [Fact]
    public async Task Server_error_without_a_useful_body_gets_a_friendly_default()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>oops</html>", Encoding.UTF8, "text/html"),
        };

        var ex = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal(500, ex.StatusCode);
        Assert.Contains("server had a problem", ex.Message);
    }

    [Fact]
    public async Task Get_detail_returns_null_on_404_instead_of_throwing()
    {
        var http = StubbedClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var students = new StudentsApiClient(http);

        var detail = await students.GetDetailAsync(Guid.NewGuid());

        Assert.Null(detail);
    }

    [Fact]
    public async Task Get_detail_returns_the_student_on_200()
    {
        var id = Guid.NewGuid();
        var http = StubbedClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"id":"{{id}}","firstName":"Ada","lastName":"Lovelace","email":"a@uni.edu","status":"Active","address":null,"emergencyContacts":[],"enrollments":[],"activeEnrollments":0}""",
                Encoding.UTF8, "application/json"),
        });
        var students = new StudentsApiClient(http);

        var detail = await students.GetDetailAsync(id);

        Assert.NotNull(detail);
        Assert.Equal("Ada", detail!.FirstName);
    }

    [Fact]
    public async Task Get_detail_still_throws_on_a_non_404_error()
    {
        var http = StubbedClient(_ => Problem(HttpStatusCode.BadRequest, """{"detail":"bad id","status":400}"""));
        var students = new StudentsApiClient(http);

        var ex = await Assert.ThrowsAsync<ApiException>(() => students.GetDetailAsync(Guid.NewGuid()));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("bad id", ex.Message);
    }

    [Fact]
    public async Task A_down_api_surfaces_a_friendly_unreachable_message()
    {
        // Reserve a loopback port, then free it so connecting is refused immediately.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var (_, students, _, _, _) = ApiClientFactory.Create($"http://127.0.0.1:{port}/", "dev-api-key-integration");

        var ex = await Assert.ThrowsAsync<ApiException>(() => students.SearchAsync(1, 20, null));
        Assert.Null(ex.StatusCode); // transport failure — never reached the server
        Assert.Contains("reach the server", ex.Message);
    }

    private static HttpClient StubbedClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubHandler(respond)) { BaseAddress = new Uri("http://localhost/") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
