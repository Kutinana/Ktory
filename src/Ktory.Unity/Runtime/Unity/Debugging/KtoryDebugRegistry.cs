#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ktory.Core.Runtime;
using UnityEngine;

namespace Ktory.Unity.Debugging
{
    public sealed class KtoryDebugLogEntry
    {
        public DateTime TimeUtc { get; }
        public int PlayerId { get; }
        public string PlayerName { get; }
        public TextAsset? SourceAsset { get; }
        public ExecutionTrace Trace { get; }

        internal KtoryDebugLogEntry(KtoryDebugRegistration player, ExecutionTrace trace)
        {
            TimeUtc = DateTime.UtcNow;
            PlayerId = player.Id;
            PlayerName = player.Target.DisplayName;
            SourceAsset = player.Target.SourceAsset;
            // Bound retained text as well as entry count, even for huge tag arguments/errors.
            Trace = new ExecutionTrace(trace.Kind, Clip(trace.Block), trace.LineNumber, Clip(trace.Message));
        }

        private static string Clip(string value) => value.Length <= 4096 ? value : value.Substring(0, 4096) + "…";
        public override string ToString() => $"{TimeUtc:O} [{PlayerName} #{PlayerId}] {Trace.Kind} {Trace.Block}:L{Trace.LineNumber} {Trace.Message}";
    }

    public sealed class KtoryDebugRegistration : IDisposable
    {
        public int Id { get; }
        public IKtoryDebugTarget Target { get; }
        public bool IsDisposed { get; private set; }
        private readonly KtorySequencer _sequencer;

        internal KtoryDebugRegistration(int id, IKtoryDebugTarget target)
        {
            Id = id;
            Target = target;
            _sequencer = target.Sequencer;
            _sequencer.OnTrace += Record;
        }

        private void Record(ExecutionTrace trace)
        {
            if (IsAlive) KtoryDebugRegistry.Record(this, trace);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _sequencer.OnTrace -= Record;
            KtoryDebugRegistry.Remove(this);
        }

        public bool IsAlive => !IsDisposed && Target.Owner != null && ReferenceEquals(_sequencer, Target.Sequencer);
    }

    /// <summary>Editor-only, main-thread registry. Independent of window lifetime; never ticks playback.</summary>
    public static class KtoryDebugRegistry
    {
        public const int LogCapacity = 2000;
        private static readonly List<KtoryDebugRegistration> Players = new List<KtoryDebugRegistration>();
        private static readonly Queue<KtoryDebugLogEntry> Log = new Queue<KtoryDebugLogEntry>();
        private static int _nextId;

        public static KtoryDebugRegistration Register(IKtoryDebugTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.Owner == null) throw new ArgumentException("A live Unity owner is required.", nameof(target));
            if (!Application.isPlaying) throw new InvalidOperationException("Register a running Play Mode session.");
            Prune();
            foreach (var player in Players)
                if (ReferenceEquals(player.Target, target)) return player;
            var registration = new KtoryDebugRegistration(++_nextId, target);
            Players.Add(registration);
            return registration;
        }

        public static IReadOnlyList<KtoryDebugRegistration> GetPlayers()
        {
            Prune();
            return Players.ToArray();
        }

        public static void Prune()
        {
            for (int i = Players.Count - 1; i >= 0; i--)
                if (!Players[i].IsAlive) Players[i].Dispose();
        }

        internal static void Remove(KtoryDebugRegistration player) => Players.Remove(player);
        internal static void Record(KtoryDebugRegistration player, ExecutionTrace trace)
        {
            if (Log.Count == LogCapacity) Log.Dequeue();
            Log.Enqueue(new KtoryDebugLogEntry(player, trace));
        }

        /// <summary>Report project input/presentation failures not raised by the sequencer.</summary>
        public static void ReportError(KtoryDebugRegistration player, Exception error)
        {
            if (!player.IsAlive) return;
            var seq = player.Target.Sequencer;
            Record(player, new ExecutionTrace(ExecutionTraceKind.Error, seq.CurrentBlockLabel, seq.CurrentLineNumber, error.Message));
        }

        public static IReadOnlyList<KtoryDebugLogEntry> GetLogSnapshot() => Log.ToArray();
        public static void ClearLog() => Log.Clear();

        public static void Reset()
        {
            while (Players.Count > 0) Players[Players.Count - 1].Dispose();
            Log.Clear();
        }
    }
}
#endif
