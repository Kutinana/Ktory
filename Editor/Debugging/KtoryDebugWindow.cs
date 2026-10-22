using System;
using System.Collections.Generic;
using System.Text;
using Ktory.Core.Ast;
using Ktory.Core.Runtime;
using Ktory.Unity.Debugging;
using UnityEditor;
using UnityEngine;

namespace Ktory.Unity.Editor.Debugging
{
    public sealed class KtoryDebugWindow : EditorWindow
    {
        private int _selectedId;
        private Vector2 _scroll;
        private Vector2 _logScroll;
        private ExecutionTraceKind _filter = ExecutionTraceKind.All;
        private bool _allPlayers;
        private bool _showFlow;
        private bool _showLog = true;
        private string? _operationError;
        private double _nextRepaint;

        [MenuItem("Window/Ktory/Debugging")]
        public static void Open() => GetWindow<KtoryDebugWindow>("Ktory Debugging");

        private void OnEnable()
        {
            minSize = new Vector2(460, 520);
            EditorApplication.update += RefreshView;
        }
        private void OnDisable() => EditorApplication.update -= RefreshView;

        private void RefreshView()
        {
            if (EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + 0.1;
            Repaint(); // Observation only: the game remains the sole owner of Update and input.
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to connect to a running Ktory player.", MessageType.Info);
                return;
            }

            var players = KtoryDebugRegistry.GetPlayers();
            if (players.Count == 0)
            {
                EditorGUILayout.HelpBox("No running player is registered. Register the project's live IKtoryDebugTarget before starting its sequencer.", MessageType.Info);
                DrawLog();
                return;
            }

            int selected = 0;
            var names = new string[players.Count];
            for (int i = 0; i < players.Count; i++)
            {
                names[i] = $"{players[i].Target.DisplayName} [#{players[i].Id}]";
                if (players[i].Id == _selectedId) selected = i;
            }
            selected = EditorGUILayout.Popup("Live player", selected, names);
            var player = players[selected];
            if (_selectedId != player.Id) { _selectedId = player.Id; _operationError = null; }

            if (EditorApplication.isPaused)
                EditorGUILayout.HelpBox("Unity is paused. Timers advance only when the host updates.", MessageType.Info);
            if (!string.IsNullOrEmpty(_operationError)) EditorGUILayout.HelpBox(_operationError, MessageType.Error);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                DrawPlayer(player);
            }
            catch (ExitGUIException) { throw; }
            catch (Exception error)
            {
                EditorGUILayout.HelpBox("Cannot read host diagnostics: " + error.Message, MessageType.Error);
            }
            EditorGUILayout.EndScrollView();
            DrawLog();
        }

