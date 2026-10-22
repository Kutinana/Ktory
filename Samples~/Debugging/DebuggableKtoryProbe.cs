#nullable enable
using System;
using System.Collections.Generic;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using UnityEngine;
#if UNITY_EDITOR
using Ktory.Unity.Debugging;
#endif

// Minimal logging host to demonstrate wiring. Adapt these interfaces to your real player;
// do not create a second sequencer alongside an existing game session.
public sealed class DebuggableKtoryProbe : MonoBehaviour
#if UNITY_EDITOR
    , IKtoryDebugTarget, IKtorySecondaryLanguageDebugTarget, IKtoryLaneDebugTarget
#endif
{
    [SerializeField] private TextAsset script = null!;
    private KtorySequencer _sequencer = null!;
    private MultiTrackPresentationController _presentations = null!;
    private readonly Dictionary<string, DisplayLifetimeController> _displays = new Dictionary<string, DisplayLifetimeController>();
    private string? _lastLoggedStatus;
    private string? _requestedLocale;
    private string? _secondaryLocale;
#if UNITY_EDITOR
    private KtoryDebugRegistration? _registration;
    public UnityEngine.Object Owner => this;
    public string DisplayName => name;
    public TextAsset SourceAsset => script;
    public KtorySequencer Sequencer => _sequencer;
    public PresentationController? Presentation => GetDefaultPresentation();
    public bool FollowsDefaultLanguage => _requestedLocale == null;
    public string? InputBlockReason => isActiveAndEnabled ? null : "Player disabled";
#endif

    private void OnEnable()
    {
        if (script == null) return;
        _sequencer = new KtorySequencer(KtoryParser.Parse(script.text));
        _sequencer.OnLaneTagsDispatched += dispatch =>
        {
            foreach (var tag in dispatch.Tags) Debug.Log($"[{dispatch.LaneName}] {tag}", this);
        };
        _sequencer.OnOutput += HandleOutput;
        _presentations = new MultiTrackPresentationController(_sequencer);
        _presentations.OnPresentationsChanged += Render;
        _presentations.OnFastForwardRequested += RevealText;
#if UNITY_EDITOR
        _registration = KtoryDebugRegistry.Register(this);
#endif
        Restart();
    }

    private void OnDisable()
    {
        _sequencer?.InvalidateSession();
        _displays.Clear();
        if (_presentations != null)
        {
            _presentations.OnPresentationsChanged -= Render;
            _presentations.OnFastForwardRequested -= RevealText;
        }
#if UNITY_EDITOR
        _registration?.Dispose();
        _registration = null;
#endif
    }

    private void Update()
    {
        if (_presentations == null) return;
        // Display lifetimes outlive execution; outputs created by this tick start at zero.
        foreach (var display in _displays.Values) display.Update(Time.unscaledDeltaTime);
        _presentations.Update(Time.unscaledDeltaTime);
        // This probe has no typewriter. Each token comes from the lane whose text finished;
        // a real renderer instead captures CapturePrintingCompletion(laneId) per job.
        foreach (var lane in _sequencer.GetActivePresentations())
            if (GetPresentationController(lane.LaneId)?.Phase == PresentationPhase.Printing)
                _presentations.NotifyPrintingFinished(lane.Token);
    }

    public PresentationController? GetPresentationController(string laneId) => _presentations.GetController(laneId);

    // Register a host source, e.g. BindClock("music.position", () => audioSource.time).
    // Script .clock(name="song", source="music.position") declares its session-wide instance.
    // The optional callback resets the host source on explicit reset; first declaration
    // does not invoke it. This sample never starts or controls audio on its own.
    public void BindClock(string name, Func<double> source) => _sequencer.BindClock(name, source);
    public void BindClock(string name, Func<double> source, Action? reset) => _sequencer.BindClock(name, source, reset);
    public void UnbindClock(string name) => _sequencer.UnbindClock(name);

    private PresentationController? GetDefaultPresentation()
    {
        var active = _sequencer.GetActivePresentations();
        foreach (var lane in active)
            if (lane.RequiresInput) return GetPresentationController(lane.LaneId);
        return active.Count == 1 ? GetPresentationController(active[0].LaneId) : null;
    }

    // Legacy editor commands execute synchronously. Queued game UI always captures a full token.
    public void RequestAdvance(long expectedPresentationId) =>
        RequestAdvance(new PresentationToken(_sequencer.CurrentSessionId, expectedPresentationId));

    public void RequestAdvance(PresentationToken token)
    {
        if (!isActiveAndEnabled) return;
        // Apply the game's own input gates here, in the same method its UI uses.
        _presentations.HandleUserClick(token);
    }

    public void SubmitChoice(string optionId, long expectedPresentationId) =>
        SubmitChoice(optionId, new PresentationToken(_sequencer.CurrentSessionId, expectedPresentationId));

    public void SubmitChoice(string optionId, PresentationToken token)
    {
        if (!isActiveAndEnabled) return;
        bool current = _sequencer.GetPresentation(token) != null;
        _sequencer.SubmitChoice(optionId, token);
        if (!current) return;
        _presentations.Synchronize();
        Render(); // SubmitChoice entered the first branch beat. Never append Step().
    }

    public void SetLanguage(string? requestedLocale) => SetLanguage(requestedLocale, _sequencer.CurrentPresentationToken);

    public void SetLanguage(string? requestedLocale, PresentationToken token)
    {
        if (!isActiveAndEnabled || !IsCurrentSession(token)) return;
        string previous = _sequencer.RequestedLanguage;
        _sequencer.SetLanguage(requestedLocale ?? _sequencer.DefaultLanguage, token);
        _requestedLocale = requestedLocale;
        if (!string.Equals(previous, _sequencer.RequestedLanguage, StringComparison.OrdinalIgnoreCase))
            _presentations.RefreshLanguage(completePrintingOnLanguageSwitch: false);
        else Render();
    }

    public void SetSecondaryLanguage(string? requestedLocale) =>
        SetSecondaryLanguage(requestedLocale, _sequencer.CurrentPresentationToken);

    public void SetSecondaryLanguage(string? requestedLocale, PresentationToken token)
    {
        if (!isActiveAndEnabled || !IsCurrentSession(token)) return;
        _sequencer.SetSecondaryLanguage(requestedLocale, token);
        _secondaryLocale = _sequencer.SecondaryLanguage;
        _presentations.RefreshLanguage(secondaryOnly: true); // Preserve every lane's typewriter and timers.
    }

    private bool IsCurrentSession(PresentationToken token) =>
        !string.IsNullOrEmpty(token.SessionId) && token.SessionId == _sequencer.CurrentSessionId &&
        _sequencer.Status != ExecutionStatus.Error;

    public void Restart()
    {
        _displays.Clear();
        _lastLoggedStatus = null;
        _sequencer.InvalidateSession();
        _sequencer.Start(requestedLocale: _requestedLocale ?? _sequencer.DefaultLanguage, secondaryLocale: _secondaryLocale);
        _presentations.Synchronize(); // New session identity discards all previous queued work.
        Render();
    }

    public Action CapturePrintingCompletion()
    {
        var presentation = GetDefaultPresentation() ??
            throw new InvalidOperationException("Specify the lane for a parallel printing job.");
        return CapturePrintingCompletion(presentation);
    }

    public Action CapturePrintingCompletion(string laneId)
    {
        var presentation = GetPresentationController(laneId) ??
            throw new InvalidOperationException("No active presentation for lane '" + laneId + "'.");
        return CapturePrintingCompletion(presentation);
    }

    private Action CapturePrintingCompletion(PresentationController presentation)
    {
        var manager = _presentations;
        var token = presentation.CurrentPresentationToken;
        return () => manager.NotifyPrintingFinished(token);
    }

    private void RevealText(PresentationToken token) => Render();

    private void HandleOutput(StoryOutput output)
    {
        if (output.Kind == StoryOutputKind.SessionEnded || output.Kind == StoryOutputKind.TrackEnded)
        {
            Debug.Log($"[{output.TrackName ?? output.TrackId}] {output.Kind}", this);
            return; // Ending execution does not remove a display or stop its duration.
        }
        if (output.Payload != null)
        {
            Debug.Log($"[{output.TrackName ?? output.TrackId}] {output.Payload}", this);
            var secondary = output.Payload.Text.Secondary;
            if (secondary != null) Debug.Log($"Secondary: {secondary.Content}", this);
            if (output.Kind == StoryOutputKind.Content && output.Payload.StepType == StepType.Text && output.Payload.Content.Length > 0)
            {
                if (!_displays.TryGetValue(output.TrackId, out var display))
                {
                    display = new DisplayLifetimeController();
                    display.OnHideRequested += token => Debug.Log($"Hide display content {token.PresentationId}", this);
                    _displays.Add(output.TrackId, display);
                }
                display.Show(output.Payload, output.Token);
                display.NotifyPrintingFinished(output.Token); // This logging probe displays the whole text immediately.
            }
        }
        if (output.Choice != null)
            foreach (var option in output.Choice.Options)
                Debug.Log($"[{output.TrackName ?? output.TrackId}] {option}", this);
    }

    private void Render()
    {
        var status = _sequencer.Status.ToString();
        if (_lastLoggedStatus == status) return;
        _lastLoggedStatus = status;
        Debug.Log(status, this);
    }
}
