// Step 4: Promote steps to first-class objects via IStep<T>.
//
// C# features introduced:
//   interface
//     Defines a contract — any class that implements IStep<T> can be added
//     to the pipeline. The pipeline depends on the abstraction, not any
//     specific implementation. This is the Dependency Inversion principle
//     in its most direct C# form.
//
//   default interface member (Name property with a default)
//     C# 8+ allows interfaces to provide a default implementation.
//     Here, Name defaults to the class name via GetType().Name, so simple
//     steps don't need to override it — only steps that want a custom label.
//
//   Overloaded AddStep
//     AddStep now accepts either an IStep<T> object or a raw Func<T, StepResult>
//     delegate (from the previous step). Both overloads work — no call sites break.

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
var final = pipeline.Run(order);
Console.WriteLine(final.IsSuccess ? $"Order #{order.Id} complete." : $"Order #{order.Id} failed.");

// ── Step interface ─────────────────────────────────────────────────────────

interface IStep<T>
{
    // Default implementation: use the class name as the label.
    // Implementors can override this to provide a friendlier name.
    string Name => GetType().Name;

    StepResult Execute(T payload);
}

// ── Step implementations ───────────────────────────────────────────────────

class ValidateStep : IStep<Order>
{
    public StepResult Execute(Order order)
    {
        if (string.IsNullOrEmpty(order.CustomerEmail))
            return StepResult.Fail("Missing customer email.");
        if (order.Items.Count == 0)
            return StepResult.Fail("Order has no items.");

        Console.WriteLine("Validated.");
        return StepResult.Ok();
    }
}

class ApplyDiscountStep : IStep<Order>
{
    public string Name => "Apply Discount";

    public StepResult Execute(Order order)
    {
        if (order.TotalAmount > 100)
        {
            order.TotalAmount *= 0.9m;
            Console.WriteLine($"Discount applied. New total: {order.TotalAmount:C}");
        }
        return StepResult.Ok();
    }
}

class ChargePaymentStep : IStep<Order>
{
    public string Name => "Charge Payment";

    public StepResult Execute(Order order)
    {
        Console.WriteLine($"Charging {order.TotalAmount:C} to {order.CustomerEmail}...");
        Console.WriteLine("Payment charged.");
        return StepResult.Ok();
    }
}

class SendConfirmationStep : IStep<Order>
{
    public string Name => "Send Confirmation";

    public StepResult Execute(Order order)
    {
        Console.WriteLine($"Sending confirmation to {order.CustomerEmail}...");
        Console.WriteLine("Email sent.");
        return StepResult.Ok();
    }
}

// ── Pipeline ───────────────────────────────────────────────────────────────

class Pipeline<T>
{
    private readonly List<IStep<T>> _steps = [];

    // Accept a full IStep<T> object.
    public Pipeline<T> AddStep(IStep<T> step)
    {
        _steps.Add(step);
        return this;
    }

    // Still accept a raw delegate — wraps it in an anonymous IStep<T> so
    // both overloads feed the same internal list. No call sites break.
    public Pipeline<T> AddStep(string name, Func<T, StepResult> func)
    {
        _steps.Add(new DelegateStep<T>(name, func));
        return this;
    }

    public StepResult Run(T payload)
    {
        foreach (var step in _steps)
        {
            var result = step.Execute(payload);
            if (result is { IsSuccess: false })
            {
                Console.WriteLine($"[{step.Name}] ABORTED: {result.Error}");
                return result;
            }
        }
        return StepResult.Ok();
    }

    // Expose the registered steps for inspection / tooling.
    public IReadOnlyList<IStep<T>> Steps => _steps.AsReadOnly();
}

// Adapter that lets a delegate satisfy the IStep<T> contract.
class DelegateStep<T>(string name, Func<T, StepResult> func) : IStep<T>
{
    public string Name => name;
    public StepResult Execute(T payload) => func(payload);
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
