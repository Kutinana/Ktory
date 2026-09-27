using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Ktory.Wasm;
using Xunit;

namespace Ktory.Core.Tests;

public class SectionBoundaryTests
{
    [Fact]
    public void UnindentedTextAfterNamedHeader_BelongsToRoot()
    {
        var file = KtoryParser.Parse("=== Aside ===\n: root first\n: root second");

        Assert.Empty(file.Blocks["Aside"].Steps);
        Assert.Collection(file.RootBlock.Steps,
            step => Assert.Equal("root first", Assert.IsType<TextStep>(step).TextVariants[""]),
            step => Assert.Equal("root second", Assert.IsType<TextStep>(step).TextVariants[""]));

        var sequencer = new KtorySequencer(file);
        sequencer.Start();
        Assert.Equal("root first", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal("root second", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
    }

    [Fact]
    public void EmptyNamedSection_DoesNotCaptureLaterIndentedRootText()
    {
        var file = KtoryParser.Parse("=== Empty ===\n// comment\n: root\n  : still root\n=== Aside ===\n  : aside\n: after");

        Assert.Empty(file.Blocks["Empty"].Steps);
        Assert.Equal(3, file.RootBlock.Steps.Count);
        Assert.Equal(new[] { 3, 4, 7 }, file.RootBlock.Steps.ConvertAll(step => step.LineNumber));
        Assert.Equal(6, Assert.Single(file.Blocks["Aside"].Steps).LineNumber);
    }

    [Theory]
    [InlineData("", "  ")]
    [InlineData("  ", "    ")]
    [InlineData("", "\t")]
    public void NamedSectionEndsAtHeaderIndent_AndRootSkipsItsBody(string headerIndent, string bodyIndent)
    {
        var file = KtoryParser.Parse($": first\n{headerIndent}=== Aside ===\n{bodyIndent}: aside\n{headerIndent}: after");

        Assert.Equal(3, Assert.Single(file.Blocks["Aside"].Steps).LineNumber);
        Assert.Equal(new[] { 1, 4 }, file.RootBlock.Steps.ConvertAll(step => step.LineNumber));

        var sequencer = new KtorySequencer(file);
        sequencer.Start();
        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal("after", sequencer.CurrentPayload!.Content);
    }

    private const string FallbackLighthouseSample = @"
@defaultLang: zh
@speaker prot: zh=主角 | en=Protagonist
@speaker alice: zh=艾莉丝 | en=Alice
-> Scene_Lighthouse_Office
=== Scene_Lighthouse_Office ===
  :
    @zh: 窗外风雪交加，狂风拍打着早已生锈的*铁栏杆*。
    @en: The blizzard raged outside, beating against the rusted *iron bars*.
    .bg(""Lighthouse_Snow"")
    .bgm(""Ambience_Blizzard"", volume=0.6)
  主角:
    @zh: 房间里乱成一团，看来之前有人在这里匆忙寻找过什么。
    @en: The room is in complete disarray. Someone was searching for something in a hurry.
  #do .camera_shake(intensity=0.4) .sfx(""metal_drop"") .next(0.5)
  艾莉丝:
    @zh: 刚才的声音是从[书桌]{desk}那边传来的！
    @en: That sound came from the desk!
    .emotion(nervous)
    .voice(""vo_alice_042"")
  #choice.loop
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
  主角: 里面只有一份盖着红印的<color=#FF5555>机密文件</color>。
    .inventory_add(""SecretDoc"", 1)
  艾莉丝: 这就是一直在隐瞒的真相吗……？
    .emotion(shocked)
  -> return
";

    private const string FallbackInvestigationSample = @"
@defaultLang: zh
-> Room_Investigation
=== Room_Investigation ===
  #choice.loop
    * [查看书桌]
      #do .sfx(""paper"")
      主角: 桌上有一张泛黄的日记碎片。
    * [检查窗户]
      主角: 窗户被铁栅栏焊死了。
    + [尝试撞门]
      主角: 门纹丝不动，撞得肩膀生疼。
    + [放弃思考] -> break
  主角: 我们离开这里吧。
";

    [Fact]
    public void LighthouseSample_DefaultEntryAndDrawerCall_PreserveStoryOrder()
    {
        var source = EmbeddedSamples.Catalog.TryGetValue("灯塔办公室 (Lighthouse Office)", out var sample)
            ? sample
            : FallbackLighthouseSample;
        var sequencer = new KtorySequencer(KtoryParser.Parse(source));
        // The sample Reader exposes unknown game conditions for branch preview.
        sequencer.Evaluator = new DefaultExpressionEvaluator { IgnoreUnknownConditions = true };
        sequencer.Start();
        Assert.Contains("窗外风雪交加", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        sequencer.Step();
        sequencer.Step();
        sequencer.Step();
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

        sequencer.SubmitChoice("用钥匙打开抽屉");
        Assert.Contains("抽屉被拉开了", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Contains("机密文件", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Contains("一直在隐瞒的真相", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
        sequencer.SubmitChoice("离开办公室");
        Assert.Contains("我们先走吧", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
    }

    [Fact]
    public void InvestigationSample_DefaultEntryAndBreak_PreserveStoryOrder()
    {
        var source = EmbeddedSamples.Catalog.TryGetValue("分支选择与循环调查 (Choice & Loop Hub)", out var sample)
            ? sample
            : FallbackInvestigationSample;
        var sequencer = new KtorySequencer(KtoryParser.Parse(source));
        sequencer.Start();
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
        sequencer.SubmitChoice("放弃思考");
        Assert.Equal("我们离开这里吧。", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
    }
}
