#nullable enable
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using UnityEngine;
#if UNITY_EDITOR
using Ktory.Unity.Debugging;
#endif

// Minimal logging host to demonstrate wiring. Adapt the interface to your real player;
// do not create a second sequencer alongside an existing game session.
public sealed class DebuggableKtoryProbe : MonoBehaviour
#if UNITY_EDITOR
    , IKtoryDebugTarget
#endif
{
    [SerializeField] private TextAsset script = null!;
    private KtorySequencer _sequencer = null!;
    private PresentationController _presentation = null!;
    private string? _requestedLocale;
#if UNITY_EDITOR
    private KtoryDebugRegistration? _registration;
    public Object Owner => this;
    public string DisplayName => name;
    public TextAsset SourceAsset => script;
    public KtorySequencer Sequencer => _sequencer;
    public PresentationController Presentation => _presentation;
    public bool FollowsDefaultLanguage => _requestedLocale == null;
    public string? InputBlockReason => isActiveAndEnabled ? null : "Player disabled";
#endif

    private void OnEnable()
    {
        if (script == null) return;
        _sequencer = new KtorySequencer(KtoryParser.Parse(script.text));
        _presentation = new PresentationController(_sequencer);
        _presentation.OnBeatChanged += Render;
        _presentation.OnFastForwardRequested += RevealText;
        _sequencer.OnTagsDispatched += tags =>
        {
            foreach (var tag in tags) Debug.Log(tag.ToString(), this);
        };
#if UNITY_EDITOR
        _registration = KtoryDebugRegistry.Register(this);
#endif
        Restart();
    }

    private void OnDisable()
    {
        _sequencer?.InvalidateSession();
        if (_presentation != null)
        {
            _presentation.OnBeatChanged -= Render;
            _presentation.OnFastForwardRequested -= RevealText;
        }
#if UNITY_EDITOR
        _registration?.Dispose();
        _registration = null;
#endif
    }

    private void Update()
    {
        if (_presentation == null) return;
        _presentation.Update(Time.unscaledDeltaTime);
        // This probe has no typewriter. A real renderer notifies with its actual completion.
        if (_presentation.Phase == PresentationPhase.Printing)
            _presentation.NotifyPrintingFinished(_presentation.CurrentPresentationToken);
    }

    // The editor interface invokes this synchronously. Queued game UI uses the token overload.
    public void RequestAdvance(long expectedPresentationId)
    {
        if (!isActiveAndEnabled || expectedPresentationId != _sequencer.CurrentPresentationId) return;
        RequestAdvance(_sequencer.CurrentPresentationToken);
    }

    public void RequestAdvance(PresentationToken token)
    {
        if (!isActiveAndEnabled) return;
        // Apply the game's own additional input gates here, in the same method its UI uses.
        _presentation.HandleUserClick(token);
    }

    public void SubmitChoice(string optionId, long expectedPresentationId)
    {
        if (!isActiveAndEnabled || expectedPresentationId != _sequencer.CurrentPresentationId) return;
        SubmitChoice(optionId, _sequencer.CurrentPresentationToken);
    }

    public void SubmitChoice(string optionId, PresentationToken token)
    {
        if (!isActiveAndEnabled) return;
        bool current = token == _sequencer.CurrentPresentationToken && token.PresentationId > 0;
        _sequencer.SubmitChoice(optionId, token);
        if (!current) return; // An ignored input must not reset the new presentation's timers.
        _presentation.SetupForCurrentBeat();
        Render(); // SubmitChoice already entered the first branch beat. Never append Step().
    }

    public void SetLanguage(string? requestedLocale)
    {
        SetLanguage(requestedLocale, _sequencer.CurrentPresentationToken);
    }

    public void SetLanguage(string? requestedLocale, PresentationToken token)
    {
        if (!isActiveAndEnabled) return;
        bool current = !string.IsNullOrEmpty(token.SessionId) && token.SessionId == _sequencer.CurrentSessionId &&
            _sequencer.Status != ExecutionStatus.Error;
        _sequencer.SetLanguage(requestedLocale ?? _sequencer.DefaultLanguage, token);
        if (!current) return;
        _requestedLocale = requestedLocale;
        if (_sequencer.Status != ExecutionStatus.Completed)
            _presentation.RefreshLanguage(completePrintingOnLanguageSwitch: false);
        Render(); // Also redraw current choices. Do not call SetupForCurrentBeat or dispatch tags.
    }

    public void Restart()
    {
        _sequencer.InvalidateSession();
        // Replace the timing controller to cancel any deferred work from the previous session.
        _presentation.OnBeatChanged -= Render;
        _presentation.OnFastForwardRequested -= RevealText;
        _presentation = new PresentationController(_sequencer);
        _presentation.OnBeatChanged += Render;
        _presentation.OnFastForwardRequested += RevealText;
        _sequencer.Start(requestedLocale: _requestedLocale ?? _sequencer.DefaultLanguage);
        _presentation.SetupForCurrentBeat();
        Render();
    }

    // Capture when starting a typewriter job; invoke when that same job completes.
    // Keeping both objects prevents an old callback from acquiring the new session's token.
    public System.Action CapturePrintingCompletion()
    {
        var presentation = _presentation;
        var token = presentation.CurrentPresentationToken;
        return () => presentation.NotifyPrintingFinished(token);
    }

    private void RevealText() => Render();
    private void Render()
    {
        Debug.Log(_sequencer.CurrentPayload?.ToString() ?? _sequencer.Status.ToString(), this);
    }
}
