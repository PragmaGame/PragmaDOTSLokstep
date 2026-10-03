# GameObject views reference

Namespace `Pragma.Lockstep.Views` (assembly `Pragma.Lockstep.Views`); the authoring components are in
`Pragma.Lockstep.Authoring`. A port of ECV (Entity Component View) that never writes to the simulation world.

## Contents

- Setup
- Parts
- Update systems
- Catalogs
- Manager and views
- Pool
- Rules and pitfalls

## Setup

1. **Key.** `EntityViewKey { FixedString32Bytes value }` on the simulated entity, 1 to 29 bytes of UTF-8, case-sensitive.
   Set it where the entity is created (`new EntityViewKey("Knight")` or a `const string`, both fine in Burst) or bake
   `EntityViewKeyAuthoring` on a registry prefab. It is simulation state, part of the hash: set it the same way on every
   client. Changing it swaps the view.
2. **Prefab.** `EntityView` on the root, `TransformComponentView` to follow the interpolated `LockstepTransform`, one
   part per component type to show.
3. **Update system.** `public partial class HealthViewUpdateSystem : EntityViewUpdateSystem<Health> { }`, one per
   component type shown, in any assembly (its presentation filter keeps it out of simulation worlds).
4. **Catalog.** An `EntityViewConfig` asset binding keys to prefabs, baked or registered at runtime (below).

## Parts

| Type | Behaviour |
|---|---|
| `EntityComponentView<T>` | `UpdateData(T)` whenever the chunk holding the entity's `T` was written; may repeat a value |
| `EntityComponentViewUnmanaged<T>` | `OnUpdateData(T)` only when the bytes changed; the cache is cleared on `Bind` |
| `TransformComponentView` | Applies a `LocalTransform`: world position, rotation, uniform scale |

Virtual members: `Bind()` (the view got an entity), `BindBreak()` (it is about to lose it), `SetViewEnable(bool)`
(default: `SetActive`). `View` is the root: `View.Entity`, `View.Client` (`LocalSlot` for "is this mine"). Several
parts may show one type; a part under a nested `EntityView` belongs to that one. Parts are MonoBehaviours, so they
must live in runtime (non-Editor) assemblies.

## Update systems

- `EntityViewUpdateSystem<T>` (`T : unmanaged, IComponentData`) runs in `EntityViewUpdateSystemGroup`
  (`PresentationSystemGroup`, after `EntityViewManagerSystem`). It pushes the chunks whose `T` changed since its last
  push (change versions of the simulation world, so writes made outside systems such as `LockstepTime` count too) and
  every value to views spawned, attached or forced since then. Disabled components count as absent, and toggling an
  enableable component of the query counts as a change. `ConfigureQuery(builder)` narrows the query (simulation
  components only).
- `TransformViewUpdateSystem` runs every frame: `LockstepTransform` and `LockstepTransformPrevious` blended with
  `InterpolationAlpha` into a `LocalTransform`, for views with a `LocalTransform` part. A view snaps to the current
  transform until one tick after it was bound.

## Catalogs

- `EntityViewConfig` (*Create > Pragma > Lockstep > Entity View Config*): `EntityViewBinder { Key, View }` list;
  `SetBinders(...)` for configs built in code.
- Baked: `EntityViewConfigAuthoring` in a subscene loaded by the client or local world bakes an `EntityViewRegistry`
  entity with `EntityViewPrefabElement { key, UnityObjectRef<GameObject> prefab }`. Dedicated server builds bake nothing.
- Runtime: `EntityViewConfigProvider` on a scene object, or `EntityViewConfigs.Add(config)` / `Remove(config)`. Needed
  when presentation worlds are created after the scene loaded (Netcode worlds made on demand): a subscene loads only
  into the worlds that exist when it is enabled.
- Baked catalogs come first, then runtime ones; the first binder of a key wins, a conflicting one logs a warning.

## Manager and views

`EntityViewManager.TryGet(presentationWorld, out manager)`:

