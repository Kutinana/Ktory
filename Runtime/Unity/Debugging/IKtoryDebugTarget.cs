#if UNITY_EDITOR
using System.Collections.Generic;
using Ktory.Core.Runtime;
using UnityEngine;

namespace Ktory.Unity.Debugging
{
    /// <summary>
    /// Adapter to an existing live host session, not another player. All getters must be side-effect free.
    /// Register before Start; dispose on disable/destroy or before replacing the sequencer.
    /// Commands must use the same input gates, refresh and lifecycle paths as the game's UI.
    /// </summary>
    public interface IKtoryDebugTarget
    {
        Object Owner { get; }
        string DisplayName { get; }
        TextAsset? SourceAsset { get; }
        KtorySequencer Sequencer { get; }
        PresentationController? Presentation { get; }
        bool FollowsDefaultLanguage { get; }

        /// <summary>Null when the host accepts input; otherwise the project-specific reason it is blocked.</summary>
        string? InputBlockReason { get; }

        void RequestAdvance(long expectedPresentationId);
        void SubmitChoice(string optionId, long expectedPresentationId);
        /// <summary>Null means follow File.DefaultLang. Refresh text/UI without stepping or replaying tags.</summary>
        void SetLanguage(string? requestedLocale);
        void Restart();
    }

    /// <summary>Optional project-owned presentation/world diagnostics. The package never interprets custom tags.</summary>
    public interface IKtoryDebugInfoProvider
    {
        void CollectDebugInfo(ICollection<KeyValuePair<string, string>> entries);
    }
}
#endif
