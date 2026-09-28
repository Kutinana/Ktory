using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    /// <summary>
    /// Represents active lexical scope automatic playback settings compliant with Ktory Specification §2.7.
    /// Internal runtime policy keeping AST block references.
    /// </summary>
    public class AutoPolicy
    {
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// When true (#AUTO.wait), beats with text use language-specific estimated reading time.
        /// </summary>
        public bool UseEstimatedReadingTime { get; set; }

        /// <summary>
        /// Default hold duration in seconds when .wait(t) is not specified. Defaults to 0 (advance immediately when readable).
        /// </summary>
        public double DefaultWaitSeconds { get; set; }

        /// <summary>
        /// The block where this #AUTO directive was declared. Kept internal to runtime.
        /// </summary>
        public KtoryBlock? ScopeBlock { get; set; }

        public AutoPolicy() { }

        public AutoPolicy(KtoryBlock? scopeBlock, bool useEstimatedReadingTime = false, double defaultWaitSeconds = 0)
        {
            ScopeBlock = scopeBlock;
            UseEstimatedReadingTime = useEstimatedReadingTime;
            DefaultWaitSeconds = defaultWaitSeconds;
            Enabled = true;
        }

        public AutoPolicyData ToData()
        {
            return new AutoPolicyData
            {
                Enabled = Enabled,
                UseEstimatedReadingTime = UseEstimatedReadingTime,
                DefaultWaitSeconds = DefaultWaitSeconds
            };
        }

        public static implicit operator AutoPolicyData?(AutoPolicy? policy)
        {
            return policy?.ToData();
        }
    }
}
