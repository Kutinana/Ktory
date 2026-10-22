namespace Ktory.Core.Runtime
{
    /// <summary>
    /// Outward-facing automatic playback policy data for host bridges and Web serialization.
    /// Exposes only execution settings (enabled, useEstimatedReadingTime, defaultWaitSeconds),
    /// keeping AST scope blocks and step references internal to avoid object cycles during JSON serialization.
    /// </summary>
    public class AutoPolicyData
    {
        public bool Enabled { get; set; } = true;
        public bool UseEstimatedReadingTime { get; set; }
        public double DefaultWaitSeconds { get; set; }

        public static AutoPolicyData? FromPolicy(AutoPolicy? policy)
        {
            if (policy == null) return null;
            return new AutoPolicyData
            {
                Enabled = policy.Enabled,
                UseEstimatedReadingTime = policy.UseEstimatedReadingTime,
                DefaultWaitSeconds = policy.DefaultWaitSeconds
            };
        }
    }
}
