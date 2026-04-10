// Step 6: Middleware — cross-cutting behaviour that wraps the pipeline.
//
// C# features introduced:
//   Higher-order functions (Func that accepts and returns a Func)
//     A middleware is a function that takes the "next" handler and returns a
//     new handler. Composed together they form a chain: each middleware decides
//     whether to call next, when, and what to do with the result.
//     This is exactly how ASP.NET Core's Use() / middleware pipeline works.
//
//   Func<T, CancellationToken, Task<StepResult>> as a first-class type alias
//     We give the delegate type a name (StepHandler<T>) via a delegate declaration
//     so signatures stay readable instead of spelling out the full Func each time.
//
//   LINQ (Aggregate / Reverse)
//     Middleware is composed by folding the list from right to left with
//     Aggregate — a functional reduce. The innermost handler is the pipeline
//     runner; each middleware wraps it in turn.
//
//   Lambda expressions capturing outer scope (closures)
//     Each middleware lambda captures `next` from its parameter — the compiler
//     generates a hidden class to hold the captured variable. This is what
//     makes the chain work without manual plumbing.

// ── Middleware definitions ─────────────────────────────────────────────────

// 1. Timing middleware — measures wall-clock time for the whole pipeline.
PipelineMiddleware<Order> timingMiddleware = async (order, ct, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var result = await next(order, ct).ConfigureAwait(false);
    Console.WriteLine($"[Timing] Pipeline finished in {sw.ElapsedMilliseconds} ms");
    return result;
};

// 2. Logging middleware — prints entry/exit around every run.
PipelineMiddleware<Order> loggingMiddleware = async (order, ct, next) =>
{
    Console.WriteLine($"[Log] Starting pipeline for order #{order.Id}");
    var result = await next(order, ct).ConfigureAwait(false);
    Console.WriteLine($"[Log] Pipeline ended — success: {result.IsSuccess}");
    return result;
};

// 3. Error guard middleware — catches unhandled exceptions, returns a Fail result.
PipelineMiddleware<Order> errorGuardMiddleware = async (order, ct, next) =>
{
    try
    {
        return await next(order, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ErrorGuard] Unhandled exception: {ex.Message}");
        return StepResult.Fail($"Unexpected error: {ex.Message}");
    }
};

// ── Pipeline setup ─────────────────────────────────────────────────────────

var pipeline = new Pipeline<Order>()
    .AddStep(new ValidateStep())
    .AddStep(new ApplyDiscountStep())
    .AddStep(new ChargePaymentStep())
    .AddStep(new SendConfirmationStep())
    .Use(errorGuardMiddleware)   // outermost — catches everything below it
    .Use(loggingMiddleware)
    .Use(timingMiddleware);      // innermost — closest to the actual steps

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

// ── Delegate types ─────────────────────────────────────────────────────────

// Named delegate for the step handler — avoids repeating the full Func signature.
delegate Task<StepResult> StepHandler<T>(T payload, CancellationToken ct);

// A middleware takes the next handler and returns a new (wrapping) handler.
delegate Task<StepResult> PipelineMiddleware<T>(T payload, CancellationToken ct, StepHandler<T> next);

// ── Step interface ─────────────────────────────────────────────────────────

interface IStep<T>
{
    string Name => GetType().Name;
    ValueTask<StepResult> ExecuteAsync(T payload, CancellationToken ct = default);
}

// ── Step implementations ───────────────────────────────────────────────────

class ValidateStep : IStep<Order>
{
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
    private readonly List<PipelineMiddleware<T>> _middleware = [];

    public Pipeline<T> AddStep(IStep<T> step)          { _steps.Add(step);      return this; }
    public Pipeline<T> Use(PipelineMiddleware<T> mw)    { _middleware.Add(mw);   return this; }

    public Task<StepResult> RunAsync(T payload, CancellationToken ct = default)
    {
        // The innermost handler: run all steps in order.
        StepHandler<T> inner = async (p, token) =>
        {
            foreach (var step in _steps)
            {
                var result = await step.ExecuteAsync(p, token).ConfigureAwait(false);
                if (result is { IsSuccess: false })
                {
                    Console.WriteLine($"[{step.Name}] ABORTED: {result.Error}");
                    return result;
                }
            }
            return StepResult.Ok();
        };

        // Fold middleware from right to left so the first .Use() call ends up
        // outermost (called first). Each layer wraps the previous handler.
        //
        // [errorGuard, logging, timing] folded right-to-left:
        //   timing(logging(errorGuard(inner)))
        // Execution order: timing → logging → errorGuard → inner → unwind
        var handler = _middleware
            .Reverse<PipelineMiddleware<T>>()
            .Aggregate(inner, (next, mw) => (p, token) => mw(p, token, next));

        return handler(payload, ct);
    }

    public IReadOnlyList<IStep<T>> Steps => _steps.AsReadOnly();
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
