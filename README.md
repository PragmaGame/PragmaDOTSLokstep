# PragmaDOTSLokstep

Development project of **Pragma DOTS Lockstep** (`com.pragma.dotslockstep`): deterministic lockstep for Unity DOTS
(Entities), built on top of Netcode for Entities.

Every client runs the same simulation in its own isolated Entities world. Only player input travels over the network:
a server orders it into confirmed frames, and every client steps its simulation with exactly the same frames. The
server never simulates, and the bandwidth depends on the number of players, not on the number of entities.

**Documentation: [Assets/PragmaDOTSLockstep/README.md](Assets/PragmaDOTSLockstep/README.md).**

## Highlights

- An isolated, deterministic simulation world; gameplay systems opt in through `LockstepSimulationSystemGroup`.
- Fixed-point math (`FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath`, `FixedRandom`), bit-identical in Mono, IL2CPP and Burst.
- Per-tick input structs, never-dropped commands, players as entities.
- A server that orders input and never stalls, adaptive input timing, a jitter-aware playout buffer.
- Desync detection by majority vote with a per-component breakdown, replays, late join, offline mode.
- Netcode for Entities integration (host, dedicated server, thin clients) and a transport-agnostic core.

## Installation

```
https://github.com/PragmaGame/PragmaDOTSLokstep.git?path=Assets/PragmaDOTSLockstep
```

Requires Unity 6000.6+, Entities 6.6 and Netcode for Entities 6.6. See the
[package documentation](Assets/PragmaDOTSLockstep/README.md#installation).

## Repository

Only `Assets/PragmaDOTSLockstep` ships; everything else is the development project.

| Path | Contents |
|---|---|
| `Assets/PragmaDOTSLockstep` | The package: runtime, Netcode integration, authoring, editor tools, Claude Code skills (`Skills~`) |
| `Assets/Examples` | *Lockstep Arena*, a playable sample: offline, host and join ([README](Assets/Examples/README.md)) |
| `Assets/Tests/Editor` | EditMode tests: math, serialization, checksums, simulation, sessions, presentation, Netcode integration |
| [`documentation.md`](documentation.md) | Design notes: architecture, decisions, what was taken from ME.BECS and Photon Quantum and why |
| [`mebecs_research.md`](mebecs_research.md) | How ME.BECS implements determinism and networking |
| [`photon_research.md`](photon_research.md) | How Photon Quantum implements determinism and prediction/rollback |
| [`AGENTS.md`](AGENTS.md) | Conventions and workflow for contributors and AI agents |

## Sample

Open `Assets/Examples/Scenes/LockstepArena.unity` and press Play, then choose *Play offline*, *Host* or *Join*.
Move with WASD or the left stick, shoot with Space, dash with Shift, change color with C.

## Tests

Run the `Pragma.Lockstep.Tests.Editor` assembly in *Window > General > Test Runner > EditMode*, or with the `unity`
CLI of `com.unity.pipeline` while the editor is open:

```bash
unity command run_tests --mode editor --filter_type assembly --filter Pragma.Lockstep.Tests.Editor
```

## License

MIT, see [LICENSE.md](LICENSE.md).
