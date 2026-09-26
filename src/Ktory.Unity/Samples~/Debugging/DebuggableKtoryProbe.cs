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
        if (_presentation.Phase == PresentationPhase.Printing) _presentation.NotifyPrintingFinished();
    }

    public void RequestAdvance(long expectedPresentationId)
    {
        if (!isActiveAndEnabled || expectedPresentationId != _sequencer.CurrentPresentationId) return;
        // Apply the game's own additional input gates here, in the same method its UI uses.
        _presentation.HandleUserClick();
    }

    public void SubmitChoice(string optionId, long expectedPresentationId)
    {
        if (!isActiveAndEnabled || expectedPresentationId != _sequencer.CurrentPresentationId) return;
        _sequencer.SubmitChoice(optionId, expectedPresentationId);
        _presentation.SetupForCurrentBeat();
        Render(); // SubmitChoice already entered the first branch beat. Never append Step().
    }

    public void SetLanguage(string? requestedLocale)
    {
        _requestedLocale = requestedLocale;
        _sequencer.SetLanguage(requestedLocale ?? _sequencer.DefaultLanguage);
        _presentation.RefreshLanguage(completePrintingOnLanguageSwitch: false);
        Render(); // Also redraw current choices. Do not call SetupForCurrentBeat or dispatch tags.
    }

    public void Restart()
    {
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

    private void RevealText() => Render();
    private void Render()
    {
        Debug.Log(_sequencer.CurrentPayload?.ToString() ?? _sequencer.Status.ToString(), this);
    }
}
