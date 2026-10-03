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
| `Scripts/Simulation/ArenaInput.cs`, `ArenaChangeColorCommand.cs` | The input struct (quantized bytes) and a command |
| `Scripts/Simulation/ArenaAvatar.cs`, `ArenaProjectile.cs`, `ArenaRules.cs`, `ArenaViewKeys.cs` | Components, tuning written with `FixedPoint`, the keys of the views |
| `Scripts/Simulation/Arena*System.cs` | Deterministic systems, all in `FixedPoint`: avatars spawned when players join and removed when they leave (`ArenaPlayerLifecycleSystem`), input with button edges, shots and commands (`ArenaControlSystem`), movement, wall bounces and avatar collisions (`ArenaMovementSystem`), projectiles that fly, knock avatars back and score (`ArenaProjectileSystem`) |
| `Scripts/Client/ArenaInputSystem.cs` | Reads devices with the Input System and writes `LockstepLocalInput`; queues a command |
| `Views/` | GameObject views: the prefabs `AvatarView` and `ProjectileView`, and `ArenaViewConfig`, the catalog that binds them to the keys |
| `Scripts/Views/ArenaAvatarView.cs`, `ArenaAvatarViewUpdateSystem.cs` | A view part: the avatar in its player's color, the local player's avatar a little bigger |
| `Scripts/ArenaPresentation.cs`, `ArenaColors.cs` | The arena and the scoreboard, read straight from the simulation world |
| `Scripts/ArenaBootstrap.cs` | Offline, host and join flows with `LockstepOfflineConfig` and `LockstepNetcode` |

The avatars and projectiles get an `EntityViewKey` when the simulation creates them; the package spawns the view
prefabs from its pool, moves them with interpolation between ticks and returns them when the entities go away. The
scene registers the catalog with an `EntityViewConfigProvider` rather than baking it into a subscene, because the
networked worlds are created on demand, after the scene has loaded.

The scene has an `OverrideAutomaticNetcodeBootstrap` set to *Disable*, so Netcode creates only a local world at
start. The networked worlds are created on demand by the bootstrap. Hosting turns on `Application.runInBackground`,
which Netcode needs to keep connections alive without focus.

The sample systems belong to `LockstepSimulationSystemGroup`, so they run in every simulation world of the project.
Delete the `Examples` folder when you build your own game.
