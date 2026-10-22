using System;

namespace Ktory.Core.Runtime
{
    /// <summary>
    /// Identifies one presentation within one playback session. Capture the entire token when
    /// scheduling asynchronous input; a presentation number alone cannot distinguish restarts.
    /// </summary>
    public readonly struct PresentationToken : IEquatable<PresentationToken>
    {
        public string SessionId { get; }
        public long PresentationId { get; }

        public PresentationToken(string sessionId, long presentationId)
        {
            SessionId = sessionId ?? string.Empty;
            PresentationId = presentationId;
        }

        public bool Equals(PresentationToken other) =>
            string.Equals(SessionId, other.SessionId, StringComparison.Ordinal) &&
            PresentationId == other.PresentationId;

        public override bool Equals(object? obj) => obj is PresentationToken other && Equals(other);
        public override int GetHashCode() => unchecked(
            StringComparer.Ordinal.GetHashCode(SessionId ?? string.Empty) * 397 ^ PresentationId.GetHashCode());
        public static bool operator ==(PresentationToken left, PresentationToken right) => left.Equals(right);
        public static bool operator !=(PresentationToken left, PresentationToken right) => !left.Equals(right);
        public override string ToString() => $"{SessionId ?? string.Empty}:{PresentationId}";
    }
}