        private void DrawPlayer(KtoryDebugRegistration player)
        {
            var target = player.Target;
            var seq = target.Sequencer;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Owner", target.Owner, typeof(UnityEngine.Object), true);
                EditorGUILayout.ObjectField("Script", target.SourceAsset, typeof(TextAsset), false);
            }
            EditorGUILayout.LabelField("Execution", seq.Status.ToString());
            string? hostBlock = target.InputBlockReason;
            if (!string.IsNullOrEmpty(hostBlock)) EditorGUILayout.HelpBox("Host input blocked: " + hostBlock, MessageType.Warning);
            var active = seq.GetActivePresentations();
            EditorGUILayout.LabelField("Active presentations", active.Count.ToString());
            foreach (var lane in active) DrawLane(player, lane, hostBlock);
            var activeTokens = new HashSet<PresentationToken>();
            foreach (var lane in active) activeTokens.Add(lane.Token);
            foreach (var retained in seq.GetRetainedPresentations())
                if (!activeTokens.Contains(retained.Token)) DrawRetainedPresentation(target, retained);
            foreach (var gate in seq.GetPendingClockGates())
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"{gate.LaneName} · {gate.LaneId}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Waiting for source", $"{gate.ClockName} >= {gate.Position}");
                SourceButton(target.SourceAsset, gate.LineNumber);
            }
            var clocks = seq.GetClocks();
            if (clocks.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Shared clocks", EditorStyles.boldLabel);
                foreach (var clock in clocks)
                {
                    EditorGUILayout.LabelField($"{clock.Name} · instance {clock.ClockId}",
                        clock.Position.HasValue ? clock.Position.Value.ToString("0.###") : "No source sample");
                    EditorGUILayout.LabelField("Source", clock.SourceName ?? "Ktory elapsed time since declaration");
                    EditorGUILayout.LabelField("Declared at", $"session {clock.DeclaredAtElapsedSeconds:0.###} s · line {clock.LineNumber}");
                }
            }
            if (GUILayout.Button("Restart current script")) Run(player, target.Restart);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Language", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Script default", seq.DefaultLanguage);
            EditorGUILayout.LabelField("Primary request", seq.RequestedLanguage);
            bool follows = EditorGUILayout.Toggle("Follow script default", target.FollowsDefaultLanguage);
            if (follows != target.FollowsDefaultLanguage)
                Run(player, () => target.SetLanguage(follows ? null : seq.RequestedLanguage));
            using (new EditorGUI.DisabledScope(follows))
            {
                string locale = EditorGUILayout.DelayedTextField("Primary locale", seq.RequestedLanguage);
                if (!string.IsNullOrWhiteSpace(locale) && !string.Equals(locale.Trim(), seq.RequestedLanguage, StringComparison.OrdinalIgnoreCase))
                    Run(player, () => target.SetLanguage(locale.Trim()));
            }
            if (target is IKtorySecondaryLanguageDebugTarget secondaryTarget)
            {
                string secondary = EditorGUILayout.DelayedTextField("Secondary locale (blank = off)", seq.SecondaryLanguage ?? string.Empty);
                string? requested = string.IsNullOrWhiteSpace(secondary) ? null : secondary.Trim();
                if (!string.Equals(requested, seq.SecondaryLanguage, StringComparison.OrdinalIgnoreCase))
                    Run(player, () => secondaryTarget.SetSecondaryLanguage(requested));
            }
            else
            {
                EditorGUILayout.LabelField("Secondary request", seq.SecondaryLanguage ?? "Off");
                EditorGUILayout.LabelField("Host does not expose secondary language editing.", EditorStyles.wordWrappedMiniLabel);
            }

            _showFlow = EditorGUILayout.Foldout(_showFlow, "Control flow and session history", true);
            if (_showFlow)
            {
                if (active.Count > 1)
                    EditorGUILayout.LabelField("Stack diagnostics below show the parent scope; active outputs are listed above.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Call stack (top first)", EditorStyles.boldLabel);
                foreach (var frame in seq.GetCallStackSnapshot())
                    EditorGUILayout.LabelField($"{frame.Type}: {frame.Block} → {(frame.ResumeLine > 0 ? "line " + frame.ResumeLine : "end of section")}");
                if (seq.CallStack.Count == 0) EditorGUILayout.LabelField("(empty)");
                EditorGUILayout.LabelField("Active loops (inner first)", EditorStyles.boldLabel);
                foreach (var loop in seq.GetActiveLoopsSnapshot())
                    EditorGUILayout.LabelField($"{loop.Block}:L{loop.LineNumber} · iteration {loop.Iteration} / {(loop.Limit < 0 ? "∞" : loop.Limit.ToString())}");
                if (seq.ActiveLoopCount == 0) EditorGUILayout.LabelField("(empty)");
                EditorGUILayout.LabelField("Consumed one-time option IDs", EditorStyles.boldLabel);
                foreach (string id in seq.VisitedItemIds) EditorGUILayout.SelectableLabel(id, GUILayout.Height(18));
                if (seq.VisitedItemIds.Count == 0) EditorGUILayout.LabelField("(none)");
            }

            if (target is IKtoryDebugInfoProvider provider)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Project presentation state", EditorStyles.boldLabel);
                var info = new List<KeyValuePair<string, string>>();
                provider.CollectDebugInfo(info);
                foreach (var row in info) EditorGUILayout.LabelField(row.Key, row.Value, EditorStyles.wordWrappedLabel);
            }
        }

        private void DrawLane(KtoryDebugRegistration player, LanePresentation lane, string? hostBlock)
        {
            var target = player.Target;
            var laneTarget = target as IKtoryLaneDebugTarget;
            bool supportsInput = laneTarget != null || lane.LaneId == "root";
            var controller = laneTarget?.GetPresentationController(lane.LaneId) ??
                (lane.LaneId == "root" ? target.Presentation : null);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{lane.LaneName} · {lane.LaneId}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Presentation", $"{lane.Token.PresentationId} / {lane.Status}" + (lane.RequiresInput ? " / interactive" : " / automatic"));
            SourceButton(target.SourceAsset, lane.Payload?.LineNumber ?? lane.Choice?.LineNumber ?? 0);
            if (lane.Payload != null)
            {
                if (lane.Payload.SpeakerName != null) DrawLocalizedValue("Speaker", lane.Payload.SpeakerName);
                if (lane.Payload.StepType == StepType.Text) DrawLocalizedValue("Body", lane.Payload.Text);
                else EditorGUILayout.LabelField("Directive", lane.Payload.Content);
            }
            DrawTiming(lane, controller);
            bool allowed = supportsInput && string.IsNullOrEmpty(hostBlock) && lane.Status == ExecutionStatus.SuspendedAtBeat &&
                (controller == null || controller.CanHandleUserClick);
            using (new EditorGUI.DisabledScope(!allowed))
                if (GUILayout.Button(controller?.Phase == PresentationPhase.Printing ? "Reveal text (normal input)" : "Advance this lane (normal input)"))
                    Run(player, () =>
                    {
                        if (laneTarget != null) laneTarget.RequestAdvance(lane.Token);
                        else target.RequestAdvance(lane.Token.PresentationId);
                    });
            if (!supportsInput)
                EditorGUILayout.LabelField("This host exposes observation only for parallel lanes.", EditorStyles.wordWrappedMiniLabel);
            if (lane.Choice != null)
                foreach (var option in lane.Choice.Options)
                {
                    DrawLocalizedValue($"{option.Marker} Option", option.Text);
                    EditorGUILayout.LabelField($"Selectable: {option.CanSelect} / consumed: {option.IsConsumed}");
                    using (new EditorGUI.DisabledScope(!supportsInput || !option.CanSelect || !string.IsNullOrEmpty(hostBlock)))
                        if (GUILayout.Button("Submit option · line " + option.LineNumber))
                            Run(player, () =>
                            {
                                if (laneTarget != null) laneTarget.SubmitChoice(option.Id, lane.Token);
                                else target.SubmitChoice(option.Id, lane.Token.PresentationId);
                            });
                }
        }

        private static void DrawRetainedPresentation(IKtoryDebugTarget target, LanePresentation lane)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Retained display · {(lane.IsMain ? "Main" : "Auxiliary")} · {lane.LaneName} · {lane.LaneId}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Presentation", $"{lane.Token.PresentationId} / {lane.Status} / read only");
            SourceButton(target.SourceAsset, lane.Payload?.LineNumber ?? lane.Choice?.LineNumber ?? 0);
            if (lane.Payload != null)
            {
                if (lane.Payload.SpeakerName != null) DrawLocalizedValue("Speaker", lane.Payload.SpeakerName);
                if (lane.Payload.StepType == StepType.Text) DrawLocalizedValue("Body", lane.Payload.Text);
            }
            // Retained text may outlive its playhead or session execution. It never grants input.
        }

        private static void DrawLocalizedValue(string label, LocalizedTextOutput output)
        {
            EditorGUILayout.LabelField(label + " primary", LanguageLabel(output.Primary.ActualLanguage, output.Primary.RequestedLanguage));
            EditorGUILayout.SelectableLabel(output.Primary.Content, EditorStyles.textArea, GUILayout.MinHeight(32));
            if (output.Secondary != null)
            {
                EditorGUILayout.LabelField(label + " secondary", LanguageLabel(output.Secondary.ActualLanguage, output.Secondary.RequestedLanguage));
                EditorGUILayout.SelectableLabel(output.Secondary.Content, EditorStyles.textArea, GUILayout.MinHeight(32));
            }
        }

        private static void DrawTiming(LanePresentation lane, PresentationController? p)
        {
            EditorGUILayout.LabelField("Presentation and input", EditorStyles.boldLabel);
            if (lane.Status != ExecutionStatus.SuspendedAtBeat)
            {
                EditorGUILayout.HelpBox(lane.Status == ExecutionStatus.AwaitingChoice ? "Choose a selectable option to continue."
                    : "No active beat accepts advance input: " + lane.Status, MessageType.Info);
                return;
            }
            if (p == null)
            {
                EditorGUILayout.HelpBox("Host-managed presentation. Standard timers are unavailable; normal input still uses the host's rules. Supply custom state through IKtoryDebugInfoProvider.", MessageType.Info);
                return;
            }
            string phase = p.Phase == PresentationPhase.Printing ? "Printing"
                : p.Phase == PresentationPhase.Holding ? p.AllowClickInterruptHold ? "Ready to advance" : "Minimum hold"
                : p.Phase.ToString();
            EditorGUILayout.LabelField("Phase", phase);
            string reason = p.Phase == PresentationPhase.Printing
                ? p.CanHandleUserClick ? "A click reveals this text; it does not directly step the story."
                    : "Fast reveal is blocked by .skippable. Input is discarded."
                : p.Phase == PresentationPhase.Holding && !p.AllowClickInterruptHold
                    ? "Advance is blocked by the minimum hold. Input is discarded."
                : p.CanHandleUserClick ? "Normal advance is available." : "Waiting for the host to establish the next presentation.";
            EditorGUILayout.HelpBox(reason, MessageType.Info);
            EditorGUILayout.LabelField("Fast reveal lock", double.IsPositiveInfinity(p.FastForwardLockRemaining) ? "Until natural printing completion" : Seconds(p.FastForwardLockRemaining));
            string pending = p.Phase == PresentationPhase.Printing ? " (starts after printing)" : "";
            EditorGUILayout.LabelField("Minimum hold remaining", Seconds(p.MinimumHoldRemaining) + pending + (p.UsesEstimatedWait ? " · estimated" : ""));
            EditorGUILayout.LabelField("AUTO effective", p.AutoAdvanceOnHoldEnd ? "Yes · " + p.AutoAdvanceSource : "No");
            var policy = lane.AutoPolicy;
            EditorGUILayout.LabelField("Lexical AUTO policy", policy == null || !policy.Enabled ? "Off"
                : policy.UseEstimatedReadingTime ? "Estimated reading time" : Seconds(policy.DefaultWaitSeconds));
            EditorGUILayout.LabelField("Auto delay remaining", p.AutoAdvanceOnHoldEnd ? Seconds(p.AutoAdvanceRemaining) + pending + (p.UsesEstimatedAuto ? " · estimated" : "") : "Inactive");
            if (p.AutoAdvanceOnHoldEnd)
                EditorGUILayout.LabelField("Next automatic advance", Seconds(Math.Max(p.MinimumHoldRemaining, p.AutoAdvanceRemaining)) + pending);
            if (p.HasDeferredAdvance) EditorGUILayout.HelpBox("Automatic batch limit reached; continuation is deferred to the host's next update.", MessageType.Warning);
        }

        private void DrawLog()
        {
            _showLog = EditorGUILayout.Foldout(_showLog, $"Execution log (up to {KtoryDebugRegistry.LogCapacity} entries across all players)", true);
            if (!_showLog) return;
            _filter = (ExecutionTraceKind)EditorGUILayout.EnumFlagsField("Categories", _filter);
            _allPlayers = EditorGUILayout.Toggle("All players", _allPlayers);
            var entries = KtoryDebugRegistry.GetLogSnapshot();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear all")) { KtoryDebugRegistry.ClearLog(); entries = Array.Empty<KtoryDebugLogEntry>(); }
            if (GUILayout.Button("Copy filtered"))
            {
                var text = new StringBuilder();
                foreach (var entry in entries) if (Matches(entry))
                    text.Append(AssetDatabase.GetAssetPath(entry.SourceAsset)).Append(' ').AppendLine(entry.ToString());
                EditorGUIUtility.systemCopyBuffer = text.ToString();
            }
            EditorGUILayout.EndHorizontal();
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(200));
            // Newest first, so ongoing execution remains visible without moving the user's scroll.
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (!Matches(entry)) continue;
                if (GUILayout.Button(entry.ToString(), EditorStyles.wordWrappedMiniLabel) && entry.SourceAsset != null)
                    AssetDatabase.OpenAsset(entry.SourceAsset, Math.Max(1, entry.Trace.LineNumber));
            }
            EditorGUILayout.EndScrollView();
        }

        private bool Matches(KtoryDebugLogEntry entry) => (_filter & entry.Trace.Kind) != 0 && (_allPlayers || entry.PlayerId == _selectedId);
        private static string Seconds(double value) => value.ToString("0.00") + " s";
        private static string LanguageLabel(string? actual, string? requested) => string.IsNullOrEmpty(actual) ? "No output language"
            : actual + (string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase) ? " (requested)" : " (fallback from " + requested + ")");

        private static void SourceButton(TextAsset? asset, int line)
        {
            bool hasSource = asset != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset));
            using (new EditorGUI.DisabledScope(!hasSource || line <= 0))
                if (GUILayout.Button(line > 0 ? "Open source · line " + line : "Source location unavailable"))
                    AssetDatabase.OpenAsset(asset, line);
            if (!hasSource) EditorGUILayout.LabelField("No source asset path is available for this session.");
        }

        private void Run(KtoryDebugRegistration player, Action action)
        {
            try
            {
                if (EditorApplication.isPlaying && player.IsAlive) action();
                _operationError = null;
            }
            catch (ExitGUIException) { throw; }
            catch (Exception error)
            {
                _operationError = error.Message;
                KtoryDebugRegistry.ReportError(player, error);
            }
            GUIUtility.ExitGUI(); // The action may replace the player, payload or scene during this GUI event.
        }
    }
}
