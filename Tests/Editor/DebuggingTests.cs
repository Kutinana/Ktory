using System;
using System.Collections;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Ktory.Unity.Debugging;
using Ktory.Unity.Editor.Debugging;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ktory.Unity.Tests
{
    public class DebuggingTests
    {
        private sealed class Target : IKtoryDebugTarget
        {
            public UnityEngine.Object Owner { get; } = new GameObject("Debug test player");
            public string DisplayName => Owner.name;
            public TextAsset SourceAsset { get; }
            public KtorySequencer Sequencer { get; }
            public PresentationController Presentation { get; }
            public bool FollowsDefaultLanguage { get; private set; } = true;
            public string InputBlockReason { get; set; }
            public Target(string source = ": first .effect.wait(seconds=2)\n: second")
            {
                SourceAsset = new TextAsset(source);
                Sequencer = new KtorySequencer(KtoryParser.Parse(SourceAsset.text));
                Presentation = new PresentationController(Sequencer);
            }
            public void RequestAdvance(long id)
            {
                if (id == Sequencer.CurrentPresentationId && string.IsNullOrEmpty(InputBlockReason)) Presentation.HandleUserClick();
            }
            public void SubmitChoice(string id, long presentation) => Sequencer.SubmitChoice(id, presentation);
            public void SetLanguage(string locale)
            {
                FollowsDefaultLanguage = locale == null;
                Sequencer.SetLanguage(locale ?? Sequencer.DefaultLanguage);
                Presentation.RefreshLanguage(false);
            }
            public void Restart() { Sequencer.Start(); Presentation.SetupForCurrentBeat(); }
        }

        [UnityTest]
        public IEnumerator WindowRegistryAndNormalInput_PreserveLiveSession()
        {
            yield return new EnterPlayMode();
            var a = new Target();
            var b = new Target();
            var first = KtoryDebugRegistry.Register(a);
            var second = KtoryDebugRegistry.Register(b);
            try
            {
                a.Restart(); b.Restart();
                Assert.AreEqual(2, KtoryDebugRegistry.GetPlayers().Count);
                Assert.AreNotEqual(first.Id, second.Id);
                long id = a.Sequencer.CurrentPresentationId;
                int logCount = KtoryDebugRegistry.GetLogSnapshot().Count;
                KtoryDebugWindow.Open();
                yield return null;
                EditorWindow.GetWindow<KtoryDebugWindow>().Close();
                Assert.AreEqual(id, a.Sequencer.CurrentPresentationId);
                Assert.AreEqual(logCount, KtoryDebugRegistry.GetLogSnapshot().Count);
                a.SetLanguage("en");
                Assert.AreEqual(logCount, KtoryDebugRegistry.GetLogSnapshot().Count);
                a.RequestAdvance(id); // reveal
                a.RequestAdvance(id); // blocked by minimum hold
                Assert.AreEqual(id, a.Sequencer.CurrentPresentationId);
                a.Presentation.Update(2);
                a.RequestAdvance(id);
                Assert.AreEqual("second", a.Sequencer.CurrentPayload.Content);
                Assert.AreEqual("first", b.Sequencer.CurrentPayload.Content);
                UnityEngine.Object.DestroyImmediate(a.Owner);
                Assert.AreEqual(1, KtoryDebugRegistry.GetPlayers().Count);
                Assert.IsTrue(first.IsDisposed);
            }
            finally
            {
                first.Dispose(); second.Dispose();
                UnityEngine.Object.DestroyImmediate(a.Owner);
                UnityEngine.Object.DestroyImmediate(b.Owner);
                UnityEngine.Object.DestroyImmediate(a.SourceAsset);
                UnityEngine.Object.DestroyImmediate(b.SourceAsset);
            }
            yield return new ExitPlayMode();
            Assert.AreEqual(0, KtoryDebugRegistry.GetPlayers().Count);
            Assert.AreEqual(0, KtoryDebugRegistry.GetLogSnapshot().Count);
        }

        [UnityTest]
        public IEnumerator RetainedBilingualObservation_PreservesOriginsAndDoesNotRequireNewHostInterfaces()
        {
            yield return new EnterPlayMode();
            // An existing adapter still supports read-only auxiliary diagnostics; optional
            // token commands are deliberately absent on this legacy target.
            var target = new Target("@defaultLang: en\n#.clock(name=\"song\")\n#.track(source=\"Lyrics\", name=\"lyrics\")\n: hello\n  @zh: 你好\n  .effect\n=== Lyrics ===\n  : lyrics\n    @zh: 歌词\n    .at(value=0, clock=\"song\").duration(seconds=2).music");
            var registration = KtoryDebugRegistry.Register(target);
            try
            {
                target.Sequencer.Start();
                var before = target.Sequencer.GetActivePresentations();
                var retained = target.Sequencer.GetRetainedPresentations();
                Assert.AreEqual(1, before.Count);
                Assert.IsTrue(before[0].IsMain);
                Assert.AreEqual(2, retained.Count);
                bool dialogue = false, lyrics = false;
                foreach (var entry in KtoryDebugRegistry.GetLogSnapshot())
                {
                    if (entry.PlayerId != registration.Id || entry.Trace.Kind != ExecutionTraceKind.Tag) continue;
                    dialogue |= entry.Trace.LaneId == "root";
                    lyrics |= entry.Trace.LaneName == "lyrics";
                }
                Assert.IsTrue(dialogue && lyrics);
                int count = KtoryDebugRegistry.GetLogSnapshot().Count;
                KtoryDebugWindow.Open();
                yield return null;
                EditorWindow.GetWindow<KtoryDebugWindow>().Close();
                var after = target.Sequencer.GetActivePresentations();
                Assert.AreEqual(before[0].Token, after[0].Token);
                target.Sequencer.SetSecondaryLanguage("zh", before[0].Token);
                foreach (var display in target.Sequencer.GetRetainedPresentations())
                    Assert.AreEqual("zh", display.Payload.Text.Secondary.ActualLanguage);
                Assert.AreEqual(count, KtoryDebugRegistry.GetLogSnapshot().Count);

                target.Sequencer.Step(before[0].Token);
                Assert.AreEqual(ExecutionStatus.Completed, target.Sequencer.Status);
                Assert.AreEqual(0, target.Sequencer.GetActivePresentations().Count);
                target.Sequencer.SetLanguageOutput("zh", "en", before[0].Token);
                count = KtoryDebugRegistry.GetLogSnapshot().Count;
                KtoryDebugWindow.Open();
                yield return null;
                EditorWindow.GetWindow<KtoryDebugWindow>().Close();
                var ended = target.Sequencer.GetRetainedPresentations();
                Assert.AreEqual(2, ended.Count);
                for (int i = 0; i < ended.Count; i++)
                {
                    Assert.AreEqual(retained[i].Token, ended[i].Token);
                    Assert.AreEqual("zh", ended[i].Payload.Text.Primary.ActualLanguage);
                    Assert.AreEqual("en", ended[i].Payload.Text.Secondary.ActualLanguage);
                    Assert.IsFalse(ended[i].RequiresInput);
                }
                Assert.AreEqual(count, KtoryDebugRegistry.GetLogSnapshot().Count);
            }
            finally
            {
                registration.Dispose();
                UnityEngine.Object.DestroyImmediate(target.Owner);
                UnityEngine.Object.DestroyImmediate(target.SourceAsset);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Log_IsBoundedAndDisposalUnsubscribes()
        {
            yield return new EnterPlayMode();
            var target = new Target();
            var registration = KtoryDebugRegistry.Register(target);
            try
            {
                for (int i = 0; i < KtoryDebugRegistry.LogCapacity + 10; i++) target.Restart();
                Assert.AreEqual(KtoryDebugRegistry.LogCapacity, KtoryDebugRegistry.GetLogSnapshot().Count);
                registration.Dispose();
                KtoryDebugRegistry.ClearLog();
                target.Restart();
                Assert.AreEqual(0, KtoryDebugRegistry.GetLogSnapshot().Count);
            }
            finally
            {
                registration.Dispose();
                UnityEngine.Object.DestroyImmediate(target.Owner);
                UnityEngine.Object.DestroyImmediate(target.SourceAsset);
            }
            yield return new ExitPlayMode();
        }
    }
}
