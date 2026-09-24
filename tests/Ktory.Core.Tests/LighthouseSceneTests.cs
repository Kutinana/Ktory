using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Xunit;

namespace Ktory.Core.Tests
{
    public class LighthouseSceneTests
    {
        [Fact]
        public void FullLighthouseScene_ParsesAndExecutesCorrectly()
        {
            string script = @"
=== Scene_Lighthouse_Office ===

:
  @zh: 窗外风雪交加，狂风拍打着早已生锈的*铁栏杆*。
  @en: The blizzard raged outside, beating against the rusted *iron bars*.
  .bg(""Lighthouse_Snow"")
  .bgm(""Ambience_Blizzard"", volume=0.6)

主角:
  @zh: 房间里乱成一团，看来之前有人在这里匆忙寻找过什么。
  @en: The room is in complete disarray. Someone was searching for something in a hurry.

#do .camera_shake(intensity=0.4) .sfx(""metal_drop"") .next()

艾莉丝:
  @zh: 刚才的声音是从[书桌]{desk}那边传来的！
  @en: That sound came from the desk!
  .emotion(nervous)
  .voice(""vo_alice_042"")

#choice.loop.timeout(0)
  * [调查散落的文件]
    主角: 散落的**航海日志**被撕掉了最后几页。
      .inventory_add(""Torn_Page"", 1)
      .sfx(""paper_flip"")

  * ? {not inventory:Has(""DeskKey"")} [尝试打开书桌抽屉]
    主角: 抽屉被锁死了，必须找到对应的钥匙。
      .emotion(thinking)

  * ? {inventory:Has(""DeskKey"")} [用钥匙打开抽屉] => Sub_OpenDrawer

  + [向艾莉丝搭话]
    艾莉丝: 我们必须在暴风雨把灯塔彻底封死之前离开！
      .emotion(urgent)

  * [离开办公室] -> break

主角: 快没时间了，我们先走吧。

=== Sub_OpenDrawer ===
主角: 伴随着刺耳的摩擦声，抽屉被拉开了。
  .sfx(""drawer_open"")

主角: 里面只有一份盖着红印的<color=#FF0000>机密文件</color>。
  .inventory_add(""SecretDoc"", 1)

艾莉丝: 这就是真相吗……？
  .emotion(shocked)

-> return
";
            var file = KtoryParser.Parse(script);
            Assert.True(file.Blocks.ContainsKey("Scene_Lighthouse_Office"));
            Assert.True(file.Blocks.ContainsKey("Sub_OpenDrawer"));

            var sequencer = new KtorySequencer(file);
            sequencer.Start("Scene_Lighthouse_Office", "zh");

            // Beat 1: Narration (zh)
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.True(sequencer.CurrentPayload!.IsNarration);
            Assert.Contains("<i>铁栏杆</i>", sequencer.CurrentPayload.Content);

            // Beat 2: Protagonist line 1
            sequencer.Step();
            Assert.Equal("主角", sequencer.CurrentPayload!.Speaker);
            Assert.Equal("房间里乱成一团，看来之前有人在这里匆忙寻找过什么。", sequencer.CurrentPayload.Content);

            // Beat 3: Directive #do with tags
            sequencer.Step();
            Assert.Equal(StepType.Directive, sequencer.CurrentPayload!.StepType);
            Assert.Equal("do", sequencer.CurrentPayload.Content);
            Assert.True(sequencer.CurrentPayload.Tags.Count >= 3);

            // Beat 4: Alice line with ruby furigana
            sequencer.Step();
            Assert.Equal("艾莉丝", sequencer.CurrentPayload!.Speaker);
            Assert.Contains("<ruby=\"desk\">书桌</ruby>", sequencer.CurrentPayload.Content);

            // Beat 5: Choice menu
            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(5, sequencer.CurrentChoice!.Options.Count);

            // Select [向艾莉丝搭话] (+)
            sequencer.SubmitChoice("向艾莉丝搭话");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("艾莉丝", sequencer.CurrentPayload!.Speaker);
            Assert.Equal("我们必须在暴风雨把灯塔彻底封死之前离开！", sequencer.CurrentPayload.Content);

            // Step completes inline branch, loops back to menu
            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            // Persistent option (+) is still selectable
            Assert.True(sequencer.CurrentChoice!.Options[3].CanSelect);

            // Select [调查散落的文件] (*)
            sequencer.SubmitChoice("调查散落的文件");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Contains("<b>航海日志</b>", sequencer.CurrentPayload!.Content);

            // Step completes inline branch, loops back to menu
            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            // One-time option (*) is now consumed
            Assert.True(sequencer.CurrentChoice!.Options[0].IsConsumed);
            Assert.False(sequencer.CurrentChoice.Options[0].CanSelect);

            // Select [离开办公室] -> break
            sequencer.SubmitChoice("离开办公室");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("主角", sequencer.CurrentPayload!.Speaker);
            Assert.Equal("快没时间了，我们先走吧。", sequencer.CurrentPayload.Content);

            // Final step -> end
            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }
    }
}
