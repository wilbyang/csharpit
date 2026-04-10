// Step 1: Extract each processing step into its own method.
// Steps are stored as Action<Order> delegates in a list and executed in order.
//
// C# feature: delegates (Action<T>) — a method is a first-class value.
// You can store it in a variable, put it in a list, and call it later.
// This decouples the pipeline runner from the step implementations.
//
// Limitation still present: steps can't signal failure — the runner has no
// way to know a step failed and abort early. That's solved in Step 2.

var order = new Order
{
    Id = 42,
    CustomerEmail = "alice@example.com",
    Items = ["Widget", "Gadget"],
    TotalAmount = 149.99m
};

// The pipeline is now a list of delegates — easy to add, remove, or reorder.
List<Action<Order>> steps =
[
    Validate,
    ApplyDiscount,
    ChargePayment,
    SendConfirmationEmail,
];

Console.WriteLine($"Processing order #{order.Id}");
foreach (var step in steps)
    step(order);
Console.WriteLine($"Order #{order.Id} complete.");

// Each step is now an independent, named, testable method.

static void Validate(Order order)
{
    if (string.IsNullOrEmpty(order.CustomerEmail))
        Console.WriteLine("FAILED: Missing customer email.");
    else if (order.Items.Count == 0)
        Console.WriteLine("FAILED: Order has no items.");
    else
        Console.WriteLine("Validated.");
    // Problem: we printed the failure but execution continues anyway.
}

static void ApplyDiscount(Order order)
{
    if (order.TotalAmount > 100)
    {
        order.TotalAmount *= 0.9m;
        Console.WriteLine($"Discount applied. New total: {order.TotalAmount:C}");
    }
}

static void ChargePayment(Order order)
{
    Console.WriteLine($"Charging {order.TotalAmount:C} to {order.CustomerEmail}...");
    Console.WriteLine("Payment charged.");
}

static void SendConfirmationEmail(Order order)
{
    Console.WriteLine($"Sending confirmation to {order.CustomerEmail}...");
    Console.WriteLine("Email sent.");
}

class Order
{
    public int Id { get; set; }
    public string CustomerEmail { get; set; } = "";
    public List<string> Items { get; set; } = [];
    public decimal TotalAmount { get; set; }
}
