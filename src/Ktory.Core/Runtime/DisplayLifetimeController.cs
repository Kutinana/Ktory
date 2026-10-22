using System;

namespace Ktory.Core.Runtime
{
    /// <summary>
    /// Optional display lifetime for one playhead's current statement. Show replaces the old
    /// token even when the host changes display targets. It never advances the sequencer;
    /// continue ticking after execution ends if that last statement remains visible.
    /// </summary>
    public sealed class DisplayLifetimeController
    {
        public PresentationToken Token { get; private set; }
        public double? DurationSeconds { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public bool IsCounting { get; private set; }
        public bool IsHidden { get; private set; }
        public event Action<PresentationToken>? OnHideRequested;

        public void Show(TextPayload payload, PresentationToken token)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            Token = token;
            DurationSeconds = null;
            ElapsedSeconds = 0;
            IsCounting = false;
            IsHidden = false;
            foreach (var tag in payload.Tags)
                if (string.Equals(tag.Name, "duration", StringComparison.OrdinalIgnoreCase))
                    DurationSeconds = tag.GetNamed<double>("seconds", 0);
            if (payload.StepType == StepType.Directive || string.IsNullOrEmpty(payload.Content))
                NotifyPrintingFinished(token);
        }

        public void NotifyPrintingFinished(PresentationToken token)
        {
            if (token != Token || IsCounting || IsHidden || !DurationSeconds.HasValue) return;
            IsCounting = true;
            Update(0);
        }

        public void Update(double deltaTime)
        {
            if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (!IsCounting || IsHidden || !DurationSeconds.HasValue) return;
            ElapsedSeconds += deltaTime;
            if (ElapsedSeconds < DurationSeconds.Value) return;
            IsHidden = true;
            IsCounting = false;
            OnHideRequested?.Invoke(Token);
        }

        public void Clear()
        {
            IsCounting = false;
            DurationSeconds = null;
            Token = default;
        }
    }
}
