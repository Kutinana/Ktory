export type LocaleKey = 'zh' | 'en' | 'ja';

export interface SupportedLocale {
  key: LocaleKey;
  code: string;
  label: string;
  shortLabel: string;
  href: string;
}

export const supportedLocales: SupportedLocale[] = [
  { key: 'zh', code: 'zh-CN', label: '简体中文', shortLabel: '中文', href: '/' },
  { key: 'en', code: 'en', label: 'English', shortLabel: 'EN', href: '/en/' },
  { key: 'ja', code: 'ja', label: '日本語', shortLabel: 'JA', href: '/ja/' },
];

export interface TranslationDictionary {
  htmlLang: string;
  head: {
    title: string;
    description: string;
  };
  nav: {
    docs: string;
    docsHref: string;
    reader: string;
    language: string;
  };
  hero: {
    slogan: string;
    description1: string;
    description2: string;
    quickstart: string;
    quickstartHref: string;
    reader: string;
  };
  demo: {
    eyebrow: string;
    title: string;
    description: string;
    fileName: string;
    narratorDefault: string;
    badgeRoot: string;
    badgeBranch: string;
    badgeLoopReturn: string;
    badgeChoiceWait: string;
    badgeCompleted: string;
    endOfFlow: string;
    scriptCompleted: string;
    reachedEnd: string;
    noTags: string;
    speakerPrefix: string;
    attachedDecorators: string;
    choicePrompt: string;
    returnedToLoopPrompt: string;
    choiceDetectedPrompt: string;
    oneTimeTag: string;
    stickyTag: string;
    restartBtn: string;
    restartTitle: string;
    nextBtn: string;
    nextSelectPrompt: string;
  };
  pillars: {
    eyebrow: string;
    title: string;
    p1: { title: string; desc: string };
    p2: { title: string; desc: string };
    p3: { title: string; desc: string };
    p4: { title: string; desc: string };
  };
  footer: {
    copyright: string;
    tagline: string;
    docs: string;
    reader: string;
  };
}

