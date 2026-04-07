using PaymentGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register the payments repository as a singleton (in-memory store for this challenge).
// In production this would be backed by a database with a scoped lifetime.
builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();

// Typed HttpClient for PaymentService. IHttpClientFactory manages the underlying handler
// pool, avoiding the socket exhaustion problem caused by manual `new HttpClient()` usage.
builder.Services.AddHttpClient<IPaymentService, PaymentService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BankPaymentsEndpoint"]
        ?? throw new InvalidOperationException("BankPaymentsEndpoint configuration is required."));
});

// Suppress the automatic 400 ProblemDetails response for invalid model state.
// This lets the controller return a structured Rejected response with a payment ID,
// which is more useful for client tracking than a generic validation error.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

// Required for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
