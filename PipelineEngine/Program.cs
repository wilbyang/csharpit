// Step 2: Steps now return a StepResult so the runner can abort early.
//
// C# features introduced:
//   record        — an immutable value type with structural equality and
//                   a compact declaration syntax. Perfect for result objects
//                   that are created once and never mutated.
//   static members on records — Ok() and Fail() are factory methods that
//                   live on the type itself, keeping call sites readable.
//   pattern matching (switch expression) — the runner inspects the result
//                   with `result is { IsSuccess: false }` instead of
//                   branching on an enum or checking a bool + string pair.

var order = new Order
{
    Id = 42,
    CustomerEmail = "alice@example.com",
    Items = ["Widget", "Gadget"],
    TotalAmount = 149.99m
};

List<Func<Order, StepResult>> steps =
[
    Validate,
    ApplyDiscount,
    ChargePayment,
    SendConfirmationEmail,
];

Console.WriteLine($"Processing order #{order.Id}");
foreach (var step in steps)
{
    var result = step(order);
    if (result is { IsSuccess: false })
    {
        Console.WriteLine($"ABORTED: {result.Error}");
        break; // runner stops — no more steps run
    }
}

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

// ── Types must come after all top-level statements and local functions ────

// `record` gives us: constructor, ToString, ==, and immutability for free.
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
