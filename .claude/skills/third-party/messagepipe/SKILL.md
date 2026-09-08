---
name: messagepipe
description: "MessagePipe pub/sub for Unity — zero-alloc in-memory messaging, IPublisher/ISubscriber, buffered messages, request/response, VContainer broker registration. Use instead of C# events, UnityEvent, or SendMessage for cross-system communication."
globs: ["**/MessagePipe*", "**/*Publisher*.cs", "**/*Subscriber*.cs", "**/*Message*.cs", "**/*LifetimeScope*.cs"]
---

# MessagePipe — Zero-Allocation Pub/Sub for Unity

MessagePipe (Cysharp) is the only allowed messaging system in this project. It replaces C# `event`, `UnityEvent`, `SendMessage`, and hand-rolled singleton event buses.

## Why not plain C# events

| Concern | C# event / UnityEvent | MessagePipe |
|---|---|---|
| Publisher knows subscribers | Yes — tight coupling | No — decoupled by message type |
| Unsubscribe leaks | Very common (forgotten `-=`) | `IDisposable`, tied to scope/lifetime |
| Allocation on publish | Delegate + args boxing | Zero (struct messages, pooled) |
| Filters / interception | Manual | First-class filter pipeline |
| Testability | Needs real objects | Inject a fake `IPublisher<T>` |

## Message types

Messages are plain data. Prefer `readonly struct` — zero allocation on publish.

```csharp
public readonly struct BlocksCleared
{
    public readonly int Count;
    public readonly Vector2Int Origin;
    public BlocksCleared(int count, Vector2Int origin) { Count = count; Origin = origin; }
}
```

Name messages as **past-tense facts** (`BlocksCleared`, `LevelCompleted`, `ComboExtended`), not commands. A command that must be handled belongs in request/response, not pub/sub.

## Registration (VContainer)

Register the broker once in your `LifetimeScope`, then declare each message type:

```csharp
using MessagePipe;
using VContainer;
using VContainer.Unity;

public sealed class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        var options = builder.RegisterMessagePipe();

        builder.RegisterMessageBroker<BlocksCleared>(options);
        builder.RegisterMessageBroker<LevelCompleted>(options);

        builder.RegisterEntryPoint<ScoreSystem>();
        builder.RegisterComponentInHierarchy<BoardView>();
    }
}
```

`RegisterMessagePipe()` returns `MessagePipeOptions`; every message type needs its own `RegisterMessageBroker<T>(options)`. Forgetting it throws at resolve time, not compile time — that is the most common MessagePipe bug.

## Publishing and subscribing

Inject `IPublisher<T>` and `ISubscriber<T>` — never resolve them from a static locator.

```csharp
public sealed class MatchResolver
{
    readonly IPublisher<BlocksCleared> _published;

    public MatchResolver(IPublisher<BlocksCleared> published) => _published = published;

    public void Resolve(Vector2Int origin, int count)
        => _published.Publish(new BlocksCleared(count, origin));
}
```

```csharp
public sealed class ScoreSystem : IStartable, IDisposable
{
    readonly ISubscriber<BlocksCleared> _cleared;
    IDisposable _subscription;

    public ScoreSystem(ISubscriber<BlocksCleared> cleared) => _cleared = cleared;

    public void Start()
        => _subscription = _cleared.Subscribe(msg => AddScore(msg.Count));

    public void Dispose() => _subscription?.Dispose();
}
```

**Always dispose the subscription.** For several subscriptions use `DisposableBag`:

```csharp
var bag = DisposableBag.CreateBuilder();
_cleared.Subscribe(OnCleared).AddTo(bag);
_completed.Subscribe(OnCompleted).AddTo(bag);
_subscriptions = bag.Build();
```

On a `MonoBehaviour`, dispose in `OnDestroy`; the object's destruction does not unsubscribe it for you.

## Buffered messages — late subscribers

`IBufferedPublisher<T>` / `IBufferedSubscriber<T>` replay the most recent value to anyone who subscribes later. Use it for **current state** (score, level, remaining moves), not for events.

```csharp
builder.RegisterMessageBroker<ScoreChanged>(options);   // event: fire and forget
// buffered variant is resolved by asking for IBufferedPublisher<T>/IBufferedSubscriber<T>
```

A HUD that spawns after gameplay started still gets the current score this way, instead of showing zero until the next change.

## Async handlers

`IAsyncPublisher<T>` / `IAsyncSubscriber<T>` await every handler. Combine with UniTask (see [unitask]) and always pass the `CancellationToken`:

```csharp
await _asyncPublished.PublishAsync(new LevelCompleted(level), cancellationToken);
```

Use async publish only when the publisher genuinely must wait — a level-complete animation, a save flush. For fire-and-forget game events the sync `IPublisher<T>` is cheaper.

## Request/response

When you need exactly one answer from exactly one handler, that is not pub/sub:

```csharp
builder.RegisterRequestHandler<BoardQuery, BoardState, BoardQueryHandler>(options);
```

```csharp
public sealed class BoardQueryHandler : IRequestHandler<BoardQuery, BoardState>
{
    public BoardState Invoke(BoardQuery request) => ...;
}
```

## Rules for this project

- Messages are `readonly struct`, past tense, in the `Core` or `Gameplay` assembly — never in `Presentation`
- `Presentation` **subscribes**; it does not publish gameplay facts
- Every `Subscribe` produces an `IDisposable` that is disposed — no exceptions
- Register every message type in `LifetimeScope`; a missing `RegisterMessageBroker<T>` fails only at runtime
- Do not inject `IPublisher<T>` into `Core` types that should stay pure — return results instead and let the caller publish

## Testing

Because publishers are injected, tests need no container:

```csharp
var published = new List<BlocksCleared>();
var fake = new FakePublisher<BlocksCleared>(published.Add);
var resolver = new MatchResolver(fake);

resolver.Resolve(new Vector2Int(2, 3), count: 5);

Assert.That(published[0].Count, Is.EqualTo(5));
```

Assert on **published messages**, not on internal state — that keeps the test tied to observable behaviour.
