// Naive version: a simple order processing pipeline.
// No abstractions yet — just plain sequential logic in a method.

var order = new Order
{
    Id = 42,
    CustomerEmail = "alice@example.com",
    Items = ["Widget", "Gadget"],
    TotalAmount = 149.99m
};

ProcessOrder(order);

static void ProcessOrder(Order order)
{
    Console.WriteLine($"Processing order #{order.Id}");

    // Step 1: Validate
    if (string.IsNullOrEmpty(order.CustomerEmail))
    {
        Console.WriteLine("FAILED: Missing customer email.");
        return;
    }
    if (order.Items.Count == 0)
    {
        Console.WriteLine("FAILED: Order has no items.");
        return;
    }
    Console.WriteLine("Validated.");

    // Step 2: Apply discount
    if (order.TotalAmount > 100)
    {
        order.TotalAmount *= 0.9m; // 10% discount
        Console.WriteLine($"Discount applied. New total: {order.TotalAmount:C}");
    }

    // Step 3: Charge payment
    Console.WriteLine($"Charging {order.TotalAmount:C} to {order.CustomerEmail}...");
    // (imagine a real payment call here)
    Console.WriteLine("Payment charged.");

    // Step 4: Send confirmation email
    Console.WriteLine($"Sending confirmation to {order.CustomerEmail}...");
    // (imagine a real email call here)
    Console.WriteLine("Email sent.");

    Console.WriteLine($"Order #{order.Id} complete.");
}

class Order
{
    public int Id { get; set; }
    public string CustomerEmail { get; set; } = "";
    public List<string> Items { get; set; } = [];
    public decimal TotalAmount { get; set; }
}
