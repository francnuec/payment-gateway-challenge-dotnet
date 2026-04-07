namespace PaymentGateway.Api.Tests.Helpers;

/// <summary>
/// Intercepts HTTP requests and returns preconfigured responses. Used in unit tests
/// to isolate PaymentService from the real bank API — tests run instantly with no
/// network or Docker dependencies.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

    public MockHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        _sendAsync = sendAsync;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _sendAsync(request, cancellationToken);
    }
}
