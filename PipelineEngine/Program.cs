// Step 5: Make the pipeline async end-to-end.
//
// C# features introduced:
//   async / await
//     `async` marks a method as asynchronous. `await` suspends it without
//     blocking the thread — the thread is returned to the pool while waiting,
//     then the method resumes when the result is ready. This is cooperative
//     multitasking built into the language, not a library bolt-on.
//
//   Task<T>
//     The standard .NET promise type. Task<StepResult> means "a value of
//     type StepResult that will be available in the future". The compiler
//     rewrites async methods into a state machine automatically.
//
//   ValueTask<T>
//     A lighter-weight alternative to Task<T> for hot paths that often
//     complete synchronously (no allocation when no actual async work happens).
//     IStep<T>.Execute returns ValueTask<StepResult> for this reason.
//
//   ConfigureAwait(false)
//     Tells the runtime not to marshal back to the original synchronization
//     context after an await. Correct for library/framework code that has
//     no UI thread to return to — avoids deadlocks in ASP.NET Classic and
//     slightly reduces overhead everywhere.

var pipeline = new Pipeline<Order>()
    .AddStep(new ValidateStep())
    .AddStep(new ApplyDiscountStep())
    .AddStep(new ChargePaymentStep())
    .AddStep(new SendConfirmationStep());

var order = new Order
{
    Id = 42,
    CustomerEmail = "alice@example.com",
    Items = ["Widget", "Gadget"],
    TotalAmount = 149.99m
};

Console.WriteLine($"Processing order #{order.Id}");
var final = await pipeline.RunAsync(order);
Console.WriteLine(final.IsSuccess ? $"Order #{order.Id} complete." : $"Order #{order.Id} failed.");

// ── Step interface ─────────────────────────────────────────────────────────

interface IStep<T>
{
    string Name => GetType().Name;

    // ValueTask<T> instead of Task<T>: zero allocation when the step
    // completes synchronously (e.g. pure in-memory validation).
    ValueTask<StepResult> ExecuteAsync(T payload, CancellationToken ct = default);
}

// ── Step implementations ───────────────────────────────────────────────────

class ValidateStep : IStep<Order>
{
    // Synchronous logic — wrap in ValueTask.FromResult, no allocation overhead.
    public ValueTask<StepResult> ExecuteAsync(Order order, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(order.CustomerEmail))
            return ValueTask.FromResult(StepResult.Fail("Missing customer email."));
        if (order.Items.Count == 0)
            return ValueTask.FromResult(StepResult.Fail("Order has no items."));

        Console.WriteLine("Validated.");
        return ValueTask.FromResult(StepResult.Ok());
    }
}

class ApplyDiscountStep : IStep<Order>
{
    public string Name => "Apply Discount";

    public ValueTask<StepResult> ExecuteAsync(Order order, CancellationToken ct)
    {
        if (order.TotalAmount > 100)
        {
            order.TotalAmount *= 0.9m;
            Console.WriteLine($"Discount applied. New total: {order.TotalAmount:C}");
        }
        return ValueTask.FromResult(StepResult.Ok());
    }
}

class ChargePaymentStep : IStep<Order>
{
    public string Name => "Charge Payment";

    public async ValueTask<StepResult> ExecuteAsync(Order order, CancellationToken ct)
    {
        Console.WriteLine($"Charging {order.TotalAmount:C} to {order.CustomerEmail}...");

        // Simulate async I/O (real impl would await an HTTP call here).
        await Task.Delay(50, ct).ConfigureAwait(false);

        Console.WriteLine("Payment charged.");
        return StepResult.Ok();
    }
}

class SendConfirmationStep : IStep<Order>
{
    public string Name => "Send Confirmation";

    public async ValueTask<StepResult> ExecuteAsync(Order order, CancellationToken ct)
    {
        Console.WriteLine($"Sending confirmation to {order.CustomerEmail}...");

        await Task.Delay(30, ct).ConfigureAwait(false);

        Console.WriteLine("Email sent.");
        return StepResult.Ok();
    }
}

// ── Pipeline ───────────────────────────────────────────────────────────────

class Pipeline<T>
{
    private readonly List<IStep<T>> _steps = [];

    public Pipeline<T> AddStep(IStep<T> step)
    {
        _steps.Add(step);
        return this;
    }

    public Pipeline<T> AddStep(string name, Func<T, CancellationToken, ValueTask<StepResult>> func)
    {
        _steps.Add(new DelegateStep<T>(name, func));
        return this;
    }

    public async Task<StepResult> RunAsync(T payload, CancellationToken ct = default)
    {
        foreach (var step in _steps)
        {
            var result = await step.ExecuteAsync(payload, ct).ConfigureAwait(false);
            if (result is { IsSuccess: false })
            {
                Console.WriteLine($"[{step.Name}] ABORTED: {result.Error}");
                return result;
            }
        }
        return StepResult.Ok();
    }

    public IReadOnlyList<IStep<T>> Steps => _steps.AsReadOnly();
}

class DelegateStep<T>(string name, Func<T, CancellationToken, ValueTask<StepResult>> func) : IStep<T>
{
    public string Name => name;
    public ValueTask<StepResult> ExecuteAsync(T payload, CancellationToken ct) => func(payload, ct);
}

// ── Supporting types ───────────────────────────────────────────────────────

record StepResult(bool IsSuccess, string? Error = null)
{
    public static StepResult Ok()           => new(true);
    public static StepResult Fail(string e) => new(false, e);
}

class Order
{
    public int Id { get; set; }
    public string CustomerEmail { get; set; } = "";
    public List<string> Items { get; set; } = [];
    public decimal TotalAmount { get; set; }
}