export const translations: Record<LocaleKey, TranslationDictionary> = {
  zh: {
    htmlLang: 'zh-CN',
    head: {
      title: 'Ktory — 对白优先、宿主驱动的按拍叙事语言',
      description: '以结构化拍组织对白与演出意图，在同一文件维护译文，由宿主驱动推进的叙事语言与 C# 运行时。',
    },
    nav: {
      docs: '文档中心',
      docsHref: '/01-overview/01-introduction/',
      reader: '在线试读器',
      language: '选择语言',
    },
    hero: {
      slogan: 'A Dialogue-First, Host-Driven Narrative Language',
      description1: '面向独立作者的对白优先、宿主驱动的按拍叙事语言与运行时。',
      description2: '对白、参数修饰符与同文件多语言共享剧情结构，资源与演出由宿主实现。',
      quickstart: '快速上手',
      quickstartHref: '/01-overview/02-quickstart/',
      reader: '在线试读器',
    },
    demo: {
      eyebrow: 'Screenplay Syntax & Discrete Step',
      title: '剧本语法与离散步进',
      description: '此处使用简化模拟说明剧本排版与逐拍选择，不调用 C# 核心，也不验证完整语义。请通过在线试读器检查真实脚本。',
      fileName: 'sample.ktr',
      narratorDefault: '旁白',
      badgeRoot: 'SuspendedAtBeat',
      badgeBranch: 'BranchSuspension',
      badgeLoopReturn: 'ReturnedToLoop',
      badgeChoiceWait: 'WaitingForChoice',
      badgeCompleted: 'Completed',
      endOfFlow: 'End of Flow',
      scriptCompleted: '“剧本执行完毕。”',
      reachedEnd: '已到达终点',
      noTags: '(无额外修饰符)',
      speakerPrefix: 'Speaker: ',
      attachedDecorators: 'Attached Decorators (Tags):',
      choicePrompt: '请选择推进分支 (Click to Select)：',
      returnedToLoopPrompt: '执行 -> return 弹栈返回循环容器，调度器挂起等待用户重新提交：',
      choiceDetectedPrompt: '检测到分支选项容器，调度器挂起等待用户提交：',
      oneTimeTag: '一次性项 *',
      stickyTag: '持久项 +',
      restartBtn: 'Restart (重置)',
      restartTitle: '重新开始 (Restart from Beat 1)',
      nextBtn: 'Next (推进下一拍)',
      nextSelectPrompt: '请点击上方选项分支',
    },
    pillars: {
      eyebrow: 'Core Pillars',
      title: '四大核心架构支柱',
      p1: {
        title: 'Anchor-Decorator 模型',
        desc: '对白与指令形成逻辑停止点，修饰符携带演出意图和参数。基础剧情结构与具体资源、动画和显示方式分开维护。',
      },
      p2: {
        title: '宿主负责呈现与时间',
        desc: '核心处理剧情拓扑、选择与会话记录；宿主处理游戏状态、资源、渲染、时钟和原始输入，决定何时请求下一拍。',
      },
      p3: {
        title: '共享 C# 核心',
        desc: '核心目标框架为 netstandard2.1，无外部 NuGet 依赖。试读工具使用 net9.0；Unity 发布流程将共享源码打包为 com.ktory.unity。',
      },
      p4: {
        title: '单文件内联多语言',
        desc: '通过 @locale 维护同文件译文。核心按请求语言选择文本，缺译时回退默认语言，并在切换语言时保留剧情位置与会话记录。',
      },
    },
    footer: {
      copyright: 'Ktory Narrative Script System © 2026',
      tagline: 'Designed with Editorial Ink aesthetic for game storytellers.',
      docs: 'Documentation',
      reader: 'Reader',
    },
  },
  en: {
    htmlLang: 'en',
    head: {
      title: 'Ktory — A Dialogue-First, Host-Driven Narrative Language',
      description: 'A dialogue-first narrative language and C# runtime with structured beats, same-file translations and host-driven advancement.',
    },
    nav: {
      docs: 'Documentation',
      docsHref: '/en/01-overview/01-introduction/',
      reader: 'Online Reader',
      language: 'Language',
    },
    hero: {
      slogan: 'A Dialogue-First, Host-Driven Narrative Language',
      description1: 'A dialogue-first, host-driven narrative language and runtime for independent authors.',
      description2: 'Dialogue, parameterized decorators and inline translations share a narrative structure; hosts implement resources and presentation.',
      quickstart: 'Quickstart',
      quickstartHref: '/en/01-overview/02-quickstart/',
      reader: 'Online Reader',
    },
    demo: {
      eyebrow: 'Screenplay Syntax & Discrete Step',
      title: 'Script Syntax & Discrete Stepping',
      description: 'This simplified simulation illustrates script layout and beat-by-beat choices. It does not run the C# core or validate full semantics. Use the online reader to check real scripts.',
      fileName: 'sample.en.ktr',
      narratorDefault: 'Narrator',
      badgeRoot: 'SuspendedAtBeat',
      badgeBranch: 'BranchSuspension',
      badgeLoopReturn: 'ReturnedToLoop',
      badgeChoiceWait: 'WaitingForChoice',
      badgeCompleted: 'Completed',
      endOfFlow: 'End of Flow',
      scriptCompleted: '"Script execution completed."',
      reachedEnd: 'Reached End',
      noTags: '(No additional decorators)',
      speakerPrefix: 'Speaker: ',
      attachedDecorators: 'Attached Decorators (Tags):',
      choicePrompt: 'Select a branch to proceed (Click to Select):',
      returnedToLoopPrompt: 'Popped from stack via -> return; sequencer suspended waiting for selection:',
      choiceDetectedPrompt: 'Choice container encountered. Sequencer suspended waiting for user selection:',
      oneTimeTag: 'Consumable *',
      stickyTag: 'Persistent +',
      restartBtn: 'Restart',
      restartTitle: 'Restart from Beat 1',
      nextBtn: 'Next (Advance Beat)',
      nextSelectPrompt: 'Please click an option above',
    },
    pillars: {
      eyebrow: 'Core Pillars',
      title: 'Four Core Architectural Pillars',
      p1: {
        title: 'Anchor-Decorator Model',
        desc: 'Dialogue and directives form logical stopping points. Decorators carry presentation cues and arguments, separating narrative structure from resources, animation and display.',
      },
      p2: {
        title: 'Host-Owned Rendering and Time',
        desc: 'The core owns narrative flow, choices and session history. The host owns world state, resources, rendering, clocks and raw input, and decides when to request another beat.',
      },
      p3: {
        title: 'Shared C# Core',
        desc: 'The core targets netstandard2.1 without external NuGet dependencies. Reader tools use net9.0; the Unity publishing workflow bundles shared sources as com.ktory.unity.',
      },
      p4: {
        title: 'Single-File Inline Localization',
        desc: 'Keep translations in one file with @locale. The core selects requested text or a default-language fallback, preserving narrative position and session history when switching languages.',
      },
    },
    footer: {
      copyright: 'Ktory Narrative Script System © 2026',
      tagline: 'Designed with Editorial Ink aesthetic for game storytellers.',
      docs: 'Documentation',
      reader: 'Reader',
    },
  },
  ja: {
    htmlLang: 'ja',
    head: {
      title: 'Ktory — 対話を中心に、ホストが拍ごとに進める物語言語',
      description: '構造化した拍でセリフと演出意図をまとめ、同一ファイルで訳文を管理する、ホスト駆動の物語言語と C# ランタイム。',
    },
    nav: {
      docs: 'ドキュメント',
      docsHref: '/ja/01-overview/01-introduction/',
      reader: 'オンライン試読器',
      language: '言語切替',
    },
    hero: {
      slogan: '対話を中心に、ホストが拍ごとに進める物語言語',
      description1: '個人作者のための、対話を中心としたホスト駆動の物語言語とランタイム。',
      description2: 'セリフ、引数付き修飾子、同一ファイルの多言語が物語構造を共有し、リソースと演出はホストが実装します。',
      quickstart: 'クイックスタート',
      quickstartHref: '/ja/01-overview/02-quickstart/',
      reader: 'オンライン試読器',
    },
    demo: {
      eyebrow: 'Screenplay Syntax & Discrete Step',
      title: '脚本構文と離散ステップ',
      description: '脚本の書式と拍ごとの選択を説明する簡易シミュレーションです。C# コアを実行せず、完全な実行意味を検証しません。実際の脚本確認にはオンライン試読器を使ってください。',
      fileName: 'sample.ja.ktr',
      narratorDefault: 'ナレーション',
      badgeRoot: 'SuspendedAtBeat',
      badgeBranch: 'BranchSuspension',
      badgeLoopReturn: 'ReturnedToLoop',
      badgeChoiceWait: 'WaitingForChoice',
      badgeCompleted: 'Completed',
      endOfFlow: 'End of Flow',
      scriptCompleted: '「スクリプトの実行が完了しました。」',
      reachedEnd: '終点に到達',
      noTags: '(追加修飾子なし)',
      speakerPrefix: 'Speaker: ',
      attachedDecorators: 'Attached Decorators (Tags):',
      choicePrompt: '進行する分岐を選択してください (Click to Select)：',
      returnedToLoopPrompt: '-> return によりスタックから復帰。スケジューラは再選択を待機中：',
      choiceDetectedPrompt: '分岐コンテナを検出。スケジューラは選択を待機中：',
      oneTimeTag: '消耗型 *',
      stickyTag: '永続型 +',
      restartBtn: 'Restart (リセット)',
      restartTitle: '最初からやり直す (Beat 1 からリセット)',
      nextBtn: 'Next (次の拍へ)',
      nextSelectPrompt: '上の選択肢をクリックしてください',
    },
    pillars: {
      eyebrow: 'Core Pillars',
      title: '4つのコア設計思想',
      p1: {
        title: 'Anchor-Decorator モデル',
        desc: 'セリフと指令は論理的な停止点を作り、修飾子は演出意図と引数を持ちます。物語構造とリソース、動作、表示方法を分けて管理します。',
      },
      p2: {
        title: '表示と時間はホストが管理',
        desc: 'コアは物語の制御、選択肢、履歴を管理します。ホストはゲーム状態、リソース、描画、時計、入力を管理し、次の拍へ進む時点を決めます。',
      },
      p3: {
        title: '共通の C# コア',
        desc: 'コアは netstandard2.1 を対象とし、外部 NuGet 依存はありません。試読ツールは net9.0 を使い、Unity 公開処理で共有ソースを com.ktory.unity にまとめます。',
      },
      p4: {
        title: '単一ファイル内インライン多言語',
        desc: '@locale で同一ファイルの訳文を管理します。要求言語または既定言語を選び、実行中の切替でも物語の位置とセッション履歴を維持します。',
      },
    },
    footer: {
      copyright: 'Ktory Narrative Script System © 2026',
      tagline: '物語を紡ぐクリエイターのための Editorial Ink デザイン。',
      docs: 'ドキュメント',
      reader: '試読器',
    },
  },
};
