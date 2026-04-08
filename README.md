# Instructions for candidates

This is the .NET version of the Payment Gateway challenge. If you haven't already read this [README.md](https://github.com/cko-recruitment/) on the details of this exercise, please do so now.

## Template structure
```
src/
    PaymentGateway.Api - a skeleton ASP.NET Core Web API
test/
    PaymentGateway.Api.Tests - an empty xUnit test project
imposters/ - contains the bank simulator configuration. Don't change this

.editorconfig - don't change this. It ensures a consistent set of rules for submissions when reformatting code
docker-compose.yml - configures the bank simulator
PaymentGateway.sln
```

Feel free to change the structure of the solution, use a different test library etc.

---

# Design Considerations

## Architecture

The solution separates concerns into three layers:

- **Controller** — Handles HTTP semantics only: model validation, status code mapping, and request/response serialisation. Contains no business logic.
- **PaymentService** — Owns the bank communication workflow: request transformation, HTTP calls, response interpretation, and error logging. Injected via `IPaymentService` for testability.
- **PaymentsRepository** — Abstracts storage behind `IPaymentsRepository`. Uses `ConcurrentDictionary<Guid, PostPaymentResponse>` for thread-safe, O(1) lookups. In production this interface would be backed by a database with a scoped lifetime.

This separation was chosen over a single-controller approach because it allows each layer to be tested independently and makes the codebase easier to navigate during a review.

## Validation and Rejected Payments

ASP.NET Core's `[ApiController]` attribute normally returns an automatic `400 ProblemDetails` response for invalid model state. This was suppressed via `ApiBehaviorOptions.SuppressModelStateInvalidFilter` so the controller can return a structured `PaymentResponseBase` with a `Rejected` status and a list of human-readable validation errors. This gives clients a consistent response shape across all error types.

Rejected responses intentionally omit payment-specific fields (ID, card details, amount) since no payment was created.

## Bank Error Handling

Bank responses are categorised into three outcomes:

| Bank Response | Gateway Behaviour |
|---|---|
| 200 + `authorized: true` | 200 OK, status `Authorized` |
| 200 + `authorized: false` | 200 OK, status `Declined` |
| 4xx | 400 Bad Request, status `Rejected` |
| 5xx or network failure | 502 Bad Gateway |

`PaymentService` throws `BankErrorException` for non-success bank responses, carrying the status code. The controller maps these to client-facing HTTP statuses. This keeps HTTP semantics in the controller and business logic in the service.

All bank errors (response body, status code, latency, network exceptions) are logged in `PaymentService` before exceptions propagate. The client never sees the bank's internal error details.

## Use of `Rejected` for Bank 4xx Responses

The spec defines `Rejected` as "invalid data submitted; gateway rejects without contacting bank." Strictly speaking, a bank 4xx occurs *after* a bank call is made. However, bank 4xx still indicates a client data problem — the bank found something wrong with the request fields. Returning `Rejected` in this case is cleaner than overloading `Declined` (which implies the bank made an authorisation decision) or introducing a fourth status.

In practice, if our gateway validation is thorough, we should never receive a 4xx from the bank — our own checks should catch invalid data before the request leaves the gateway. A bank 4xx therefore represents either a gap in our validation or a mismatch between our rules and the bank's. Treating it as `Rejected` gives clients a consistent signal ("your data was invalid") regardless of which layer caught the problem, and the logged bank error body helps us close any validation gaps.

## Response Model Hierarchy

`PaymentResponseBase` (status + errors) serves as the base class. `PostPaymentResponse` extends it with payment-specific fields. This avoids returning empty or default-valued fields (e.g., `id: "00000000-..."`) for rejected payments, and gives a clean contract:

- **Rejected / Bank 4xx**: `{ "status": "Rejected", "errors": [...] }`
- **Authorized / Declined**: `{ "id": "...", "status": "Authorized", "card_number_last_four": "...", ... }`

## HttpClient Management

`HttpClient` is registered via `AddHttpClient<IPaymentService, PaymentService>()` (typed client pattern). `IHttpClientFactory` manages the underlying handler pool, avoiding the socket exhaustion problem that occurs with manual `new HttpClient()` instantiation.

## Observability

Structured logging uses semantic parameter names (`{PaymentId}`, `{BankStatusCode}`, `{BankLatencyMs}`, `{BankResponseBody}`) throughout the request lifecycle. In a production system these would feed into:

- **Dashboards** — Payment status distribution, bank response times, error rates
- **Alerting** — Spike in bank 5xx, latency exceeding SLA thresholds
- **Distributed tracing** — Correlation across gateway and bank calls via OpenTelemetry

## Testing Strategy

The test suite has 29 tests across two categories:

- **Unit tests (14)** — Test `PaymentService`, `PaymentsRepository`, and `FutureDateAttribute` in isolation with no I/O. Bank HTTP responses are controlled via a `MockHttpMessageHandler`. These run in milliseconds and give fast feedback during development.
- **Integration tests (15)** — Verify the full HTTP pipeline (routing → validation → service → repository → serialisation) using `WebApplicationFactory`. A `BankSimulatorHandler` replicates the mountebank rules in-process, so tests run without Docker.

This split is deliberate: unit tests catch logic regressions quickly, while integration tests catch wiring issues (missing DI registrations, serialisation mismatches, incorrect route templates) that unit tests cannot.

All test dates use `DateTime.UtcNow.Year + 1` to avoid future expiry failures.

# Assumptions

1. **Only successfully processed payments are stored.** The spec says "retrieve previously processed payments" — rejected payments (failed validation) were never processed, so they are not persisted.
2. **Bank 4xx indicates invalid request data**, not a card-level decline. The bank simulator returns 400 only for missing or malformed fields. A generic error message is returned to avoid leaking bank internals.
3. **Bank 5xx is treated as an upstream failure**, not a decline. The client receives 502 and can retry. This differs from treating it as `Declined`, which would imply the bank made a decision.
4. **The `Rejected` status is included** even though the response field spec says "Authorized or Declined only." The three-outcome model (Authorized, Declined, Rejected) is described in the requirements and provides more useful information to clients than overloading `Declined` for validation failures.
5. **Currency validation is limited to EUR, GBP, USD.** The challenge specifies validating against a maximum of three ISO currency codes. These three were chosen for broad coverage across common payment regions.
6. **No authentication or authorisation** is implemented. The challenge focuses on payment processing, not merchant identity.
