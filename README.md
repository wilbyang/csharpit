# C# Pipeline Engine — From Naive Code to Framework

This project builds a composable, async pipeline engine from scratch, one deliberate step at a time. The goal is not just to arrive at a working framework, but to show *why* each abstraction exists by letting the previous step's limitations motivate the next one.

Each commit introduces exactly one C# language feature or pattern, explained in context.

---

## The Idea

An order processing pipeline: validate → discount → charge → confirm. Simple enough to hold in your head, rich enough to expose real design problems as it grows.

The same domain stays constant throughout. Only the structure evolves.

---

## Evolution

### Step 1 — Naive baseline (`b870f74`)

Everything lives in a single `ProcessOrder()` method. Logic is sequential, early exit uses `return`, steps are invisible from the outside.

```csharp
static void ProcessOrder(Order order)
{
    if (string.IsNullOrEmpty(order.CustomerEmail)) { Console.WriteLine("FAILED"); return; }
    // ... discount, charge, email all inline
}
```

**Problems this exposes:**
- Steps cannot be reused, tested, or reordered without editing the method
- The caller cannot tell *why* processing stopped
- Adding a step means reading the whole method to find the right place

---

### Step 2 — Delegates: `Action<T>` (`4373c68`)

Each step becomes a named method. A `List<Action<Order>>` holds them; a `foreach` runs them in order.

```csharp
List<Action<Order>> steps = [Validate, ApplyDiscount, ChargePayment, SendConfirmationEmail];

foreach (var step in steps)
    step(order);
```

**C# feature — delegates (`Action<T>`):**
A method is a first-class value. `Action<Order>` is a type that represents any `void` method accepting an `Order`. Storing methods in a list decouples the runner from the implementations and makes the set of steps data rather than hard-coded control flow.

**Problem exposed:** `Action<T>` is fire-and-forget — a step has no way to signal failure, so the runner always executes every step regardless of what happened before.

---

### Step 3 — Result type: `record` + pattern matching (`22fbb87`)

Steps change from `Action<Order>` to `Func<Order, StepResult>`. The runner checks each result and breaks on failure.

```csharp
record StepResult(bool IsSuccess, string? Error = null)
{
    public static StepResult Ok()           => new(true);
    public static StepResult Fail(string e) => new(false, e);
}

foreach (var step in steps)
{
    var result = step(order);
    if (result is { IsSuccess: false }) { Console.WriteLine($"ABORTED: {result.Error}"); break; }
}
```

**C# features — `record` and property pattern matching:**
- `record` generates the constructor, `ToString`, `Equals`, and `GetHashCode` from a one-line declaration. Records are immutable by default, making result objects safe to pass around.
- `result is { IsSuccess: false }` is a property pattern — it reads like a guard clause and scales cleanly as the result type grows.
- Static factory methods (`Ok()`, `Fail()`) on the record itself keep call sites expressive.

**Problem exposed:** the step list and runner are loose top-level code — nothing protects the abort logic from being accidentally skipped or the list from being mutated.

---

### Step 4 — Fluent builder: generics + `Pipeline<T>` (`e769cf2`)

The list and runner are encapsulated in a `Pipeline<T>` class. `AddStep()` returns `this` for chaining.

```csharp
var pipeline = new Pipeline<Order>()
    .AddStep("Validate",       Validate)
    .AddStep("Charge payment", ChargePayment);

var result = pipeline.Run(order);
```

**C# features — generics and fluent builder:**
- `Pipeline<T>` knows nothing about `Order` specifically — swap in `Invoice`, `Request`, or anything. Type safety is enforced by the compiler with zero runtime overhead.
- `AddStep()` returns `this` (`Pipeline<T>`), enabling method chaining. This is the same pattern used by LINQ, `StringBuilder`, `HttpClient`, and virtually every modern C# API.
- Steps are stored as named value tuples `(string Name, Func<T, StepResult> Execute)`, deconstructed inline in `foreach` — no wrapper class needed.

**Problem exposed:** steps are still anonymous delegates — they carry no state, cannot receive injected dependencies, and cannot be introspected beyond a name string.

---

### Step 5 — First-class steps: `IStep<T>` interface (`509a883`)

Each step becomes a class implementing `IStep<T>`. The pipeline stores `List<IStep<T>>`.

```csharp
interface IStep<T>
{
    string Name => GetType().Name; // default implementation — free label
    ValueTask<StepResult> ExecuteAsync(T payload, CancellationToken ct = default);
}

class ValidateStep : IStep<Order>
{
    public ValueTask<StepResult> ExecuteAsync(Order order, CancellationToken ct) { ... }
}
```

