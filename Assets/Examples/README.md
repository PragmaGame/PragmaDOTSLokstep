# Lockstep Arena (sample)

Open `Scenes/LockstepArena.unity` and press Play. The panel in the top-left corner starts the game:

- **Play offline** runs a session against an in-process server. It is the same protocol, just without a network.
- **Host** creates a Netcode server world and a client world in this editor and listens on the port.
- **Join** creates a client world and connects to the address and port. Use it from a build, from a second editor,
  or from a Multiplayer Play Mode virtual player.

| Action | Keyboard | Gamepad |
|---|---|---|
| Move | WASD / arrows | Left stick |
| Shoot | Space | South button |
| Dash | Left Shift | East button |
| Change color (a command) | C | North button |

Projectiles knock other players back and score for the shooter. The scores in the top-right corner come straight
from the simulation, so they are identical on every client. *Window > Pragma > Lockstep Sessions* shows the session
state, the round-trip time, the jitter, and the state hashes.

## What is here

| File | Shows |
|---|---|
| `Scripts/Simulation/ArenaComponents.cs` | The input struct (quantized bytes), a command, components and tuning written with `FixedPoint` |
| `Scripts/Simulation/ArenaSystems.cs` | Deterministic systems: avatars spawned when players join and removed when they leave, movement with button edges, shots, wall bounces, collisions, all in `FixedPoint` |
| `Scripts/Client/ArenaInputSystem.cs` | Reads devices with the Input System and writes `LockstepLocalInput`; queues a command |
| `Scripts/ArenaPresentation.cs` | GameObject views reading the simulation world, interpolated between ticks |
| `Scripts/ArenaBootstrap.cs` | Offline, host and join flows with `LockstepOfflineConfig` and `LockstepNetcode` |

The scene has an `OverrideAutomaticNetcodeBootstrap` set to *Disable*, so Netcode creates only a local world at
start. The networked worlds are created on demand by the bootstrap. Hosting turns on `Application.runInBackground`,
which Netcode needs to keep connections alive without focus.

The sample systems belong to `LockstepSimulationSystemGroup`, so they run in every simulation world of the project.
Delete the `Examples` folder when you build your own game.
