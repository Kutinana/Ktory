using Ktory.Unity.Debugging;
using UnityEditor;

namespace Ktory.Unity.Editor.Debugging
{
    [InitializeOnLoad]
    internal static class KtoryDebugLifecycle
    {
        private static double _nextPrune;

        static KtoryDebugLifecycle()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += KtoryDebugRegistry.Reset;
            EditorApplication.update += Prune;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // Also runs when domain reload is disabled. Never reset after host OnEnable/Start registration.
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                KtoryDebugRegistry.Reset();
        }

        private static void Prune()
        {
            if (EditorApplication.timeSinceStartup < _nextPrune) return;
            _nextPrune = EditorApplication.timeSinceStartup + 0.25;
            KtoryDebugRegistry.Prune();
        }
    }
}