**C# features — interface, default member, primary constructor:**
- An `interface` defines a contract. `Pipeline<T>` depends only on `IStep<T>` — new steps are added by creating a new class, not by editing the pipeline (Open/Closed Principle).
- Default interface members (C# 8+): `Name` defaults to the class name via `GetType().Name`. Simple steps get a sensible label for free; any step can override it.
- `DelegateStep<T>` uses a **primary constructor** (C# 12): parameters declared on the class line are promoted to fields automatically — no constructor body needed.
- `IReadOnlyList<IStep<T>> Steps` exposes the internal list for introspection without allowing mutation.

**Problem exposed:** steps run synchronously and block the calling thread. Any step doing real I/O (HTTP, database, email) wastes a thread while waiting.

---

### Step 6 — Async: `async/await`, `Task<T>`, `ValueTask<T>` (`557ddd0`)

`ExecuteAsync` returns `ValueTask<StepResult>`; `RunAsync` is fully async.

```csharp
public async ValueTask<StepResult> ExecuteAsync(Order order, CancellationToken ct)
{
    await Task.Delay(50, ct).ConfigureAwait(false); // real impl: await httpClient.PostAsync(...)
    return StepResult.Ok();
}
```

**C# features — async/await, Task vs ValueTask, CancellationToken:**
- `async`/`await`: the compiler rewrites each async method into a state machine. `await` suspends the method and releases the thread to the pool; it resumes when the result is ready. No callbacks, no manual state.
- `Task<StepResult>` for `RunAsync` (always does real async work); `ValueTask<StepResult>` for `ExecuteAsync` (avoids a heap allocation when the step completes synchronously, e.g. pure validation).
- `CancellationToken` threads through every boundary so a single cancellation at the top (e.g. request timeout) propagates through the whole pipeline.
- `ConfigureAwait(false)` on every internal `await` — correct for library code with no UI thread, avoids deadlocks in legacy ASP.NET.

**Problem exposed:** cross-cutting concerns (logging, timing, error handling, retry) have nowhere clean to live. They must be copy-pasted into every step or stuffed into the runner.

---

### Step 7 — Middleware: higher-order functions + LINQ `Aggregate` (`174bb5e`)

Middleware wraps the pipeline from the outside. Each middleware receives `next` and returns a new handler.

```csharp
PipelineMiddleware<Order> timingMiddleware = async (order, ct, next) =>
{
    var sw = Stopwatch.StartNew();
    var result = await next(order, ct);
    Console.WriteLine($"[Timing] {sw.ElapsedMilliseconds} ms");
    return result;
};

var pipeline = new Pipeline<Order>()
    .AddStep(new ValidateStep())
    .AddStep(new ChargePaymentStep())
    .Use(errorGuardMiddleware)
    .Use(loggingMiddleware)
    .Use(timingMiddleware);
```

Composition inside `RunAsync`:

```csharp
var handler = _middleware
    .Reverse<PipelineMiddleware<T>>()
    .Aggregate(inner, (next, mw) => (p, ct) => mw(p, ct, next));
```

**C# features — named delegates, higher-order functions, closures, LINQ `Aggregate`:**
- Named `delegate` types (`StepHandler<T>`, `PipelineMiddleware<T>`) give reusable, readable names to function signatures — they appear in IDE tooltips and stack traces with meaningful names, unlike raw `Func<>`.
- A middleware is a higher-order function: it accepts a function (`next`) and returns a new function that wraps it. No base class, no attribute, no framework hook.
- `Aggregate` (functional reduce) folds the middleware list into a nested call chain at zero runtime cost beyond delegate allocations. Reversing first ensures `.Use()` call order matches execution order intuitively.
- Each fold iteration produces a lambda that closes over `next`. The compiler generates a hidden class to hold the captured variable — this is the mechanism behind the chain.

---

## Final structure

```
Pipeline<T>
├── List<IStep<T>>              — business logic, added with .AddStep()
├── List<PipelineMiddleware<T>> — cross-cutting concerns, added with .Use()
└── RunAsync()                  — composes middleware chain, then runs steps
```

This mirrors the architecture of production frameworks:

| This project | ASP.NET Core | MediatR |
|---|---|---|
| `IStep<T>` | `IMiddleware` / handler | `IRequestHandler<T>` |
| `PipelineMiddleware<T>` | `RequestDelegate` middleware | `IPipelineBehavior<T>` |
| `Pipeline<T>.Use()` | `app.Use()` | behaviour registration |
| `RunAsync()` | `app.Build()` + `Run()` | `mediator.Send()` |

---

## Running

```bash
cd PipelineEngine
dotnet run
```

Requires .NET 8 or later.

---

## Reading the history

Each commit is a self-contained step. The commit message explains what changed, which C# feature it introduces, and what limitation it exposes for the next step.

```bash
git log --oneline
git show <hash>   # full diff + explanation for any step
```