| Member | Notes |
|---|---|
| `Views`, `TryGetView(entity, out view)` | Spawned views by simulation entity |
| `Attach(entity, view)` | A view the caller owns (HUD panel, scene object): same data, never pooled; false when no session, no such entity or the view is bound |
| `Detach(view)` | Ends an attachment; also automatic when the entity or the session goes |
| `ForceUpdate(entity)` | Pushes every value of the entity at the next update |
| `Client` | The session shown |

`EntityView`: `Entity`, `Client`, `IsBound`, `IsAttached`, `IsAutoUpdateEnabled` (pauses the pushes, transform
included; turning it on catches up), `Transform`, `GetComponentView<T>()`, `SetComponentViewEnable<T>(bool)`,
`UpdateData<T>(T)`, `UpdateData(IComponentData)`, `IsHasHandler(Type)`, `IsHasHandlers(params Type[])`,
`RefreshComponentViews()`.

`EntityViewManagerSystem` spawns and returns views when a tick created, destroyed or re-keyed keyed entities (and
right away when a view was destroyed from outside): views of entities that went away or changed their key are returned
first, then new entities get theirs. A new session returns every view.

## Pool

- `IEntityViewPool`: `EntityView Spawn(EntityView prefab)`, `void Release(EntityView view)`. The manager binds after
  `Spawn` and unbinds before `Release`; the pool only keeps instances.
- Per project: `EntityViewManagerSystem.PoolFactory = world => ...` from the game's bootstrap (cleared when play mode
  starts). The manager owns the result: views go back to it when the world is destroyed, then it is disposed if it is
  `IDisposable`. To share one pool, return a thin wrapper per world.
- Per world: `world.GetExistingSystemManaged<EntityViewManagerSystem>().Pool = pool`; the spawned views go back to the
  previous pool first and the caller keeps ownership.
- Default `EntityViewPool(name, parent)`: inactive instances under one root object (kept across scene loads in play
  mode), `Prewarm(prefab, count)`, `InactiveCount`, `Dispose()` destroys every instance.
- com.pragma.pool keeps `PrefabPoolObject`s: give each view prefab one next to its `EntityView` and adapt:

```csharp
public sealed class PragmaPoolEntityViewPool : IEntityViewPool
{
    private readonly IPrefabPoolService _pools;

    public PragmaPoolEntityViewPool(IPrefabPoolService pools)
    {
        _pools = pools;
    }

    public EntityView Spawn(EntityView prefab)
    {
        return _pools.Spawn(prefab.GetComponent<PrefabPoolObject>()).GetComponent<EntityView>();
    }

    public void Release(EntityView view)
    {
        _pools.Release(view.GetComponent<PrefabPoolObject>());
    }
}

// Bootstrap:
EntityViewManagerSystem.PoolFactory = world => new PragmaPoolEntityViewPool(poolService);
```

## Rules and pitfalls

- Nothing in a view, a part or a view system may write to the simulation world, not even a tag: the entity would move
  to another archetype, which changes the chunk order, the state hash and the order `LockstepEntityId`s are assigned in.
- Local state (selection, hover, fog of war visibility, "is mine") belongs to the presentation: compare `data.slot`
  with `View.Client.LocalSlot`, keep selection on the view or in UI code, send changes as commands.
- `view.Entity` is valid only in this process and session. Commands refer to `LockstepEntityId`: read it from the
  simulation world with `view.Entity`.
- Parts run on the main thread for every push: keep `UpdateData` cheap and prefer `EntityComponentViewUnmanaged<T>`.
- A view that misses a type has no `EntityViewUpdateSystem<T>`.
- Pooled instances keep what their parts changed: restore it in `BindBreak` or `Bind`. `IsAutoUpdateEnabled` comes back
  on by itself when a view is unbound.
- Parts learn values, not absence: when `T` is removed or disabled the entity leaves the query and the part gets no
  call. A part for a tag can only ever show "on"; use a `bool` field for presence.
