// Step 3: Wrap the list + runner into a generic Pipeline<T> class.
//
// C# features introduced:
//   Generics (Pipeline<T>, Func<T, StepResult>)
//     The pipeline knows nothing about Order specifically. T is the payload
//     type — swap Order for Invoice, Request, anything. The compiler enforces
//     type safety at the call site with zero runtime overhead.
//
//   Fluent builder pattern (method returns `this`)
//     AddStep() returns the Pipeline<T> instance so calls can be chained.
//     This is idiomatic C# — LINQ, StringBuilder, and most modern APIs use it.
//
//   Tuple deconstruction in foreach
//     Each step is stored as a (string Name, Func<T, StepResult> Execute) tuple.
//     The foreach deconstructs it inline: `var (name, execute) in _steps`.

var pipeline = new Pipeline<Order>()
    .AddStep("Validate",          Validate)
    .AddStep("Apply discount",    ApplyDiscount)
    .AddStep("Charge payment",    ChargePayment)
    .AddStep("Send confirmation", SendConfirmationEmail);

var order = new Order
{
    Id = 42,
    CustomerEmail = "alice@example.com",
    Items = ["Widget", "Gadget"],
    TotalAmount = 149.99m
};

Console.WriteLine($"Processing order #{order.Id}");
var final = pipeline.Run(order);
Console.WriteLine(final.IsSuccess ? $"Order #{order.Id} complete." : $"Order #{order.Id} failed.");

// ── Steps ─────────────────────────────────────────────────────────────────

static StepResult Validate(Order order)
{
    if (string.IsNullOrEmpty(order.CustomerEmail))
        return StepResult.Fail("Missing customer email.");
    if (order.Items.Count == 0)
        return StepResult.Fail("Order has no items.");

    Console.WriteLine("Validated.");
    return StepResult.Ok();
}

static StepResult ApplyDiscount(Order order)
{
    if (order.TotalAmount > 100)
    {
        order.TotalAmount *= 0.9m;
        Console.WriteLine($"Discount applied. New total: {order.TotalAmount:C}");
    }
    return StepResult.Ok();
}

static StepResult ChargePayment(Order order)
{
    Console.WriteLine($"Charging {order.TotalAmount:C} to {order.CustomerEmail}...");
    Console.WriteLine("Payment charged.");
    return StepResult.Ok();
}

static StepResult SendConfirmationEmail(Order order)
{
    Console.WriteLine($"Sending confirmation to {order.CustomerEmail}...");
    Console.WriteLine("Email sent.");
    return StepResult.Ok();
}

// ── Types ─────────────────────────────────────────────────────────────────

// Pipeline<T>: owns the step list and the runner logic.
// T is unconstrained — add `where T : class` later if needed.
class Pipeline<T>
{
    // Tuple list: pairs a human-readable name with the step delegate.
    private readonly List<(string Name, Func<T, StepResult> Execute)> _steps = [];

    // Returns `this` so calls chain fluently.
    public Pipeline<T> AddStep(string name, Func<T, StepResult> step)
    {
        _steps.Add((name, step));
        return this;
    }

    public StepResult Run(T payload)
    {
        foreach (var (name, execute) in _steps)
        {
            var result = execute(payload);
            if (result is { IsSuccess: false })
            {
                Console.WriteLine($"[{name}] ABORTED: {result.Error}");
                return result;
            }
        }
        return StepResult.Ok();
    }
}

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
