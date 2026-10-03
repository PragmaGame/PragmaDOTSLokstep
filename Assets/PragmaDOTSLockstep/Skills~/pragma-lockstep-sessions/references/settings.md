# Session settings reference

## `LockstepServerSettings` (start from `LockstepServerSettings.Default`)

Serializable struct with properties, so it can be exposed in an inspector (`[SerializeField] private
LockstepServerSettings _serverSettings;`). `Validate()` throws for out-of-range values.

| Setting | Default | Meaning |
|---|---|---|
| `TickRate` | 30 | Ticks per second, 1-1000 |
| `MaxPlayers` | 8 | 1-64 |
| `InputSize` | 0 | `UnsafeUtility.SizeOf<TInput>()`, 0-128 bytes |
| `Seed` | 0 | 0 picks a random seed when the game starts |
| `MinPlayersToStart` | 1 | Auto start threshold; 0 = only `StartGame` / `RequestStart` |
| `StartDelaySeconds` | 0.5 | Countdown before tick 0 (clients create their worlds meanwhile) |
| `AllowLateJoin` | true | Late joiners receive all frames and re-simulate |
| `ChecksumInterval` | 60 | Ticks between state hashes (0 = off), at most 65535 |
| `MaxInputWaitTicks` | 0 | Ticks a frame may wait for missing input; 0 = repeat the previous input immediately |
| `MaxInputLeadTicks` | 0 | Inputs further ahead are ignored; 0 = four seconds (`EffectiveMaxInputLeadTicks`), at most 4096 |
| `FeedbackIntervalTicks` | 4 | Ticks between timing feedback messages per client |
| `MaxSendBytesPerUpdate` | 32 768 | Frame bytes per connection per update (late-join bursts) |
| `MaxPacketSize` | 1024 | Packet size limit; larger messages are fragmented. At least 64 |
| `ValidateSimulationHash` | true | Reject clients whose `LockstepSimulation.DefaultSimulationHash` differs |
| `StartData` | empty | `FixedList128Bytes<byte>` (126 bytes) for match parameters |

## `LockstepClientSettings` (start from `LockstepClientSettings.Default`)

| Setting | Default | Meaning |
|---|---|---|
| `Simulate` | true | False for bots and thin clients (no simulation, no checksums) |
| `InputMarginTicks` | 1.5 | Target arrival margin of input before its deadline; late inputs add up to 4 ticks automatically, decaying back |
| `PlayoutDelayTicks` | 1 | Frames kept in reserve before display; 2 × measured jitter is added |
| `MaxPlayoutDelayTicks` | 10 | Cap of the playout delay |
| `MaxTicksPerUpdate` | 60 | Most ticks simulated in one update while catching up |
| `MaxSimulationMillisecondsPerUpdate` | 16 | Time budget per update while catching up; at least one tick runs |
| `PingIntervalSeconds` | 0.5 | Round-trip measurement interval |
| `RecordReplay` | true | Keep all frames for `ExportReplay` |
| `StopOnDesync` | false | Stop simulating after a desync report |
| `MaxPacketSize` | 1024 | Packet size limit |

The offline mode uses `InputMarginTicks = 0.5` and `PlayoutDelayTicks = 0.5`, since the loopback has no latency.

## `LockstepOfflineConfig`

`Create<TInput>(tickRate = 30)` sets `tickRate`, `maxPlayers = 1`, `inputSize`. Other fields: `seed`,
`checksumInterval`, `startData`, `joinData`, `waitForPrefabRegistry`. The offline server starts as soon as the local
player joins (no countdown, no late join).

## `LockstepClientConfig` (Netcode client worlds)

`settings` (`LockstepClientSettings`), `joinData` (`FixedList64Bytes<byte>`), `waitForPrefabRegistry`.

## `LockstepServerConfig` (Netcode server worlds)

`settings` (`LockstepServerSettings`). Removing it shuts the session down.

## Latency

```
input delay ≈ RTT
            + 1 / TickRate                                   (the server closes tick T when its clock reaches T + 1)
            + (InputMarginTicks + automatic extra margin) / TickRate
            + min(PlayoutDelayTicks + 2 × jitter, MaxPlayoutDelayTicks) / TickRate
            + one rendered frame
```

Rough examples at RTT 60 ms and low jitter: 30 Hz about 200 ms, 20 Hz about 250 ms. Strategy games tolerate this
well; action games do not (no prediction or rollback in this package).

## Bandwidth

- Upstream per client: one input message per tick with the input bytes, or a one-byte repeat flag when unchanged,
  plus commands (header, payload and data; a command larger than a packet is fragmented and the whole stream behind
  it waits for it, so keep per-tick commands small and send big lists once).
- Downstream per client: one frame per tick. A frame lists only players with news (changed input, commands, join,
  leave): idle players cost nothing, an active player costs about its input size per tick.
- Late join: the whole history is sent, limited by `MaxSendBytesPerUpdate` per update.
- Checksums: one message per `ChecksumInterval` ticks per client.

## Timing internals

- The server closes tick T when its clock reaches T + 1 (after the start countdown).
- Each client's input clock is steered by `InputFeedback` (how early its inputs arrive, smoothed): small errors
  change its speed by -5 %..+10 %, errors above 3 ticks jump the clock and ignore feedback for one round trip plus 0.25 s.
- The playout clock targets `newest confirmed tick - playout delay` and simulates the frames that are due, within the
  catch-up budget; `InterpolationAlpha` is the fraction between the last two simulated ticks.
