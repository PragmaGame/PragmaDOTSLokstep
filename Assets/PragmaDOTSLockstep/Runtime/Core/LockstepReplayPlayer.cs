using System;

namespace Pragma.Lockstep
{
    /// <summary>Plays a <see cref="LockstepReplay"/> into a fresh simulation, optionally verifying the recorded checksums.</summary>
    public sealed unsafe class LockstepReplayPlayer : IDisposable
    {
        private readonly LockstepReplay _replay;
        private double _clock;

        public LockstepReplayPlayer(LockstepReplay replay, LockstepSimulationOptions options = null)
        {
            _replay = replay ?? throw new ArgumentNullException(nameof(replay));
            Simulation = new LockstepSimulation(replay.Config, options);
        }

        public LockstepSimulation Simulation { get; }
        /// <summary>Playback speed multiplier.</summary>
        public float Speed { get; set; } = 1f;
        public bool VerifyChecksums { get; set; } = true;
        public bool IsFinished => Simulation.Tick >= _replay.FrameCount;
        /// <summary>First tick whose state did not match the recording, or -1.</summary>
        public int FirstMismatchTick { get; private set; } = -1;
        public float InterpolationAlpha { get; private set; } = 1f;

        /// <summary>Advances playback by real time.</summary>
        public void Update(double deltaTime, int maxTicks = 16)
        {
            _clock = Math.Min(_clock + deltaTime * Speed * Simulation.Config.TickRate, _replay.FrameCount + 1);
            var ticks = 0;
            while (!IsFinished && Simulation.Tick + 1 <= _clock && ticks < maxTicks)
            {
                StepOne();
                ticks++;
            }
            if (Simulation.Tick + 1 < _clock)
            {
                _clock = Simulation.Tick + 1;
            }
            InterpolationAlpha = (float)Math.Max(0, Math.Min(1, _clock - Simulation.Tick));
        }

        /// <summary>Simulates every remaining frame at once. Returns the first mismatching tick, or -1.</summary>
        public int SimulateToEnd()
        {
            while (!IsFinished)
            {
                StepOne();
            }
            _clock = Simulation.Tick + 1;
            InterpolationAlpha = 1f;
            return FirstMismatchTick;
        }

        public void Dispose() => Simulation.Dispose();

        private void StepOne()
        {
            var tick = Simulation.Tick;
            _replay.Frames.TryGet(tick, out var frame, out var length);
            Simulation.Step(frame, length);
            if (VerifyChecksums && FirstMismatchTick < 0 && _replay.Checksums.TryGetValue(tick, out var expected) &&
                Simulation.ComputeChecksum() != expected)
            {
                FirstMismatchTick = tick;
            }
        }
    }
}
