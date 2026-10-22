/**
 * Ktory Archives - Minimalist Ink Pure-Text Reader Client
 * Compliant with Ktory Implementation Specification v1.0
 */

function getPreferredLocale() {
  const supported = ['zh', 'en', 'ja'];
  try {
    const saved = localStorage.getItem('ktory-locale');
    if (saved && supported.includes(saved)) {
      return saved;
    }
  } catch (e) { }

  const langs = (typeof navigator !== 'undefined' && navigator.languages) || [(typeof navigator !== 'undefined' && navigator.language) || ''];
  for (const lang of langs) {
    if (!lang) continue;
    const l = lang.toLowerCase().trim();
    if (l.startsWith('zh')) return 'zh';
    if (l.startsWith('ja')) return 'ja';
    if (l.startsWith('en')) return 'en';
  }
  return 'en'; // fallback to en
}

let state = {
  status: 'Ready',
  payload: null,
  choice: null,
  requestedLocale: (typeof window !== 'undefined' && window.__KTORY_INITIAL_LOCALE__) || getPreferredLocale(),
  secondaryLocale: null,
  defaultLocale: 'en',
  autoPlay: false,

  // Typewriter & Presentation
  isTyping: false,
  canFastForward: true,
  fastForwardLockTimer: null,
  typewriterTimer: null,
  fullTextHtml: '',
  currentActivePassageEl: null,
  currentActiveSpeakerEl: null,
  currentActiveTypingEl: null,
  currentActiveCursorEl: null,
  currentActiveChoiceEl: null,
  activeTags: [],

  // Hold & Countdown (.next / .wait)
  isHolding: false,
  holdTimer: null,
  holdDuration: 0,
  holdElapsed: 0,
  minimumHoldDuration: 0,
  autoAdvanceDuration: 0,
  autoAdvanceElapsed: 0,
  autoAdvanceOnHoldEnd: false,
  usesEstimatedWait: false,
  usesEstimatedAuto: false,
  allowClickInterrupt: true,

  // Narrative Stream Data
  currentSessionId: '',
  currentPresentationId: 0,
  beatsCount: 0,
  visitedItems: [],
  recentTags: [],
  diagnostics: [],
  callStackDepth: 0,
  currentSampleKey: '',
  samples: {}
};
const primaryReaderState = state;

// DOM Elements Cache
let el = {
  body: document.body,
  readerViewport: document.getElementById('readerViewport'),
  readerContainer: document.getElementById('readerContainer'),
  storyStream: document.getElementById('storyStream'),
  storyCompletedBanner: document.getElementById('storyCompletedBanner'),
  btnReplayStory: document.getElementById('btnReplayStory'),
  portraitStage: document.getElementById('portraitStage'),
  portraitSlotLeft: document.getElementById('portraitSlotLeft'),
  portraitSlotRight: document.getElementById('portraitSlotRight'),
  runtimeDiagnostics: document.getElementById('runtimeDiagnostics'),
  runtimeDiagnosticSummary: document.getElementById('runtimeDiagnosticSummary'),
  runtimeDiagnosticMessages: document.getElementById('runtimeDiagnosticMessages'),

  // Top Nav
  langSwitcher: document.getElementById('langSwitcher'),
  secondaryLanguage: document.getElementById('secondaryLanguage'),
  secondaryLanguageLabel: document.getElementById('secondaryLanguageLabel'),
  secondaryLanguageOff: document.getElementById('secondaryLanguageOff'),
  btnAutoPlay: document.getElementById('btnAutoPlay'),
  btnToggleEditor: document.getElementById('btnToggleEditor'),
  btnRestartSession: document.getElementById('btnRestartSession'),
  btnGoHome: document.getElementById('btnGoHome'),
  navHomeText: document.getElementById('navHomeText'),

  // Portal Landing Overlay
  portalOverlay: document.getElementById('portalOverlay'),
  btnPortalSample: document.getElementById('btnPortalSample'),
  btnPortalWrite: document.getElementById('btnPortalWrite'),
  portalTagline: document.getElementById('portalTagline'),
  portalSampleTitle: document.getElementById('portalSampleTitle'),
  portalWriteTitle: document.getElementById('portalWriteTitle'),

  // Desktop Lyrics Dock
  readerLyricsScrim: document.getElementById('readerLyricsScrim'),
  readerLyricsDock: document.getElementById('readerLyricsDock'),

  // Floating Dock
  floatingDock: document.getElementById('floatingDock'),
  dockProgressBar: document.getElementById('dockProgressBar'),
  dockStatusDot: document.getElementById('dockStatusDot'),
  dockStatusText: document.getElementById('dockStatusText'),
  dockStepHint: document.getElementById('dockStepHint'),
  dockLocaleTag: document.getElementById('dockLocaleTag'),

  // Drawers & Backdrop
  editorDrawer: document.getElementById('editorDrawer'),
  drawerBackdrop: document.getElementById('drawerBackdrop'),
  btnCloseEditor: document.getElementById('btnCloseEditor'),
  sampleSelect: document.getElementById('sampleSelect'),
  scriptInput: document.getElementById('scriptInput'),
  btnUploadScript: document.getElementById('btnUploadScript'),
  fileInputKtr: document.getElementById('fileInputKtr'),
  btnRunScript: document.getElementById('btnRunScript'),
  drawerResizer: document.getElementById('drawerResizer')
};
const primaryReaderElements = el;

// Existing rendering and timing policies run against one presentation at a time.
// Capture that context at callback creation, so sibling timers never read each
// other's state or accidentally target the session's legacy "current" output.
let currentPresentationLane = null;

function withPresentationContext(context, action) {
  const previous = { state, elements: el, portraits: portraitState, lane: currentPresentationLane };
  state = context.state;
  el = context.elements;
  portraitState = context.portraits;
  currentPresentationLane = context;
  if (context.isPassive !== undefined) state.isPassive = context.isPassive;
  try { return action(); }
  finally {
    state = previous.state;
    el = previous.elements;
    portraitState = previous.portraits;
    currentPresentationLane = previous.lane;
  }
}

function capturePresentationCallback(action) {
  const context = currentPresentationLane || { state, elements: el, portraits: portraitState };
  const epoch = currentSessionEpoch;
  return (...args) => {
    if (epoch !== currentSessionEpoch) return;
    const result = withPresentationContext(context, () => action(...args));
    if (multiTrackReader.active) multiTrackReader.updateDock();
    return result;
  };
}

const PORTAL_I18N = (typeof window !== 'undefined' && window.__KTORY_I18N__) || {
  'zh': {
    tagline: '轻量 · 优雅 · 对白驱动的叙事创作与接入系统',
    sampleTitle: '尝试示例剧本',
    writeTitle: '撰写我的剧本',
    home: '官网',
    homeTitle: '访问 Ktory 官网',
    langSwitcherTitle: '切换语言',
    autoPlayTitle: '自动阅读模式 (A)',
    restartTitle: '重新从头开始试读 (R)',
    homeUrl: 'https://ktory.ink',
    stepHint: '点击页面或按 [空格] 推进阅读 ▾',
    hintAdvance: '推进',
    hintAuto: '自动阅读',
    hintRestart: '重新开始',
    hintEditor: '编辑器',
    editorTitle: '剧本编辑器 (.ktr)',
    editorToggleTitle: '展开 / 收起编辑器 (E)',
    editorCloseTitle: '关闭 (Esc)',
    editorResizeTitle: '按住拖拽调整宽度',
    catalogLabel: '内置示例剧本',
    sourceLabel: '脚本源码 (.ktr)',
    uploadLabel: '上传剧本',
    uploadTitle: '上传本地 .ktr 文件并试读',
    runLabel: '重新加载并试读',
    samplePlaceholder: '选择示例剧本',
    uploadedTag: '上传'
  },
  'en': {
    tagline: 'Lightweight, elegant, dialogue-driven narrative engine and runtime',
    sampleTitle: 'Try Sample Script',
    writeTitle: 'Write My Script',
    home: 'Home',
    homeTitle: 'Visit Ktory Homepage',
    langSwitcherTitle: 'Switch Language',
    autoPlayTitle: 'Auto-advance mode (A)',
    restartTitle: 'Restart from beginning (R)',
    homeUrl: 'https://ktory.ink/en/',
    stepHint: 'Click screen or press [Space] to advance ▾',
    hintAdvance: 'Advance',
    hintAuto: 'Auto-read',
    hintRestart: 'Restart',
    hintEditor: 'Editor',
    editorTitle: 'Script Editor (.ktr)',
    editorToggleTitle: 'Toggle editor (E)',
    editorCloseTitle: 'Close (Esc)',
    editorResizeTitle: 'Drag to resize',
    catalogLabel: 'Built-in samples',
    sourceLabel: 'Script source (.ktr)',
    uploadLabel: 'Upload script',
    uploadTitle: 'Upload a local .ktr file and preview it',
    runLabel: 'Reload & preview',
    samplePlaceholder: 'Choose a sample',
    uploadedTag: 'Uploaded'
  },
  'ja': {
    tagline: '軽量・優雅・対話主導のシナリオ制作・接続システム',
    sampleTitle: 'サンプルを試読',
    writeTitle: '脚本を作成する',
    home: '公式サイト',
    homeTitle: 'Ktory 公式サイトへ',
    langSwitcherTitle: '言語を切り替える',
    autoPlayTitle: '自動進行モード (A)',
    restartTitle: '最初からやり直す (R)',
    homeUrl: 'https://ktory.ink/ja/',
    stepHint: '画面クリックまたは [Space] で進行 ▾',
    hintAdvance: '進行',
    hintAuto: '自動進行',
    hintRestart: '最初から',
    hintEditor: 'エディタ',
    editorTitle: 'スクリプトエディタ (.ktr)',
    editorToggleTitle: 'エディタを開く / 閉じる (E)',
    editorCloseTitle: '閉じる (Esc)',
    editorResizeTitle: 'ドラッグで幅を変更',
    catalogLabel: '内蔵サンプル',
    sourceLabel: 'スクリプトソース (.ktr)',
    uploadLabel: 'スクリプトを読み込む',
    uploadTitle: 'ローカルの .ktr ファイルを読み込んで試読',
    runLabel: '再読み込みして試読',
    samplePlaceholder: 'サンプルを選択',
    uploadedTag: 'アップロード'
  }
};

function updatePortalLabels(locale) {
  const texts = PORTAL_I18N[locale] || PORTAL_I18N['en'];
  if (el.portalTagline) el.portalTagline.textContent = texts.tagline;
  if (el.portalSampleTitle) el.portalSampleTitle.textContent = texts.sampleTitle;
  if (el.portalWriteTitle) el.portalWriteTitle.textContent = texts.writeTitle;
  if (el.navHomeText) el.navHomeText.textContent = texts.home;
  if (el.btnGoHome) {
    el.btnGoHome.href = texts.homeUrl;
    el.btnGoHome.title = texts.homeTitle;
  }
  if (el.langSwitcher) el.langSwitcher.title = texts.langSwitcherTitle;
  if (el.secondaryLanguageLabel) el.secondaryLanguageLabel.textContent = locale === 'zh' ? '副语言' : locale === 'ja' ? '副言語' : 'Translation';
  if (el.secondaryLanguageOff) el.secondaryLanguageOff.textContent = locale === 'zh' ? '关闭' : locale === 'ja' ? 'オフ' : 'Off';
  if (el.btnAutoPlay) el.btnAutoPlay.title = texts.autoPlayTitle;
  if (el.btnRestartSession) el.btnRestartSession.title = texts.restartTitle;
  if (el.dockLocaleTag) el.dockLocaleTag.textContent = locale.toUpperCase();
  if (el.dockStatusText && state.status === 'Ready') el.dockStatusText.textContent = statusLabel('Ready', locale);
  if (el.dockStepHint && state.status === 'Ready') el.dockStepHint.textContent = texts.stepHint || 'Click screen or press [Space] to advance ▾';
  document.querySelectorAll('[data-i18n]').forEach(node => {
    const value = texts[node.dataset.i18n];
    if (value) node.textContent = value;
  });
  document.querySelectorAll('[data-i18n-title]').forEach(node => {
    const value = texts[node.dataset.i18nTitle];
    if (value) node.title = value;
  });
  if (window.refreshCustomSampleSelect) window.refreshCustomSampleSelect();
}

function samplePlaceholder() {
  return (PORTAL_I18N[state.requestedLocale] || PORTAL_I18N.en).samplePlaceholder;
}

function hidePortal() {
  if (el.portalOverlay) {
    el.portalOverlay.classList.add('portal-hidden');
  }
}

function showPortal() {
  if (window.ktoryReaderHost) return;
  if (el.portalOverlay) {
    el.portalOverlay.classList.remove('portal-hidden');
  }
}

// ==========================================================================
// Initialization
// ==========================================================================
document.addEventListener('DOMContentLoaded', async () => {
  setupEventListeners();
  setupDrawerResizer();
  setupCustomSampleSelect();

  const initialLocale = (typeof window !== 'undefined' && window.__KTORY_INITIAL_LOCALE__) || getPreferredLocale();
  state.requestedLocale = initialLocale;
  document.documentElement.lang = initialLocale === 'zh' ? 'zh-CN' : initialLocale;
  updatePortalLabels(initialLocale);
  if (el.langSwitcher) {
    el.langSwitcher.querySelectorAll('.pill-btn').forEach(btn => {
      btn.classList.toggle('active', btn.dataset.locale === initialLocale);
    });
  }

  if (window.ktoryReaderHost) {
    hidePortal();
    window.ktoryReaderHost.onDomReady();
    return;
  }
  await loadSamples();

  // Populate first catalog sample into scriptInput without auto-starting session
  const sampleKeys = Object.keys(state.samples);
  if (sampleKeys.length > 0) {
    state.currentSampleKey = sampleKeys[0];
    el.scriptInput.value = state.samples[state.currentSampleKey];
    document.dispatchEvent(new Event('ktory:source-changed'));
    syncSampleSelect(state.currentSampleKey);
  }
});

function setupEventListeners() {
  // Resume any pending audio on first user gesture
  ['click', 'keydown', 'pointerdown', 'touchstart'].forEach(evt => {
    window.addEventListener(evt, () => resumePendingAudio(), { passive: true });
  });

  // Viewport / Reading area click
  el.readerViewport.addEventListener('click', (e) => {
    // Prevent advance if clicking on choices, drawers, top nav, or buttons
    if (
      e.target.closest('.choice-group-block') ||
      e.target.closest('.drawer') ||
      e.target.closest('.top-nav') ||
      e.target.closest('.floating-dock') ||
      e.target.closest('.completion-replay-btn')
    ) {
      return;
    }
    handleAdvanceAction();
  });

  // Spacebar and Enter to advance
  window.addEventListener('keydown', (e) => {
    if (e.target.tagName === 'TEXTAREA' || e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') {
      return;
    }
    // Escape must work even while a button (e.g. the drawer handle) holds focus.
    if (e.code === 'Escape') {
      if (!document.querySelector('#sampleSelectWrapper.open')) closeAllDrawers();
      return;
    }
    if (e.target.closest('button, a, [role="button"]')) return;

    if (e.code === 'Space' || e.code === 'Enter') {
      e.preventDefault();
      handleAdvanceAction();
    } else if (e.code === 'KeyF') {
      fastForwardTypewriter();
    } else if (e.code === 'KeyA') {
      toggleAutoPlay();
    } else if (e.code === 'KeyE') {
      toggleEditorDrawer();
    } else if (e.code === 'KeyR') {
      restartSession();
    } else if (multiTrackReader.active) {
      multiTrackReader.chooseNumber(Number(e.key));
    } else if (state.status === 'AwaitingChoice' && state.choice) {
      // Numerical hotkeys for choices: 1, 2, 3...
      const num = parseInt(e.key, 10);
      if (!isNaN(num) && num >= 1 && num <= state.choice.options.length) {
        const option = state.choice.options[num - 1];
        if (option && option.canSelect) {
          submitChoice(option.id, state.currentPresentationId, state.currentSessionId);
        }
      }
    }
  });

  // Portal actions
  if (el.btnPortalSample) {
    el.btnPortalSample.addEventListener('click', async (e) => {
      e.stopPropagation();
      resumePendingAudio();
      hidePortal();
      if (Object.keys(state.samples).length === 0) {
        await loadSamples();
      }
      const sampleKey = state.currentSampleKey || Object.keys(state.samples)[0];
      const script = (sampleKey && state.samples[sampleKey]) || el.scriptInput.value;
      if (script) {
        await startSession(script, state.requestedLocale || 'en');
      }
    });
  }

  if (el.btnPortalWrite) {
    el.btnPortalWrite.addEventListener('click', (e) => {
      e.stopPropagation();
      resumePendingAudio();
      hidePortal();
      toggleEditorDrawer();
      if (el.scriptInput) {
        el.scriptInput.focus();
      }
    });
  }

  const brandEl = document.querySelector('.top-nav .brand');
  if (brandEl) {
    brandEl.addEventListener('click', () => {
      showPortal();
    });
  }

  // Language buttons
  el.secondaryLanguage?.addEventListener('change', e => changeSecondaryLanguage(e.target.value || null));
  el.secondaryLanguage?.addEventListener('click', e => e.stopPropagation());
  el.langSwitcher.querySelectorAll('.pill-btn').forEach(btn => {
    btn.addEventListener('click', async (e) => {
      e.stopPropagation();
      const locale = btn.dataset.locale;
      await changeLanguage(locale);
    });
  });

  // Auto-play toggle
  el.btnAutoPlay.addEventListener('click', (e) => {
    e.stopPropagation();
    toggleAutoPlay();
  });

  // Drawers
  el.btnToggleEditor.addEventListener('click', (e) => {
    e.stopPropagation();
    toggleEditorDrawer();
  });

  el.btnCloseEditor.addEventListener('click', () => closeAllDrawers());
  el.drawerBackdrop.addEventListener('click', () => closeAllDrawers());

  // Restart Button
  el.btnRestartSession.addEventListener('click', (e) => {
    e.stopPropagation();
    restartSession();
  });

  el.btnReplayStory.addEventListener('click', () => {
    restartSession();
  });

  // Drawer sample select
  el.sampleSelect.addEventListener('change', () => {
    const selected = el.sampleSelect.value;
    if (selected && state.samples[selected]) {
      state.currentSampleKey = selected;
      el.scriptInput.value = state.samples[selected];
      document.dispatchEvent(new Event('ktory:source-changed'));
      syncSampleSelect(selected);
    }
  });

  // Upload .ktr script file
  if (el.btnUploadScript && el.fileInputKtr) {
    el.btnUploadScript.addEventListener('click', () => {
      el.fileInputKtr.value = '';
      el.fileInputKtr.click();
    });

    el.fileInputKtr.addEventListener('change', async (e) => {
      const file = e.target.files && e.target.files[0];
      if (!file) return;

      try {
        const content = await file.text();
        el.scriptInput.value = content;
        document.dispatchEvent(new Event('ktory:source-changed'));

        // Extract title from comment or file name
        let title = file.name.replace(/\.(ktr|ktory|txt)$/i, '');
        const titleMatch = content.match(/\/\/\s*title\s*[:：]\s*(.+)/i);
        if (titleMatch && titleMatch[1].trim()) {
          title = titleMatch[1].trim();
        }

        // Add uploaded script to samples dictionary and select it
        const customKey = `[${(PORTAL_I18N[state.requestedLocale] || PORTAL_I18N.en).uploadedTag}] ${title}`;
        state.samples[customKey] = content;
        state.currentSampleKey = customKey;

        if (el.sampleSelect) {
          let opt = Array.from(el.sampleSelect.options).find(o => o.value === customKey);
          if (!opt) {
            opt = document.createElement('option');
            opt.value = customKey;
            opt.textContent = customKey;
            el.sampleSelect.appendChild(opt);
          }
          el.sampleSelect.value = customKey;
          if (window.refreshCustomSampleSelect) window.refreshCustomSampleSelect();
        }

        closeAllDrawers();
        await startSession(content, state.requestedLocale);
      } catch (err) {
        console.error('Failed to read uploaded script file:', err);
        alert('读取剧本文件失败: ' + err.message);
      }
    });
  }

  // Run Script from editor
  el.btnRunScript.addEventListener('click', async () => {
    const script = el.scriptInput.value;
    closeAllDrawers();
    await startSession(script, state.requestedLocale);
  });
}

// ==========================================================================
// Drawer Management
// ==========================================================================
function toggleEditorDrawer() {
  const isOpen = el.editorDrawer.classList.contains('open');
  closeAllDrawers();
  if (!isOpen) {
    el.editorDrawer.classList.add('open');
    el.drawerBackdrop.classList.add('active');
    loadSamples();
  }
}

function closeAllDrawers() {
  el.editorDrawer.classList.remove('open');
  el.drawerBackdrop.classList.remove('active');
}

function setupDrawerResizer() {
  if (!el.drawerResizer || !el.editorDrawer) return;

  // Restore saved width if valid
  try {
    const saved = localStorage.getItem('ktory_editor_drawer_width');
    if (saved) {
      const numW = parseFloat(saved);
      const minW = Math.min(480, window.innerWidth * 0.9);
      const maxW = window.innerWidth * 0.9;
      if (!isNaN(numW) && numW >= minW && numW <= maxW) {
        el.editorDrawer.style.width = `${numW}px`;
      }
    }
  } catch { }

  const onDragStart = (startClientX) => {
    el.editorDrawer.classList.add('no-transition');
    document.body.classList.add('is-resizing-drawer');

    const updateWidth = (clientX) => {
      const minW = Math.min(480, window.innerWidth * 0.9);
      const maxW = window.innerWidth * 0.9;
      let newW = clientX;
      if (newW < minW) newW = minW;
      if (newW > maxW) newW = maxW;
      el.editorDrawer.style.width = `${newW}px`;
    };

    const onMouseMove = (e) => updateWidth(e.clientX);
    const onTouchMove = (e) => {
      if (e.touches && e.touches.length > 0) {
        updateWidth(e.touches[0].clientX);
      }
    };

    const onDragEnd = () => {
      el.editorDrawer.classList.remove('no-transition');
      document.body.classList.remove('is-resizing-drawer');
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onDragEnd);
      window.removeEventListener('touchmove', onTouchMove);
      window.removeEventListener('touchend', onDragEnd);

      try {
        localStorage.setItem('ktory_editor_drawer_width', el.editorDrawer.style.width);
      } catch { }
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onDragEnd);
    window.addEventListener('touchmove', onTouchMove, { passive: true });
    window.addEventListener('touchend', onDragEnd);
  };

  el.drawerResizer.addEventListener('mousedown', (e) => {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    onDragStart(e.clientX);
  });

  el.drawerResizer.addEventListener('touchstart', (e) => {
    if (e.touches && e.touches.length > 0) {
      onDragStart(e.touches[0].clientX);
    }
  }, { passive: true });

  // Support dragging directly via pull handle when drawer is already open
  let handleStartX = 0;
  let isHandleDragging = false;

  el.btnToggleEditor.addEventListener('mousedown', (e) => {
    if (e.button !== 0) return;
    if (!el.editorDrawer.classList.contains('open')) return;
    handleStartX = e.clientX;
    isHandleDragging = false;

    const onHandleMouseMove = (moveEvent) => {
      if (!isHandleDragging && Math.abs(moveEvent.clientX - handleStartX) > 4) {
        isHandleDragging = true;
        el.editorDrawer.classList.add('no-transition');
        document.body.classList.add('is-resizing-drawer');
      }
      if (isHandleDragging) {
        const minW = Math.min(480, window.innerWidth * 0.9);
        const maxW = window.innerWidth * 0.9;
        let newW = moveEvent.clientX;
        if (newW < minW) newW = minW;
        if (newW > maxW) newW = maxW;
        el.editorDrawer.style.width = `${newW}px`;
      }
    };

    const onHandleMouseUp = () => {
      window.removeEventListener('mousemove', onHandleMouseMove);
      window.removeEventListener('mouseup', onHandleMouseUp);

      if (isHandleDragging) {
        el.editorDrawer.classList.remove('no-transition');
        document.body.classList.remove('is-resizing-drawer');
        try {
          localStorage.setItem('ktory_editor_drawer_width', el.editorDrawer.style.width);
        } catch { }
        setTimeout(() => { isHandleDragging = false; }, 60);
      }
    };

    window.addEventListener('mousemove', onHandleMouseMove);
    window.addEventListener('mouseup', onHandleMouseUp);
  });

  // Intercept click on btnToggleEditor if it was a drag
  el.btnToggleEditor.addEventListener('click', (e) => {
    if (isHandleDragging) {
      e.stopImmediatePropagation();
      isHandleDragging = false;
    }
  }, true);

  // Keep drawer width bounded on window resize
  window.addEventListener('resize', () => {
    const maxW = window.innerWidth * 0.9;
    const currentW = parseFloat(el.editorDrawer.style.width);
    if (!isNaN(currentW) && currentW > maxW) {
      el.editorDrawer.style.width = `${maxW}px`;
    }
  });
}

function setupCustomSampleSelect() {
  const wrapper = document.getElementById('sampleSelectWrapper');
  const trigger = document.getElementById('sampleSelectTrigger');
  const triggerText = document.getElementById('sampleSelectTriggerText');
  const list = document.getElementById('sampleSelectOptionsList');
  if (!wrapper || !trigger || !list) return;

  function renderOptions() {
    list.innerHTML = '';
    const options = Array.from(el.sampleSelect.options);
    const selectedVal = el.sampleSelect.value;

    let selectedText = samplePlaceholder();

    options.forEach(opt => {
      if (!opt.value) return;
      const isSelected = opt.value === selectedVal;
      if (isSelected) selectedText = opt.textContent;

      const item = document.createElement('div');
      item.className = `custom-select-option ${isSelected ? 'selected' : ''}`;
      item.setAttribute('role', 'option');
      item.setAttribute('aria-selected', isSelected ? 'true' : 'false');
      item.dataset.value = opt.value;
      item.innerHTML = `
        <span class="option-check">✓</span>
        <span class="option-title">${escapeHtml(opt.textContent)}</span>
      `;

      item.addEventListener('click', (e) => {
        e.stopPropagation();
        el.sampleSelect.value = opt.value;
        el.sampleSelect.dispatchEvent(new Event('change'));
        wrapper.classList.remove('open');
        trigger.setAttribute('aria-expanded', 'false');
        renderOptions();
      });

      list.appendChild(item);
    });

    triggerText.textContent = selectedText;
  }

  trigger.addEventListener('click', (e) => {
    e.stopPropagation();
    const isOpen = wrapper.classList.toggle('open');
    trigger.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
  });

  document.addEventListener('click', (e) => {
    if (!wrapper.contains(e.target)) {
      wrapper.classList.remove('open');
      trigger.setAttribute('aria-expanded', 'false');
    }
  });

  document.addEventListener('keydown', (e) => {
    if (e.code === 'Escape' && wrapper.classList.contains('open')) {
      wrapper.classList.remove('open');
      trigger.setAttribute('aria-expanded', 'false');
    }
  });

  window.refreshCustomSampleSelect = renderOptions;
  renderOptions();
}


function toggleAutoPlay() {
  if (state === primaryReaderState && multiTrackReader.active) {
    multiTrackReader.toggleAutoPlay();
    return;
  }
  state.autoPlay = !state.autoPlay;
  el.btnAutoPlay.classList.toggle('active', state.autoPlay);
  el.btnAutoPlay.setAttribute('aria-pressed', String(state.autoPlay));

  if (state.status === 'SuspendedAtBeat' && state.isHolding) {
    const hasNext = state.activeTags.some(t => t.name.toLowerCase() === 'next');
    const autoPolicy = state.autoPolicy || state.payload?.autoPolicy;
    if (!hasNext && !autoPolicy?.enabled) {
      // Changing host autoplay does not restart or bypass an active minimum wait.
      state.autoAdvanceOnHoldEnd = state.autoPlay;
      updateHoldHint();
    }
  }

  if (state.autoPlay && state.status === 'SuspendedAtBeat' && !state.isTyping && !state.isHolding) {
    stepSession();
  }
}

// ==========================================================================
// Runtime Orchestration: WASM vs Local REST API vs Host Mode
// ==========================================================================
let resolveWasmReady = null;
let rejectWasmReady = null;

if (typeof window !== 'undefined') {
  window.__ktoryWasmPromise = new Promise((resolve, reject) => {
    resolveWasmReady = resolve;
    rejectWasmReady = reject;
  });
  window.__resolveKtoryWasm = (val) => {
    if (resolveWasmReady) resolveWasmReady(val);
  };
  window.__rejectKtoryWasm = (err) => {
    window.__ktoryWasmFailed = true;
    if (rejectWasmReady) rejectWasmReady(err);
  };
}

function isWasmPending() {
  return typeof window !== 'undefined' &&
    !!window.__ktoryWasmPromise &&
    !window.KtoryWasm &&
    !window.__ktoryWasmFailed;
}

async function ensureRuntimeReady() {
  if (!isWasmPending()) return;
  try {
    await Promise.race([
      window.__ktoryWasmPromise,
      new Promise((_, reject) => setTimeout(() => reject(new Error('WASM boot timeout')), 5000))
    ]);
  } catch (_) {
    window.__ktoryWasmFailed = true;
  }
}

// ==========================================================================
// WebAssembly Bridge (For Vercel / GitHub Pages Static Hosting)
// ==========================================================================
window.registerKtoryWasmBridge = function (dotNetRef) {
  window.KtoryWasm = {
    async start(script, requestedLocale, entryBlock, secondaryLocale = null) {
      const json = await dotNetRef.invokeMethodAsync('StartWithLanguages', script, requestedLocale, entryBlock || null, secondaryLocale);
      return JSON.parse(json);
    },
    async step(expectedPresentationId = null, expectedSessionId = null, deltaTime, sourcePositions = {}) {
      const json = await dotNetRef.invokeMethodAsync(deltaTime === undefined ? 'Step' : 'StepWithClock',
        expectedPresentationId, expectedSessionId, ...(deltaTime === undefined ? [] : [deltaTime, sourcePositions]));
      return JSON.parse(json);
    },
    async choice(choiceId, expectedPresentationId = null, expectedSessionId = null, deltaTime, sourcePositions = {}) {
      const json = await dotNetRef.invokeMethodAsync(deltaTime === undefined ? 'Choice' : 'ChoiceWithClock',
        choiceId, expectedPresentationId, expectedSessionId, ...(deltaTime === undefined ? [] : [deltaTime, sourcePositions]));
      return JSON.parse(json);
    },
    async break(expectedPresentationId = null, expectedSessionId = null, deltaTime, sourcePositions = {}) {
      const json = await dotNetRef.invokeMethodAsync(deltaTime === undefined ? 'Break' : 'BreakWithClock',
        expectedPresentationId, expectedSessionId, ...(deltaTime === undefined ? [] : [deltaTime, sourcePositions]));
      return JSON.parse(json);
    },
    async setLanguage(locale, expectedPresentationId = null, expectedSessionId = null) {
      const json = await dotNetRef.invokeMethodAsync('SetLanguage', locale, expectedPresentationId, expectedSessionId);
      return JSON.parse(json);
    },
    async setSecondaryLanguage(locale, expectedPresentationId = null, expectedSessionId = null) {
      const json = await dotNetRef.invokeMethodAsync('SetSecondaryLanguage', locale, expectedPresentationId, expectedSessionId);
      return JSON.parse(json);
    },
    async reportClockPosition(sourceName, position, sessionId, deltaTime, sourcePositions = {}) {
      return JSON.parse(await dotNetRef.invokeMethodAsync(deltaTime === undefined ? 'ReportClockPosition' : 'ReportClockPositionWithClock',
        sourceName, position, sessionId, ...(deltaTime === undefined ? [] : [deltaTime, sourcePositions])));
    },
    async advanceClocks(deltaTime, sessionId, sourcePositions = {}) {
      return JSON.parse(await dotNetRef.invokeMethodAsync('AdvanceClocksWithSources', deltaTime, sessionId, sourcePositions));
    },
    async getSamples() {
      const json = await dotNetRef.invokeMethodAsync('GetSamples');
      return JSON.parse(json);
    }
  };
  console.log('[Ktory] WebAssembly in-browser engine is ready.');

  if (typeof window !== 'undefined' && window.__resolveKtoryWasm) {
    window.__resolveKtoryWasm(window.KtoryWasm);
  }

  if (window.ktoryReaderHost) {
    window.ktoryReaderHost.onRuntimeReady();
    return;
  }

  // If page loaded before WASM booted, populate first catalog sample into scriptInput without auto-starting session
  if (Object.keys(state.samples).length === 0) {
    loadSamples().then(() => {
      const sampleKeys = Object.keys(state.samples);
      if (sampleKeys.length > 0 && !state.payload) {
        state.currentSampleKey = sampleKeys[0];
        el.scriptInput.value = state.samples[state.currentSampleKey];
        document.dispatchEvent(new Event('ktory:source-changed'));
        syncSampleSelect(state.currentSampleKey);
      }
    });
  }
};

// ==========================================================================
// API Interaction (Dual-Mode: Local REST API or In-Browser WASM)
// ==========================================================================
async function loadSamples() {
  try {
    if (isWasmPending()) {
      await ensureRuntimeReady();
    }

    let samples = null;
    if (window.KtoryWasm && window.KtoryWasm.getSamples) {
      samples = await window.KtoryWasm.getSamples();
    } else if (!isWasmPending()) {
      const res = await fetch('/api/samples');
      if (res.ok) {
        samples = await res.json();
      }
    }

    if (samples) {
      state.samples = samples;

      // Populate drawer dropdown
      if (el.sampleSelect) {
        const prevValue = el.sampleSelect.value;
        el.sampleSelect.innerHTML = `<option value="">${escapeHtml(samplePlaceholder())}</option>`;

        for (const name of Object.keys(state.samples)) {
          const opt = document.createElement('option');
          opt.value = name;
          opt.textContent = name;
          el.sampleSelect.appendChild(opt);
        }

        if (state.currentSampleKey && state.samples[state.currentSampleKey]) {
          el.sampleSelect.value = state.currentSampleKey;
        } else if (prevValue && state.samples[prevValue]) {
          el.sampleSelect.value = prevValue;
        }
        if (window.refreshCustomSampleSelect) window.refreshCustomSampleSelect();
      }
    }
  } catch (err) {
    console.error('Failed to load samples:', err);
  }
}

// ==========================================================================
// Serial Session Operation Queue & Epoch Isolation
// Guarantees:
// 1. Advance, choice submission, and language switching are processed serially.
// 2. Restarting / reloading creates a new epoch; responses from old sessions are dropped.
// 3. PresentationId uniquely stamps presentations and prevents stale actions.
// ==========================================================================
let currentSessionEpoch = 0;
let sessionOpChain = Promise.resolve();
let sessionOpsSinceYield = 0;

function enqueueSessionOp(opFn) {
  const opEpoch = currentSessionEpoch;
  sessionOpChain = sessionOpChain.then(async () => {
    // If epoch changed before this op began, discard it immediately
    if (opEpoch !== currentSessionEpoch) {
      return;
    }
    // WASM calls may settle in one microtask chain. Yield periodically so a
    // zero-duration automatic lane cannot monopolize rendering or sibling input.
    if (++sessionOpsSinceYield > 32) {
      sessionOpsSinceYield = 0;
      await new Promise(resolve => setTimeout(resolve, 0));
      if (opEpoch !== currentSessionEpoch) return;
    }
    try {
      await opFn(opEpoch);
    } catch (err) {
      console.error('Session operation failed:', err);
    }
  });
  return sessionOpChain;
}

async function startSession(script, requestedLocale = state.requestedLocale || 'en', entryBlock = null) {
  hidePortal();
  clearTimers();
  resetStoryStream();
  state.status = 'Loading';
  state.payload = null;
  state.choice = null;
  state.currentSessionId = '';
  state.currentPresentationId = 0;
  state.diagnostics = [];
  renderDiagnostics();
  state.currentActiveChoiceEl = null;
  state.requestedLocale = requestedLocale;
  const secondaryLocale = state.secondaryLocale;
  currentSessionEpoch++;
  const sessionEpoch = currentSessionEpoch;

  // If entryBlock is not provided or empty, normalize to null (defaults to root block)
  if (!entryBlock) {
    entryBlock = null;
  }

  return enqueueSessionOp(async (opEpoch) => {
    if (sessionEpoch !== currentSessionEpoch) return;

    try {
      if (isWasmPending()) {
        await ensureRuntimeReady();
      }

      let data = null;
      if (window.KtoryWasm && window.KtoryWasm.start) {
        data = await window.KtoryWasm.start(script, requestedLocale, entryBlock, secondaryLocale);
      } else {
        const res = await fetch('/api/session/start', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ script, requestedLocale, entryBlock, secondaryLocale })
        });
        if (sessionEpoch !== currentSessionEpoch) return;
        if (res.ok) {
          data = await res.json();
        } else {
          const err = await res.json();
          reportSessionError(`解析错误: ${err.error}`);
          return;
        }
      }
      if (sessionEpoch !== currentSessionEpoch) return;
      if (data) {
        updateState(data);
      }
    } catch (err) {
      if (sessionEpoch !== currentSessionEpoch) return;
      console.error('Failed to start session:', err);
      reportSessionError(`解析错误: ${err.message || err}`);
    }
  });
}

async function stepSession(expectedPresentationId = null) {
  if (multiTrackReader.active) {
    return multiTrackReader.command('step', {
      sessionId: state.currentSessionId, presentationId: expectedPresentationId || state.currentPresentationId
    });
  }
  if (expectedPresentationId && expectedPresentationId !== state.currentPresentationId) {
    return;
  }
  if (state.status !== 'SuspendedAtBeat') return;

  const targetPresentationId = expectedPresentationId || state.currentPresentationId;
  const targetSessionId = state.currentSessionId;

  return enqueueSessionOp(async (opEpoch) => {
    if (opEpoch !== currentSessionEpoch) return;
    if (state.status !== 'SuspendedAtBeat') return;
    if (targetPresentationId && targetPresentationId !== state.currentPresentationId) return;

    clearTimers();

    try {
      if (isWasmPending()) {
        await ensureRuntimeReady();
      }

      let data = null;
      const clockInput = readerClocks.captureInput(targetSessionId);
      if (window.KtoryWasm && window.KtoryWasm.step) {
        data = await window.KtoryWasm.step(targetPresentationId, targetSessionId, clockInput.deltaTime, clockInput.sourcePositions);
      } else {
        const res = await fetch('/api/session/step', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ presentationId: targetPresentationId, sessionId: targetSessionId, ...clockInput })
        });
        if (opEpoch !== currentSessionEpoch) return;
        if (res.ok) {
          data = await res.json();
        }
      }
      if (opEpoch !== currentSessionEpoch) return;
      if (data) {
        updateState(data);
      }
    } catch (err) {
      if (opEpoch !== currentSessionEpoch) return;
      console.error('Failed to step session:', err);
      reportSessionError(err.message || String(err));
    }
  });
}

async function submitChoice(choiceId, expectedPresentationId = null, expectedSessionId = null) {
  if (multiTrackReader.active) {
    return multiTrackReader.command('choice', {
      sessionId: expectedSessionId || state.currentSessionId,
      presentationId: expectedPresentationId || state.currentPresentationId
    }, choiceId);
  }
  if (expectedSessionId !== null && expectedSessionId !== state.currentSessionId) {
    console.debug('[Ktory] Ignored choice from a previous session.');
    return;
  }
  if (expectedPresentationId && expectedPresentationId !== state.currentPresentationId) {
    return;
  }
  if (state.status !== 'AwaitingChoice') return;

  const targetPresentationId = expectedPresentationId || state.currentPresentationId;
  const targetSessionId = expectedSessionId ?? state.currentSessionId;

  clearTimers();
  if (state.currentActiveChoiceEl) {
    state.currentActiveChoiceEl.classList.add('has-selection');
    state.currentActiveChoiceEl = null;
  }

  return enqueueSessionOp(async (opEpoch) => {
    if (opEpoch !== currentSessionEpoch) return;
    if (targetPresentationId && targetPresentationId !== state.currentPresentationId) return;

    try {
      if (isWasmPending()) {
        await ensureRuntimeReady();
      }

      let data = null;
      const clockInput = readerClocks.captureInput(targetSessionId);
      if (window.KtoryWasm && window.KtoryWasm.choice) {
        data = await window.KtoryWasm.choice(choiceId, targetPresentationId, targetSessionId, clockInput.deltaTime, clockInput.sourcePositions);
      } else {
        const res = await fetch('/api/session/choice', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ choiceId, presentationId: targetPresentationId, sessionId: targetSessionId, ...clockInput })
        });
        if (opEpoch !== currentSessionEpoch) return;
        if (res.ok) {
          data = await res.json();
        }
      }
      if (opEpoch !== currentSessionEpoch) return;
      if (data) {
        updateState(data);
      }
    } catch (err) {
      if (opEpoch !== currentSessionEpoch) return;
      console.error('Failed to submit choice:', err);
      reportSessionError(err.message || String(err));
    }
  });
}

async function changeLanguage(locale) {
  state.requestedLocale = locale;
  try {
    localStorage.setItem('ktory-locale', locale);
  } catch (e) { }
  document.documentElement.lang = locale === 'zh' ? 'zh-CN' : locale;
  updatePortalLabels(locale);
  el.langSwitcher.querySelectorAll('.pill-btn').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.locale === locale);
  });
  if (state.status === 'Error' || state.status === 'Loading' || state.status === 'Ready') return;

  const targetSessionId = state.currentSessionId;

  return enqueueSessionOp(async (opEpoch) => {
    if (opEpoch !== currentSessionEpoch) return;

    try {
      if (isWasmPending()) {
        await ensureRuntimeReady();
      }

      let data = null;
      if (window.KtoryWasm && window.KtoryWasm.setLanguage) {
        data = await window.KtoryWasm.setLanguage(locale, state.currentPresentationId, targetSessionId);
      } else {
        const res = await fetch('/api/session/language', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ locale, presentationId: state.currentPresentationId, sessionId: targetSessionId })
        });
        if (opEpoch !== currentSessionEpoch) return;
        if (res.ok) {
          data = await res.json();
        }
      }
      if (opEpoch !== currentSessionEpoch) return;
      if (data) {
        updateState(data, true);
      }
    } catch (err) {
      if (opEpoch !== currentSessionEpoch) return;
      console.error('Failed to change language:', err);
      reportSessionError(err.message || String(err));
    }
  });
}

async function changeSecondaryLanguage(locale) {
  state.secondaryLocale = locale || null;
  if (el.secondaryLanguage) el.secondaryLanguage.value = state.secondaryLocale || '';
  if (state.status === 'Error' || state.status === 'Ready') return;

  return enqueueSessionOp(async (opEpoch) => {
    if (opEpoch !== currentSessionEpoch || !state.currentSessionId) return;
    // Language settings apply to the active presentation after earlier queued inputs.
    const token = { presentationId: state.currentPresentationId, sessionId: state.currentSessionId };
    try {
      if (isWasmPending()) await ensureRuntimeReady();
      let data;
      if (window.KtoryWasm?.setSecondaryLanguage) {
        data = await window.KtoryWasm.setSecondaryLanguage(locale || null, token.presentationId, token.sessionId);
      } else {
        const res = await fetch('/api/session/secondary-language', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ locale: locale || null, ...token })
        });
        if (opEpoch !== currentSessionEpoch) return;
        if (!res.ok) throw new Error((await res.json()).error || 'Language refresh failed');
        data = await res.json();
      }
      if (opEpoch !== currentSessionEpoch) return;
      if (data) updateState(data, 'secondary');
    } catch (err) {
      if (opEpoch !== currentSessionEpoch) return;
      console.error('Failed to change secondary language:', err);
      reportSessionError(err.message || String(err));
    }
  });
}

async function restartSession() {
  const script = (el.scriptInput && el.scriptInput.value) || (state.samples[state.currentSampleKey] || '');
  if (script) {
    await startSession(script, state.requestedLocale);
  }
}

// ==========================================================================
// Narrative Stream & State Management
// ==========================================================================
function resetStoryStream() {
  multiTrackReader.reset();
  readerClocks.reset();
  clearAllPortraits();
  stopBgm(0);
  el.storyStream.innerHTML = '';
  el.storyCompletedBanner.style.display = 'none';
  state.beatsCount = 0;
  state.currentActivePassageEl = null;
  state.currentActiveSpeakerEl = null;
  state.currentActiveTypingEl = null;
  state.currentActiveCursorEl = null;
}

function syncSampleSelect(sampleKey) {
  if (el.sampleSelect && sampleKey) el.sampleSelect.value = sampleKey;
  if (window.refreshCustomSampleSelect) window.refreshCustomSampleSelect();
}

function renderTextValue(value, fallback = '', plain = false) {
  if (!Array.isArray(value?.segments)) return plain ? escapeHtml(value?.content ?? fallback) : value?.content ?? fallback;
  return value.segments.map(segment => segment.isLiteral
    ? `<span class="ktory-literal">${escapeHtml(segment.content)}</span>`
    : (plain ? escapeHtml(segment.content) : segment.content)).join('');
}

function prepareReaderPayload(payload) {
  if (!payload) return payload;
  if (payload.text?.primary) {
    payload = {
      ...payload, content: renderTextValue(payload.text.primary), actualLanguage: payload.text.primary.actualLanguage,
      readingContent: payload.text.primary.content,
      speaker: payload.speakerName?.primary?.content ?? payload.speaker,
      speakerActualLanguage: payload.speakerName?.primary?.actualLanguage ?? payload.speakerActualLanguage
    };
  }
  if (window.ktoryReaderHost?.sanitizeHtml && payload.stepType !== 1 && payload.stepType !== 'Directive') {
    payload = { ...payload, content: window.ktoryReaderHost.sanitizeHtml(payload.content || '') };
  }
  return payload;
}

function updateState(serverState, isLanguageSwitch = false) {
  window.ktoryReaderHost?.onState?.(serverState);
  const samePresentation = state.currentSessionId === serverState.sessionId &&
    state.currentPresentationId === serverState.presentationId && state.status === serverState.status;
  serverState = { ...serverState, payload: prepareReaderPayload(serverState.payload) };
  state.status = serverState.status;
  state.payload = serverState.payload;
  state.choice = serverState.choice;
  state.currentSessionId = serverState.sessionId || '';
  state.currentPresentationId = serverState.presentationId ??
    serverState.payload?.presentationId ??
    serverState.choice?.presentationId ??
    0;
  state.requestedLocale = serverState.requestedLanguage;
  state.secondaryLocale = serverState.secondaryLanguage || null;
  if (el.secondaryLanguage) el.secondaryLanguage.value = state.secondaryLocale || '';
  state.defaultLocale = serverState.defaultLanguage;
  state.visitedItems = serverState.visitedItems || [];
  state.callStackDepth = serverState.callStackDepth || 0;
  state.recentTags = serverState.recentTags || [];
  state.diagnostics = serverState.diagnostics || [];
  renderDiagnostics();
  state.autoPolicy = serverState.autoPolicy || serverState.payload?.autoPolicy || null;
  readerClocks.sync(serverState);
  if (multiTrackReader.sync(serverState, isLanguageSwitch)) return;

  if (el.dockLocaleTag) {
    const act = serverState.payload?.actualLanguage;
    el.dockLocaleTag.textContent = act ? act.toUpperCase() : 'RAW';
  }

  updateDockStatus(state.status);
  if (isLanguageSwitch === 'clock' && samePresentation) return;
  if (!samePresentation) isLanguageSwitch = false;

  // Supplementary text has no presentation lifecycle. In particular, preserve the
  // running primary typewriter, its timers, and the existing choice button listeners.
  if (isLanguageSwitch === 'secondary') {
    if (state.status === 'AwaitingChoice') refreshSecondaryChoices(state.choice);
    else if (state.status === 'SuspendedAtBeat') renderSecondaryText(state.payload);
    return;
  }

  // If Completed
  if (state.status === 'Completed') {
    el.storyCompletedBanner.style.display = 'block';
    el.dockStepHint.textContent = state.requestedLocale === 'en'
      ? 'Story sequence concluded'
      : state.requestedLocale === 'ja'
        ? 'シナリオの再生が終了しました'
        : '剧本演练已全部结束';
    scrollToBottom();
    return;
  }

  // If Choice Active
  if (state.status === 'AwaitingChoice' && state.choice) {
    renderChoices(state.choice);
    return;
  }

  // If Beat Active
  if (state.status === 'SuspendedAtBeat' && state.payload) {
    renderBeat(state.payload, isLanguageSwitch);
    return;
  }

  // If Clock Waiting
  if (state.status === 'WaitingForClock') {
    const gate = serverState.pendingClockGates?.[0];
    state.requiresInput = false;
    state.clockGate = gate || null;
    if (el.dockStepHint && gate) {
      el.dockStepHint.textContent = readerClocks.waitHint(gate, state.requestedLocale);
    }
  }
}

function renderDiagnostics() {
  if (!el.runtimeDiagnostics) return;
  // InputIgnored (512) is a background diagnostic, never a player-facing warning.
  const warnings = state.diagnostics.filter(item => item.kind === 'Warning' || item.kind === 256);
  el.runtimeDiagnostics.hidden = warnings.length === 0;
  el.runtimeDiagnosticSummary.textContent = state.requestedLocale === 'en'
    ? `Script Warnings (${warnings.length})`
    : state.requestedLocale === 'ja'
      ? `スクリプト警告（${warnings.length}）`
      : `剧本警告（${warnings.length}）`;
  el.runtimeDiagnosticMessages.textContent = warnings.map(item =>
    `${item.block || 'Root'} · L${item.lineNumber}: ${item.message}`).join('\n');
}

// ==========================================================================
// Portrait Stage & Character Presentation Manager
// ==========================================================================
let portraitState = {
  left: { id: null, src: null, timer: null },
  right: { id: null, src: null, timer: null }
};

const PORTRAIT_REGISTRY = {
  'kutori': 'portraits/kutori.webp',
  'william': 'portraits/william.webp',
  'kutori_normal': 'portraits/kutori.webp',
  'william_normal': 'portraits/william.webp',
  'kutori.webp': 'portraits/kutori.webp',
  'william.webp': 'portraits/william.webp'
};

function resolvePortraitUrl(id) {
  if (!id) return '';
  const cleanId = String(id).trim().toLowerCase();
  return PORTRAIT_REGISTRY[cleanId] || (cleanId.endsWith('.webp') || cleanId.endsWith('.png') ? cleanId : `portraits/${cleanId}.webp`);
}

function extractPortraitsFromTags(tags, speakerName) {
  if (!Array.isArray(tags)) return { left: null, right: null };

  let leftTarget = null;
  let rightTarget = null;

  for (const tag of tags) {
    if (!tag || !tag.name) continue;
    const tagName = tag.name.toLowerCase();

    // Match .portrait(...), .char(...), .portrait_left(...), .portrait_right(...)
    if (tagName !== 'portrait' && tagName !== 'char' &&
      tagName !== 'portrait_left' && tagName !== 'portrait_right' &&
      tagName !== 'portraitleft' && tagName !== 'portraitright') {
      continue;
    }

    const namedArgs = tag.namedArgs || {};
    const posArgs = Array.isArray(tag.positionalArgs) ? tag.positionalArgs : [];

    let slot = null;
    let charId = null;

    if (tagName.includes('left')) slot = 'left';
    if (tagName.includes('right')) slot = 'right';

    // Check named arguments: side, slot, pos, id, char, name
    if (namedArgs.side) slot = String(namedArgs.side).toLowerCase();
    else if (namedArgs.slot) slot = String(namedArgs.slot).toLowerCase();
    else if (namedArgs.pos) slot = String(namedArgs.pos).toLowerCase();

    if (namedArgs.id) charId = String(namedArgs.id);
    else if (namedArgs.char) charId = String(namedArgs.char);
    else if (namedArgs.name) charId = String(namedArgs.name);

    // Check positional arguments
    if (!charId && posArgs.length > 0) {
      const p0 = String(posArgs[0]);
      if (posArgs.length === 1) {
        charId = p0;
      } else {
        const p1 = String(posArgs[1]).toLowerCase();
        if (p1 === 'left' || p1 === 'right') {
          charId = p0;
          slot = p1;
        } else if (p0.toLowerCase() === 'left' || p0.toLowerCase() === 'right') {
          slot = p0.toLowerCase();
          charId = p1;
        } else {
          charId = p0;
        }
      }
    }

    if (!charId) continue;
    const lowerId = charId.toLowerCase();

    // Check if slot was not explicitly declared: default kutori->left, william->right
    if (!slot) {
      if (lowerId.includes('kutori')) {
        slot = 'left';
      } else if (lowerId.includes('william')) {
        slot = 'right';
      } else if (speakerName && String(speakerName).toLowerCase().includes('william')) {
        slot = 'right';
      } else {
        slot = 'left';
      }
    }

    // Hide / clear command
    if (lowerId === 'none' || lowerId === 'hide' || lowerId === 'clear' || lowerId === 'off') {
      if (slot === 'left') leftTarget = null;
      if (slot === 'right') rightTarget = null;
      continue;
    }

    const resolvedSrc = resolvePortraitUrl(charId);
    if (slot === 'left') {
      leftTarget = { id: charId, src: resolvedSrc };
    } else if (slot === 'right') {
      rightTarget = { id: charId, src: resolvedSrc };
    }
  }

  return { left: leftTarget, right: rightTarget };
}

function updatePortraits(tags, payload) {
  // Portraits are dedicated to Web Reader; VS Code extension does not display portraits.
  if (window.ktoryReaderHost) return;
  const speaker = payload ? payload.speaker : null;
  const targets = extractPortraitsFromTags(tags, speaker);

  updateSlot('left', el.portraitSlotLeft, targets.left);
  updateSlot('right', el.portraitSlotRight, targets.right);
}

function updateSlot(side, slotEl, target) {
  if (!slotEl) return;
  const current = portraitState[side];

  if (target) {
    // If already showing this portrait in this slot, keep active and do nothing
    if (current.id === target.id && current.src === target.src && slotEl.classList.contains('active')) {
      return;
    }

    clearTimeout(current.timer);
    current.id = target.id;
    current.src = target.src;

    slotEl.innerHTML = `<img src="${escapeHtml(target.src)}" alt="${escapeHtml(target.id)}" />`;

    // Trigger enter animation: slide-in from side + fade-in
    slotEl.classList.remove('active', 'exiting');
    slotEl.classList.add('entering');

    // Force layout reflow so animation always triggers cleanly
    if (typeof slotEl.offsetWidth === 'number') {
      void slotEl.offsetWidth;
    }

    slotEl.classList.remove('entering');
    slotEl.classList.add('active');
  } else {
    // Fade out in place
    if (current.id !== null || slotEl.classList.contains('active')) {
      clearTimeout(current.timer);
      current.id = null;
      current.src = null;

      slotEl.classList.remove('active', 'entering');
      slotEl.classList.add('exiting');

      current.timer = setTimeout(() => {
        if (current.id === null) {
          slotEl.classList.remove('exiting');
          slotEl.innerHTML = '';
        }
      }, 400);
    }
  }
}

function clearAllPortraits() {
  if (window.ktoryReaderHost) return;
  updateSlot('left', el.portraitSlotLeft, null);
  updateSlot('right', el.portraitSlotRight, null);
}

// ==========================================================================
// Background Music (BGM) & Audio Support for Web Reader
// ==========================================================================
let currentBgmAudio = null;
let currentBgmTrack = null;
let bgmFadeAnimId = null;
let pendingBgm = null;

const requestAnimFrame = typeof requestAnimationFrame === 'function'
  ? requestAnimationFrame
  : (fn) => setTimeout(() => fn(typeof performance !== 'undefined' ? performance.now() : Date.now()), 16);

const cancelAnimFrame = typeof cancelAnimationFrame === 'function'
  ? cancelAnimationFrame
  : (id) => clearTimeout(id);

function resolveAudioUrl(track) {
  if (!track) return '';
  let clean = String(track).trim().replace(/^["']|["']$/g, '');
  if (!clean) return '';
  if (/^(https?:)?\/\//i.test(clean) || clean.startsWith('/') || clean.startsWith('./')) {
    return clean;
  }
  if (clean.toLowerCase().startsWith('audio/')) {
    return clean;
  }
  const hasExt = /\.(mp3|m4a|ogg|wav|aac|flac)$/i.test(clean);
  const filename = hasExt ? clean : `${clean}.mp3`;
  return `audio/${filename}`;
}

function stopBgmFade() {
  if (bgmFadeAnimId != null) {
    cancelAnimFrame(bgmFadeAnimId);
    bgmFadeAnimId = null;
  }
}

function setBgmVolume(targetVolume, durationSeconds = 0) {
  if (window.ktoryReaderHost) return;
  const target = Math.max(0, Math.min(1, Number(targetVolume) || 0));
  const duration = Math.max(0, Number(durationSeconds) || 0);

  if (pendingBgm) {
    pendingBgm.targetVolume = target;
  }

  if (!currentBgmAudio) return;
  stopBgmFade();

  if (duration <= 0) {
    currentBgmAudio.volume = target;
    return;
  }

  const startVolume = currentBgmAudio.volume;
  const startTime = typeof performance !== 'undefined' ? performance.now() : Date.now();
  const durationMs = duration * 1000;

  function stepFade(now) {
    const elapsed = now - startTime;
    if (elapsed >= durationMs) {
      if (currentBgmAudio) {
        currentBgmAudio.volume = target;
      }
      bgmFadeAnimId = null;
    } else {
      const progress = elapsed / durationMs;
      if (currentBgmAudio) {
        currentBgmAudio.volume = Math.max(0, Math.min(1, startVolume + (target - startVolume) * progress));
      }
      bgmFadeAnimId = requestAnimFrame(stepFade);
    }
  }

  bgmFadeAnimId = requestAnimFrame(stepFade);
}

function stopBgm(durationSeconds = 0) {
  if (window.ktoryReaderHost) return;
  stopBgmFade();
  pendingBgm = null;
  if (!currentBgmAudio) return;

  const audio = currentBgmAudio;
  const duration = Math.max(0, Number(durationSeconds) || 0);

  if (duration <= 0) {
    audio.pause();
    try { audio.currentTime = 0; } catch (_) { }
    if (currentBgmAudio === audio) {
      currentBgmAudio = null;
      currentBgmTrack = null;
    }
  } else {
    const startVolume = audio.volume;
    const startTime = typeof performance !== 'undefined' ? performance.now() : Date.now();
    const durationMs = duration * 1000;

    function stepFade(now) {
      const elapsed = now - startTime;
      if (elapsed >= durationMs) {
        audio.volume = 0;
        audio.pause();
        try { audio.currentTime = 0; } catch (_) { }
        if (currentBgmAudio === audio) {
          currentBgmAudio = null;
          currentBgmTrack = null;
        }
        bgmFadeAnimId = null;
      } else {
        const progress = elapsed / durationMs;
        audio.volume = Math.max(0, startVolume * (1 - progress));
        bgmFadeAnimId = requestAnimFrame(stepFade);
      }
    }
    bgmFadeAnimId = requestAnimFrame(stepFade);
  }
}

function playBgm(track, initialVolume = 1.0) {
  if (window.ktoryReaderHost) return;
  if (typeof Audio === 'undefined') return;
  if (!track) return;

  const resolvedUrl = resolveAudioUrl(track);
  if (!resolvedUrl) return;

  const volume = Math.max(0, Math.min(1, initialVolume != null ? Number(initialVolume) : 1.0));

  // Reusing this player preserves the position when resuming a paused track.
  if (currentBgmAudio && currentBgmTrack === resolvedUrl) {
    setBgmVolume(volume, 0);
    if (currentBgmAudio.paused) attemptBgmPlayback(currentBgmAudio, volume);
    return;
  }

  stopBgm(0);

  const audio = new Audio();
  audio.loop = true;
  audio.volume = volume;
  audio.src = resolvedUrl;

  let fallbackAttempted = false;
  audio.onerror = () => {
    // A late error belongs to this player, never the replacement song or session.
    if (currentBgmAudio !== audio || fallbackAttempted) return;
    if (resolvedUrl.startsWith('audio/')) {
      fallbackAttempted = true;
      audio.src = resolvedUrl.slice(6);
      attemptBgmPlayback(audio, volume);
    }
  };

  currentBgmAudio = audio;
  currentBgmTrack = resolvedUrl;

  attemptBgmPlayback(audio, volume);
}

function attemptBgmPlayback(audio, volume) {
  const playPromise = audio.play();
  if (playPromise !== undefined) {
    playPromise.catch(err => {
      if (currentBgmAudio !== audio) return;
      if (err.name === 'NotAllowedError' || err.name === 'AbortError') {
        pendingBgm = { audio, targetVolume: volume };
      } else {
        console.warn('BGM playback failed:', err);
      }
    });
  }
}

function pauseBgm() {
  if (window.ktoryReaderHost) return;
  stopBgmFade();
  pendingBgm = null;
  currentBgmAudio?.pause();
}

function resumePendingAudio() {
  if (pendingBgm && pendingBgm.audio === currentBgmAudio) {
    const { audio, targetVolume } = pendingBgm;
    pendingBgm = null;
    audio.volume = targetVolume;
    attemptBgmPlayback(audio, targetVolume);
  }
}

function parseVolumeArg(raw) {
  if (raw == null) return null;
  if (typeof raw === 'number') return raw;
  const str = String(raw).trim();
  const match = str.match(/(?:volume|vol|target)\s*[:=]\s*([0-9.]+)/i);
  if (match) return Number(match[1]);
  const num = Number(str);
  return isNaN(num) ? null : num;
}

function parseDurationArg(raw) {
  if (raw == null) return null;
  if (typeof raw === 'number') return raw;
  const str = String(raw).trim();
  const match = str.match(/(?:duration|time|fade)\s*[:=]\s*([0-9.]+)/i);
  if (match) return Number(match[1]);
  const num = Number(str);
  return isNaN(num) ? null : num;
}

function processAudioTags(tags) {
  if (window.ktoryReaderHost) return;
  if (!Array.isArray(tags)) return;

  for (const tag of tags) {
    if (!tag || !tag.name) continue;
    const name = tag.name.toLowerCase();
    const namedArgs = tag.namedArgs || {};
    const posArgs = Array.isArray(tag.positionalArgs) ? tag.positionalArgs : [];

    if (name === 'playbgm' || name === 'bgm' || name === 'play_bgm') {
      let track = namedArgs.track || namedArgs.file || namedArgs.src || namedArgs.name || (posArgs.length > 0 ? posArgs[0] : null);
      if (track) {
        let vol = parseVolumeArg(namedArgs.volume ?? namedArgs.vol);
        if (vol == null && posArgs.length > 1) {
          vol = parseVolumeArg(posArgs[1]);
        }
        if (vol == null) vol = 1.0;
        playBgm(track, vol);
      }
    } else if (name === 'setbgmvolume' || name === 'bgmvolume' || name === 'set_bgm_volume' || name === 'fadebgm' || name === 'fade_bgm') {
      let targetVol = parseVolumeArg(namedArgs.volume ?? namedArgs.target ?? namedArgs.vol);
      if (targetVol == null && posArgs.length > 0) {
        targetVol = parseVolumeArg(posArgs[0]);
      }
      if (targetVol == null) targetVol = 1.0;

      let duration = parseDurationArg(namedArgs.duration ?? namedArgs.time ?? namedArgs.fade);
      if (duration == null && posArgs.length > 1) {
        duration = parseDurationArg(posArgs[1]);
      }
      if (duration == null) duration = 0;

      setBgmVolume(targetVol, duration);
    } else if (name === 'pausebgm' || name === 'pause_bgm') {
      pauseBgm();
    } else if (name === 'stopbgm' || name === 'stop_bgm') {
      let duration = parseDurationArg(namedArgs.duration ?? namedArgs.fade);
      if (duration == null && posArgs.length > 0) {
        duration = parseDurationArg(posArgs[0]);
      }
      if (duration == null) duration = 0;

      stopBgm(duration);
    }
  }
}

function retireCurrentSentence() {
  clearTimers();
  clearTimeout(state.displayTimer);
  state.displayTimer = null;
  state.displayLifetimeIdentity = null;
  const passage = state.currentActivePassageEl;
  if (passage) {
    passage.classList.remove('lyrics-active');
    passage.classList.add('sentence-history');
    if (currentPresentationLane?.isPassive || state.isPassive) {
      passage.hidden = true;
      passage.remove();
      if (currentPresentationLane) currentPresentationLane.panel.hidden = true;
    }
  }
  if (state.currentActiveCursorEl) state.currentActiveCursorEl.style.display = 'none';
  state.currentActivePassageEl = null;
  state.currentActiveTypingEl = null;
  state.currentActiveSpeakerEl = null;
  state.currentActiveCursorEl = null;
  state.fullTextHtml = '';
}

function renderBeat(payload, isLanguageSwitch = false) {
  const isDirective = payload.stepType === 1 || payload.stepType === 'Directive';

  if (!isLanguageSwitch) retireCurrentSentence();

  // An empty input anchor is a new sentence without a printing phase.
  if (!isDirective && !payload.content) {
    if (!isLanguageSwitch) {
      state.activeTags = payload.tags || [];
      processAudioTags(state.activeTags);
      setupHoldTimer(state.activeTags);
    }
    return;
  }

  // 1. Directives beat (#do, .bg, .sfx, etc.)
  if (isDirective) {
    if (isLanguageSwitch) {
      // Hot language switch on a directive beat:
      // Directive beats contain no dialogue text to translate.
      // Simply preserve current hold countdown state without touching DOM or previous dialogue elements.
      if (state.isHolding) {
        updateHoldHint();
      }
      return;
    }

    state.beatsCount++;
    updatePortraits(payload.tags || [], payload);
    processAudioTags(payload.tags || []);
    renderDirectiveBeat(payload);
    return;
  }

  // Hot language switch: update current typing/active text and speaker if applicable
  if (isLanguageSwitch && state.currentActiveTypingEl) {
    const wasTyping = state.isTyping;
    const wasHolding = state.isHolding;

    // 1. Immediately abort typewriter timer and fast-forward lock timer
    clearInterval(state.typewriterTimer);
    clearTimeout(state.fastForwardLockTimer);
    state.typewriterTimer = null;
    state.fastForwardLockTimer = null;
    state.isTyping = false;
    state.canFastForward = true;

    // 2. Hide typing cursor
    if (state.currentActiveCursorEl) {
      state.currentActiveCursorEl.style.display = 'none';
    }

    // 3. Dynamically update speaker display name in DOM
    if (state.currentActiveSpeakerEl) {
      state.currentActiveSpeakerEl.textContent = payload.speaker || '';
      state.currentActiveSpeakerEl.title = getSpeakerLanguageTitle(payload.speakerActualLanguage);
    } else if (payload.speaker && state.currentActivePassageEl) {
      const spEl = state.currentActivePassageEl.querySelector('.speaker-name');
      if (spEl) {
        spEl.textContent = payload.speaker;
        spEl.title = getSpeakerLanguageTitle(payload.speakerActualLanguage);
      }
    }

    // 4. Update the text content
    state.fullTextHtml = payload.content;
    state.currentActiveTypingEl.innerHTML = payload.content;
    renderSecondaryText(payload);

    if (payload.tags) {
      state.activeTags = payload.tags;
    }
    const tags = state.activeTags || [];

    // 5. Unified post-display state handling (without re-executing plot/directive tags)
    if (wasTyping) {
      // Switching while typing reveals full translation and triggers post-text policy (.wait / .next / #AUTO / manual)
      setupHoldTimer(tags);
    } else if (wasHolding) {
      refreshHoldLanguage(payload);
    } else {
      // Already finished typing and not holding (manual wait)
      el.dockStepHint.textContent = getAdvanceHint();
    }

    scrollToBottom();
    return;
  }

  state.beatsCount++;

  // 2. Standard Prose / Dialogue Beat
  const passage = document.createElement('div');
  passage.className = 'passage-item';

  const tags = payload.tags || [];
  state.activeTags = tags;

  updatePortraits(tags, payload);
  processAudioTags(tags);

  // Extract emotion tag if present
  let emotionArg = null;
  const emotionTag = tags.find(t => t.name.toLowerCase() === 'emotion');
  if (emotionTag && emotionTag.positionalArgs.length > 0) {
    emotionArg = emotionTag.positionalArgs[0];
  }



  let textContainer = null;
  let cursorEl = null;

  if (payload.speaker) {
    // Character Dialogue
    passage.classList.add('passage-dialogue');
    passage.innerHTML = `
      <div class="speaker-header">
        <span class="speaker-mark">✦</span>
        <span class="speaker-name">${escapeHtml(payload.speaker)}</span>
        ${emotionArg ? `<span class="speaker-emotion-tag">${escapeHtml(emotionArg)}</span>` : ''}
      </div>
      <div class="dialogue-body">
        <div class="dialogue-text"></div>
        <span class="typing-cursor">▎</span>
      </div>
    `;
    textContainer = passage.querySelector('.dialogue-text');
    cursorEl = passage.querySelector('.typing-cursor');
    state.currentActiveSpeakerEl = passage.querySelector('.speaker-name');
    if (state.currentActiveSpeakerEl) {
      state.currentActiveSpeakerEl.title = getSpeakerLanguageTitle(payload.speakerActualLanguage);
    }
  } else {
    // Narrator
    passage.classList.add('passage-narrator');
    passage.innerHTML = `
      <p class="passage-prose"></p>
      <span class="typing-cursor">▎</span>
    `;
    textContainer = passage.querySelector('.passage-prose');
    cursorEl = passage.querySelector('.typing-cursor');
    state.currentActiveSpeakerEl = null;
  }

  const isPassiveLyrics = Boolean(currentPresentationLane?.isPassive || state.isPassive);
  if (isPassiveLyrics) {
    passage.classList.add('lyrics-passage-item', 'lyrics-active');
  }

  state.currentActivePassageEl = passage;
  if (currentPresentationLane) currentPresentationLane.panel.hidden = false;
  el.storyStream.appendChild(passage);
  renderSecondaryText(payload);
  scrollToBottom();

  if (isPassiveLyrics) {
    if (cursorEl) cursorEl.style.display = 'none';
    textContainer.innerHTML = payload.content;
    state.fullTextHtml = payload.content;
    state.isTyping = false;
    state.canFastForward = true;
    state.currentActiveTypingEl = textContainer;
    state.currentActiveCursorEl = cursorEl;
    setupHoldTimer(tags);
  } else {
    startTypewriter(textContainer, cursorEl, payload.content, tags);
  }
}

function renderSecondaryText(payload) {
  const passage = state.currentActivePassageEl;
  // Empty anchors have no current sentence to translate.
  if (!passage || !payload?.content || payload.stepType === 1 || payload.stepType === 'Directive') return;
  const text = payload.text?.secondary;
  const speaker = payload.speakerName?.secondary;
  let block = passage.querySelector('.secondary-translation');
  if (!block && !text && !speaker) return;
  if (!block) {
    block = document.createElement('div');
    block.className = 'secondary-translation';
    block.innerHTML = '<div class="secondary-speaker"></div><div class="secondary-content"></div>';
    passage.appendChild(block);
  }
  block.hidden = !text && !speaker;
  const speakerEl = block.querySelector('.secondary-speaker');
  speakerEl.hidden = !speaker;
  speakerEl.textContent = speaker?.content || '';
  speakerEl.lang = speaker?.actualLanguage || '';
  const textEl = block.querySelector('.secondary-content');
  textEl.hidden = !text;
  textEl.lang = text?.actualLanguage || '';
  const html = renderTextValue(text);
  textEl.innerHTML = window.ktoryReaderHost?.sanitizeHtml ? window.ktoryReaderHost.sanitizeHtml(html) : html;
}

function refreshSecondaryChoices(choicePayload) {
  const group = state.currentActiveChoiceEl;
  if (!group || !choicePayload) return;
  const options = new Map((choicePayload.options || []).map(option => [option.id, option]));
  group.querySelectorAll('.choice-card-btn').forEach(button => {
    const secondary = options.get(button.dataset.choiceId)?.text?.secondary;
    const label = button.querySelector('.choice-secondary');
    if (!label) return;
    if (secondary?.segments) label.innerHTML = renderTextValue(secondary, '', true);
    else label.textContent = secondary?.content || '';
    label.lang = secondary?.actualLanguage || '';
    label.hidden = !secondary;
  });
}

function renderDirectiveBeat(payload) {
  // Clear any active typing / prose elements from previous dialogue beats
  state.currentActiveTypingEl = null;
  state.currentActiveSpeakerEl = null;
  state.currentActiveCursorEl = null;
  state.currentActivePassageEl = null;
  state.fullTextHtml = '';

  const passage = document.createElement('div');
  passage.className = 'passage-item passage-directive';

  const tagsDesc = (payload.tags || [])
    .map(t => {
      const parts = (t.positionalArgs || []).map(a => JSON.stringify(a));
      if (t.namedArgs) {
        for (const [k, v] of Object.entries(t.namedArgs)) {
          parts.push(`${k}: ${JSON.stringify(v)}`);
        }
      }
      return `.${t.name}(${parts.join(', ')})`;
    })
    .join(' ');

  passage.innerHTML = `
    <span class="directive-badge">⚡ #${escapeHtml(payload.content || 'pause')}</span>
    <span class="directive-args">${escapeHtml(tagsDesc)}</span>
  `;

  el.storyStream.appendChild(passage);
  scrollToBottom();

  setupHoldTimer(payload.tags || [], 0.4);
}

// ==========================================================================
// Typewriter & Fast-Forward Mechanics
// ==========================================================================
function startTypewriter(targetEl, cursorEl, htmlContent, tags) {
  clearTimers();
  state.isTyping = true;
  state.currentActiveTypingEl = targetEl;
  state.currentActiveCursorEl = cursorEl;
  state.fullTextHtml = htmlContent;

  const typewriterPresentationId = state.currentPresentationId;
  const typewriterEpoch = currentSessionEpoch;

  cursorEl.style.display = 'inline-block';
  el.dockStepHint.textContent = state.requestedLocale === 'en'
    ? 'Typing... Click to fast-forward'
    : state.requestedLocale === 'ja'
      ? 'テキスト表示中... クリックで早送り'
      : '文字呈现中... 点击可快速显示全文';

  // Parse .skippable(false, [duration])
  state.canFastForward = true;
  const skippableTag = tags.find(t => t.name.toLowerCase() === 'skippable');
  if (skippableTag) {
    const rawAllowed = skippableTag.namedArgs?.enabled ?? true;
    const allowed = rawAllowed !== false && rawAllowed !== 'false';
    if (!allowed) {
      state.canFastForward = false;

      // Check if explicit duration parameter t is provided
      const hasDuration = skippableTag.namedArgs?.seconds !== undefined;
      if (hasDuration) {
        const duration = Number(skippableTag.namedArgs.seconds);
        if (duration > 0) {
          el.dockStepHint.textContent = state.requestedLocale === 'en'
            ? `Fast-forward locked (${duration}s)...`
            : state.requestedLocale === 'ja'
              ? `早送り待機中 (${duration}s)...`
              : `快显锁定中 (${duration}s)...`;
          state.fastForwardLockTimer = setTimeout(capturePresentationCallback(() => {
            if (typewriterPresentationId !== state.currentPresentationId || typewriterEpoch !== currentSessionEpoch) {
              return;
            }
            state.canFastForward = true;
            el.dockStepHint.textContent = state.requestedLocale === 'en'
              ? 'Click to fast-forward'
              : state.requestedLocale === 'ja'
                ? 'クリックで早送り'
                : '点击可快速显示全文';
          }), duration * 1000);
        } else {
          state.canFastForward = true;
        }
      } else {
        // When t is omitted, fast-forward is prohibited for the entire printing duration per Spec §2.6
        el.dockStepHint.textContent = state.requestedLocale === 'en'
          ? 'Presenting text (fast-forward disabled)...'
          : state.requestedLocale === 'ja'
            ? 'テキスト表示中 (早送り不可)...'
            : '文字呈现中 (禁止快显)...';
      }
    }
  }

  const tokens = tokenizeHtml(htmlContent);
  let tokenIdx = 0;
  let visibleHtml = '';
  targetEl.innerHTML = '';

  const speedMs = 18; // literary typewriter speed
  state.typewriterTimer = setInterval(capturePresentationCallback(() => {
    if (typewriterPresentationId !== state.currentPresentationId || typewriterEpoch !== currentSessionEpoch) {
      clearInterval(state.typewriterTimer);
      state.typewriterTimer = null;
      return;
    }
    if (tokenIdx >= tokens.length) {
      finishTypewriter(cursorEl, tags, typewriterPresentationId, typewriterEpoch);
      return;
    }

    visibleHtml += tokens[tokenIdx++];
    targetEl.innerHTML = visibleHtml;
    scrollToBottom();
  }), speedMs);
}

function fastForwardTypewriter() {
  if (state === primaryReaderState && multiTrackReader.active) {
    multiTrackReader.routeInput(fastForwardTypewriter);
    return;
  }
  if (!state.isTyping || !state.canFastForward) return;
  const currentPresId = state.currentPresentationId;
  const currentEpoch = currentSessionEpoch;
  clearInterval(state.typewriterTimer);
  if (state.fastForwardLockTimer) {
    clearTimeout(state.fastForwardLockTimer);
    state.fastForwardLockTimer = null;
  }

  if (state.currentActiveTypingEl) {
    state.currentActiveTypingEl.innerHTML = state.fullTextHtml;
  }
  finishTypewriter(state.currentActiveCursorEl, state.activeTags, currentPresId, currentEpoch);
  scrollToBottom();
}

function finishTypewriter(cursorEl, tags, expectedPresentationId = null, expectedEpoch = null) {
  if (expectedEpoch !== null && expectedEpoch !== currentSessionEpoch) return;
  if (expectedPresentationId !== null && expectedPresentationId !== state.currentPresentationId) return;

  clearInterval(state.typewriterTimer);
  state.typewriterTimer = null;
  state.isTyping = false;
  if (state.fastForwardLockTimer) {
    clearTimeout(state.fastForwardLockTimer);
    state.fastForwardLockTimer = null;
  }
  state.canFastForward = true;
  if (cursorEl) cursorEl.style.display = 'none';

  setupHoldTimer(tags);
}

function tokenizeHtml(html) {
  const tokens = [];
  let i = 0;
  while (i < html.length) {
    if (html[i] === '<') {
      const closeIdx = html.indexOf('>', i);
      if (closeIdx !== -1) {
        tokens.push(html.substring(i, closeIdx + 1));
        i = closeIdx + 1;
        continue;
      }
    }
    if (html[i] === '&') {
      const entity = html.slice(i).match(/^&(?:#[0-9]+|#x[0-9a-f]+|[a-z][a-z0-9]*);/i);
      if (entity) { tokens.push(entity[0]); i += entity[0].length; continue; }
    }
    const character = String.fromCodePoint(html.codePointAt(i));
    tokens.push(character);
    i += character.length;
  }
  return tokens;
}

function estimateReadingTime(text, lang = null) {
  if (!text) return 1.0;
  const l = (lang || '').toLowerCase();
  if (l.startsWith('zh') || l.startsWith('ja') || (!l && /[\u4e00-\u9fff\u3040-\u30ff]/.test(text))) {
    return Math.max(1.0, text.length / 7.0);
  }
  const words = text.trim().split(/\s+/).length;
  return Math.max(1.0, words / 3.5);
}

// ==========================================================================
// Hold & Auto-Advance Timer (.next / .wait / #AUTO)
// ==========================================================================
function startDisplayLifetime(tags) {
  const duration = tags.find(tag => tag.name.toLowerCase() === 'duration');
  if (!duration) return;
  const owner = state;
  const displayLane = currentPresentationLane;
  const identity = `${owner.currentSessionId}:${owner.currentPresentationId}`;
  if (owner.displayLifetimeIdentity === identity) return;
  if (owner.displayTimer) clearTimeout(owner.displayTimer);
  owner.displayLifetimeIdentity = identity;
  owner.displayTimer = null;
  const passage = owner.currentActivePassageEl;
  if (!duration || !passage) return;
  const epoch = currentSessionEpoch;
  owner.displayTimer = setTimeout(() => {
    if (epoch !== currentSessionEpoch || owner.displayLifetimeIdentity !== identity) return;
    // Only this content instance may hide. It never clears a renderer slot or advances Core.
    passage.hidden = true;
    if (displayLane?.isPassive && owner.currentActivePassageEl === passage) {
      displayLane.panel.hidden = true;
      multiTrackReader.checkPassiveState();
    }
    owner.displayTimer = null;
  }, Math.max(0, Number(duration.namedArgs?.seconds) || 0) * 1000);
}

function setupHoldTimer(tags, defaultHoldSec = 0) {
  if (state.holdTimer) {
    clearInterval(state.holdTimer);
    state.holdTimer = null;
  }

  const holdPresentationId = state.currentPresentationId;
  const holdEpoch = currentSessionEpoch;

  startDisplayLifetime(tags);
  if (state.isAuxiliary || state.executionEnded) {
    state.isHolding = false;
    state.autoAdvanceOnHoldEnd = false;
    state.allowClickInterrupt = false;
    return;
  }

  const nextTag = tags.find(t => t.name.toLowerCase() === 'next');
  const waitTag = tags.find(t => t.name.toLowerCase() === 'wait');
  const autoPolicy = state.autoPolicy || (state.payload && state.payload.autoPolicy);

  const readingTime = estimateReadingTime(state.payload?.readingContent ?? state.payload?.content ?? '', state.payload?.actualLanguage || state.requestedLocale);
  state.usesEstimatedWait = !!waitTag && waitTag.namedArgs?.seconds === undefined;
  state.usesEstimatedAuto = false;
  state.minimumHoldDuration = waitTag
    ? (state.usesEstimatedWait ? readingTime : Math.max(0, Number(waitTag.namedArgs?.seconds) || 0)) : 0;
  const directiveAuto = state.payload?.stepType === 1 || state.payload?.stepType === 'Directive';
  state.autoAdvanceOnHoldEnd = !state.isAuxiliary && (!!nextTag || !!autoPolicy?.enabled || state.autoPlay || directiveAuto);
  if (nextTag) {
    state.autoAdvanceDuration = Math.max(0, Number(nextTag.namedArgs?.seconds) || 0);
  } else if (waitTag) {
    // A local wait replaces the automatic default delay without enabling automatic playback.
    state.autoAdvanceDuration = 0;
  } else if (autoPolicy && autoPolicy.enabled) {
    state.usesEstimatedAuto = autoPolicy.useEstimatedReadingTime &&
      (state.payload?.stepType === 0 || state.payload?.stepType === 'Text');
    state.autoAdvanceDuration = state.usesEstimatedAuto ? readingTime : Math.max(0, Number(autoPolicy.defaultWaitSeconds) || 0);
  } else if (directiveAuto) {
    state.autoAdvanceDuration = 0;
  } else if (state.autoPlay) {
    state.autoAdvanceDuration = 2.2;
  } else {
    state.autoAdvanceDuration = defaultHoldSec;
  }
  state.holdElapsed = 0;
  state.autoAdvanceElapsed = 0;
  state.holdDuration = Math.max(state.minimumHoldDuration, state.autoAdvanceDuration);
  state.allowClickInterrupt = state.minimumHoldDuration <= 0;

  if (state.autoAdvanceOnHoldEnd && state.holdDuration <= 0) {
    // Immediate auto advance per specification
    state.isHolding = false;
    el.dockProgressBar.style.width = '0%';
    stepSession(holdPresentationId);
    return;
  }

  if (state.holdDuration > 0 && (state.autoAdvanceOnHoldEnd || state.minimumHoldDuration > 0)) {
    state.isHolding = true;
    el.dockProgressBar.style.width = '0%';
    updateHoldHint();

    const intervalMs = 25;
    state.holdTimer = setInterval(capturePresentationCallback(() => {
      // Guard against stale presentation or session epoch
      if (holdPresentationId !== state.currentPresentationId || holdEpoch !== currentSessionEpoch) {
        clearInterval(state.holdTimer);
        state.holdTimer = null;
        state.isHolding = false;
        return;
      }

      // Keep .next's clock independent of estimated reading progress during language refresh.
      state.holdElapsed = Math.round((state.holdElapsed + intervalMs / 1000) * 1e9) / 1e9;
      state.autoAdvanceElapsed = Math.round((state.autoAdvanceElapsed + intervalMs / 1000) * 1e9) / 1e9;
      state.allowClickInterrupt = state.holdElapsed >= state.minimumHoldDuration;
      const remaining = Math.max(0, state.minimumHoldDuration - state.holdElapsed,
        state.autoAdvanceOnHoldEnd ? state.autoAdvanceDuration - state.autoAdvanceElapsed : 0);
      const progress = Math.min(100, (1 - remaining / Math.max(0.1, state.holdDuration)) * 100);
      el.dockProgressBar.style.width = `${progress}%`;
      updateHoldHint();
      if (remaining <= 0) {
        clearInterval(state.holdTimer);
        state.holdTimer = null;
        state.isHolding = false;
        el.dockProgressBar.style.width = '0%';
        if (state.autoAdvanceOnHoldEnd) {
          stepSession(holdPresentationId);
        } else {
          el.dockStepHint.textContent = getAdvanceHint();
        }
      }
    }), intervalMs);
  } else {
    // Normal manual step
    state.isHolding = false;
    el.dockProgressBar.style.width = '0%';
    el.dockStepHint.textContent = getAdvanceHint();
  }
}

function getAdvanceHint() {
  return state.requestedLocale === 'en'
    ? 'Click or press [Space] to advance ▾'
    : state.requestedLocale === 'ja'
      ? 'クリックまたは [Space] で進行 ▾'
      : '点击页面或按 [空格] 推进阅读 ▾';
}

function getSpeakerLanguageTitle(actualLang) {
  if (!actualLang) return '';
  return state.requestedLocale === 'en'
    ? `Name Language: ${actualLang}`
    : state.requestedLocale === 'ja'
      ? `名前の言語: ${actualLang}`
      : `名字语言：${actualLang}`;
}

function updateHoldHint() {
  const waitRemaining = Math.max(0, state.minimumHoldDuration - state.holdElapsed);
  const autoRemaining = Math.max(0, state.autoAdvanceDuration - state.autoAdvanceElapsed);
  el.dockStepHint.textContent = !state.allowClickInterrupt
    ? (state.requestedLocale === 'en'
      ? `Waiting (${waitRemaining.toFixed(1)}s)... Click disabled`
      : state.requestedLocale === 'ja'
        ? `待機中 (${waitRemaining.toFixed(1)}s)... クリック不可`
        : `强制停留中 (${waitRemaining.toFixed(1)}s)... 点击无效`)
    : state.autoAdvanceOnHoldEnd
      ? (state.requestedLocale === 'en'
        ? `Auto-advancing (${autoRemaining.toFixed(1)}s)... Click to advance now`
        : state.requestedLocale === 'ja'
          ? `自動進行カウントダウン (${autoRemaining.toFixed(1)}s)... クリックで直ちに進行`
          : `自动推进倒计时 (${autoRemaining.toFixed(1)}s)... 点击即刻推进`)
      : getAdvanceHint();
}

function refreshHoldLanguage(payload) {
  const duration = estimateReadingTime(payload.readingContent ?? payload.content ?? '', payload.actualLanguage || state.requestedLocale);
  if (state.usesEstimatedWait) {
    const progress = state.minimumHoldDuration > 0 ? Math.min(1, state.holdElapsed / state.minimumHoldDuration) : 1;
    state.minimumHoldDuration = duration;
    state.holdElapsed = progress * duration;
  }
  if (state.usesEstimatedAuto) {
    const progress = state.autoAdvanceDuration > 0 ? Math.min(1, state.autoAdvanceElapsed / state.autoAdvanceDuration) : 1;
    state.autoAdvanceDuration = duration;
    state.autoAdvanceElapsed = progress * duration;
    state.holdElapsed = state.autoAdvanceElapsed;
  }
  state.holdDuration = Math.max(state.minimumHoldDuration, state.autoAdvanceDuration);
  state.allowClickInterrupt = state.holdElapsed >= state.minimumHoldDuration;
  updateHoldHint();
}

// Stage / Viewport Advance Trigger
function handleAdvanceAction() {
  if (state === primaryReaderState && multiTrackReader.active) {
    multiTrackReader.routeInput(handleAdvanceAction);
    return;
  }
  if (state.status === 'WaitingForClock') return;
  if (state.status !== 'SuspendedAtBeat') {
    return; // Player must select an option
  }

  // 1. If currently typing -> fast forward
  if (state.isTyping) {
    fastForwardTypewriter();
    return;
  }

  // 2. If holding (.next / .wait)
  if (state.isHolding) {
    if (state.allowClickInterrupt) {
      const presId = state.currentPresentationId;
      clearTimers();
      state.isHolding = false;
      el.dockProgressBar.style.width = '0%';
      stepSession(presId);
    } else {
      el.dockStepHint.textContent = '强制停留中，请稍候...';
    }
    return;
  }

  // 3. Normal step
  // The next delivered sentence retires this display; an unreleased at gate does not.
  stepSession(state.currentPresentationId);
}

function clearTimers() {
  clearInterval(state.typewriterTimer);
  clearTimeout(state.fastForwardLockTimer);
  clearInterval(state.holdTimer);
  state.isTyping = false;
  state.isHolding = false;
  el.dockProgressBar.style.width = '0%';
}

// ==========================================================================
// Choice Menu Rendering (Ink Inline Style)
// ==========================================================================
function renderChoices(choicePayload) {
  retireCurrentSentence();
  clearTimers();
  state.currentActiveTypingEl = null;
  state.currentActiveSpeakerEl = null;
  state.currentActiveCursorEl = null;
  state.currentActivePassageEl = null;
  state.fullTextHtml = '';

  // If there is already an active unselected choice group on screen, remove it before rendering the updated one
  if (state.currentActiveChoiceEl && !state.currentActiveChoiceEl.classList.contains('has-selection')) {
    state.currentActiveChoiceEl.remove();
    state.currentActiveChoiceEl = null;
  }
  const existingUnselected = el.storyStream.querySelector('.choice-group-block:not(.has-selection)');
  if (existingUnselected) {
    existingUnselected.remove();
  }

  const choiceGroup = document.createElement('div');
  choiceGroup.className = 'choice-group-block';
  const choicePresentationId = choicePayload.presentationId || state.currentPresentationId;
  const choiceSessionId = state.currentSessionId;
  choiceGroup.dataset.presentationId = choicePresentationId;
  state.currentActiveChoiceEl = choiceGroup;

  const isInvestigate = choicePayload.containerName === 'investigate';
  const titleText = isInvestigate
    ? (state.requestedLocale === 'en' ? 'Investigation Hub' : state.requestedLocale === 'ja' ? '調査中 (Investigation Hub)' : '调查中 (Investigation Hub)')
    : (state.requestedLocale === 'en' ? 'Make a Choice' : state.requestedLocale === 'ja' ? '選択してください' : '请做出抉择');

  choiceGroup.innerHTML = `
    <div class="choice-group-header">
      <span class="choice-group-title">${escapeHtml(titleText)}</span>
      ${choicePayload.isLoop ? '<span class="choice-group-badge">LOOP</span>' : ''}
    </div>
    <div class="choice-options-list"></div>
  `;

  const listEl = choiceGroup.querySelector('.choice-options-list');

  choicePayload.options.forEach((opt, idx) => {
    const btn = document.createElement('button');
    btn.className = 'choice-card-btn';
    btn.disabled = !opt.canSelect;
    btn.dataset.choiceId = opt.id;

    const consumedTag = opt.isConsumed
      ? `<span class="choice-state-tag">${state.requestedLocale === 'en' ? '✓ Visited' : state.requestedLocale === 'ja' ? '✓ 探索済み' : '✓ 已探索'}</span>`
      : '';

    btn.innerHTML = `
      <div class="choice-btn-left">
        <span class="choice-marker">${escapeHtml(opt.marker || '*')}</span>
        <span class="choice-labels">
          <span class="choice-text">${renderTextValue(opt.text?.primary, opt.label, true)}</span>
          <span class="choice-secondary" hidden></span>
        </span>
      </div>
      ${consumedTag}
    `;

    btn.addEventListener('click', capturePresentationCallback((e) => {
      e.stopPropagation();
      // Guard: Stale button clicks on old content must not affect new content
      if (state.currentSessionId !== choiceSessionId || state.currentPresentationId !== choicePresentationId ||
        state.currentActiveChoiceEl !== choiceGroup) {
        console.debug('[Ktory] Ignored a choice button from an expired view.');
        return;
      }
      // Immediately disable all buttons in this choice group to prevent double submission
      choiceGroup.querySelectorAll('.choice-card-btn').forEach(b => b.disabled = true);
      btn.classList.add('is-selected');
      choiceGroup.classList.add('has-selection');
      state.currentActiveChoiceEl = null;
      submitChoice(opt.id, choicePresentationId, choiceSessionId);
    }));

    listEl.appendChild(btn);
  });

  el.storyStream.appendChild(choiceGroup);
  refreshSecondaryChoices(choicePayload);
  scrollToBottom();

  el.dockStepHint.textContent = state.requestedLocale === 'en'
    ? 'Please choose an option (press 1, 2... or click)'
    : state.requestedLocale === 'ja'
      ? '選択肢を選んでください (数字キー 1, 2... またはクリック)'
      : '请做出选择 (按数字键 1, 2... 或点击选项)';
}

// ==========================================================================
// Status Updates
// ==========================================================================
const STATUS_LABELS = {
  zh: { Ready: '就绪', SuspendedAtBeat: '等待推进', AwaitingChoice: '等待选择', Completed: '已完成', Error: '出错', WaitingForClock: '等待时钟', WaitingForTrack: '等待辅助结束' },
  en: { Ready: 'Ready', SuspendedAtBeat: 'Waiting to advance', AwaitingChoice: 'Awaiting choice', Completed: 'Completed', Error: 'Error', WaitingForClock: 'Waiting for clock', WaitingForTrack: 'Waiting for auxiliary track' },
  ja: { Ready: '準備完了', SuspendedAtBeat: '進行待ち', AwaitingChoice: '選択待ち', Completed: '完了', Error: 'エラー', WaitingForClock: 'クロック待ち', WaitingForTrack: '補助トラック待ち' }
};

function statusLabel(status, locale) {
  const labels = STATUS_LABELS[locale] || STATUS_LABELS.en;
  return labels[status] || status;
}

function updateDockStatus(status) {
  el.dockStatusText.textContent = statusLabel(status, state.requestedLocale);
  el.dockStatusDot.dataset.status = status;
}

// Helpers
function reportSessionError(message) {
  multiTrackReader.reset();
  readerClocks.reset();
  clearTimers();
  state.status = 'Error';
  state.choice = null;
  updateDockStatus('Error');
  if (window.ktoryReaderHost) window.ktoryReaderHost.onError(message);
  else alert(message);
}

function scrollToBottom() {
  requestAnimationFrame(() => {
    el.readerViewport.scrollTop = el.readerViewport.scrollHeight;
  });
}

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

// A presentation renderer per lane, sharing the single-track display policies
// above. Narrative execution remains entirely in Core and crosses one serial
// request queue. A refreshed snapshot is not an instruction to replay a beat.
class MultiTrackReader {
  constructor() {
    this.active = false;
    this.lanes = new Map();
    this.pending = new Set();
    this.container = null;
    this.lyricsDock = null;
    this.lyricsScrim = null;
  }

  ensureLyricsDock() {
    if (!this.lyricsDock) {
      let dock = document.getElementById('readerLyricsDock');
      let scrim = document.getElementById('readerLyricsScrim');
      if (!dock) {
        scrim = document.createElement('div');
        scrim.className = 'lyrics-gradient-scrim';
        scrim.id = 'readerLyricsScrim';
        scrim.setAttribute('aria-hidden', 'true');

        dock = document.createElement('aside');
        dock.className = 'reader-lyrics-dock';
        dock.id = 'readerLyricsDock';
        dock.setAttribute('role', 'region');
        dock.setAttribute('aria-label', 'Lyrics');

        const floatingDock = document.getElementById('floatingDock');
        if (floatingDock && floatingDock.parentNode) {
          floatingDock.parentNode.insertBefore(scrim, floatingDock);
          floatingDock.parentNode.insertBefore(dock, floatingDock);
        } else if (document.body) {
          document.body.appendChild(scrim);
          document.body.appendChild(dock);
        }
      }
      this.lyricsDock = dock;
      this.lyricsScrim = scrim;
    }
    this.lyricsDock.hidden = false;
    if (this.lyricsScrim) {
      this.lyricsScrim.hidden = false;
      this.lyricsScrim.classList?.add?.('visible');
    }
    const readerContainer = document.getElementById('readerContainer');
    if (readerContainer) {
      readerContainer.classList?.add?.('has-passive-lyrics');
    }
    return this.lyricsDock;
  }

  checkPassiveState() {
    const hasPassive = [...this.lanes.values()].some(lane => lane.isPassive && !lane.panel.hidden && !lane.panel?.classList?.contains('lane-completed'));
    if (!hasPassive) {
      if (this.lyricsDock) this.lyricsDock.hidden = true;
      if (this.lyricsScrim) {
        this.lyricsScrim.classList.remove('visible');
        this.lyricsScrim.hidden = true;
      }
      const readerContainer = document.getElementById('readerContainer');
      if (readerContainer) {
        readerContainer.classList?.remove?.('has-passive-lyrics');
      }
    } else {
      if (this.lyricsDock) this.lyricsDock.hidden = false;
      if (this.lyricsScrim) {
        this.lyricsScrim.hidden = false;
        this.lyricsScrim.classList?.add?.('visible');
      }
      const readerContainer = document.getElementById('readerContainer');
      if (readerContainer) {
        readerContainer.classList?.add?.('has-passive-lyrics');
      }
    }
    if (this.container) {
      const activeInteractiveCount = [...this.lanes.values()].filter(lane => !lane.isPassive && lane.panel.style.display !== 'none' && !lane.panel?.classList?.contains('lane-completed')).length;
      if (activeInteractiveCount === 0) {
        this.container.style.display = 'none';
      } else {
        this.container.style.display = '';
        this.container.classList?.toggle?.('single-interactive-lane', activeInteractiveCount === 1);
      }
    }
  }

  reset() {
    for (const lane of this.lanes.values()) {
      if (lane.state.displayTimer) clearTimeout(lane.state.displayTimer);
      this.retire(lane);
    }
    this.lanes.clear();
    this.pending.clear();
    this.active = false;
    this.container = null;
    if (this.lyricsDock) {
      this.lyricsDock.innerHTML = '';
      this.lyricsDock.hidden = true;
    }
    if (this.lyricsScrim) {
      this.lyricsScrim.classList?.remove?.('visible');
      this.lyricsScrim.hidden = true;
    }
    const readerContainer = document.getElementById('readerContainer');
    if (readerContainer) {
      readerContainer.classList?.remove?.('has-passive-lyrics');
    }
  }

  retire(lane) {
    withPresentationContext(lane, () => {
      clearTimers();
      clearAllPortraits();
      state.currentPresentationId = 0;
      state.status = 'Completed';
      state.currentActiveChoiceEl?.querySelectorAll('.choice-card-btn').forEach(button => button.disabled = true);
      lane.advance.hidden = true;
      lane.panel.classList.add('lane-completed');
      lane.elements.dockStepHint.textContent = '';
    });
    if (window.ktoryReaderHost) {
      lane.panel.remove();
    } else if (lane.isPassive) {
      lane.panel.remove();
      this.checkPassiveState();
    } else if (lane.shouldHide) {
      lane.panel.remove();
    }
  }

  createLane(presentation, isPassive = false, shouldHide = false) {
    const panel = document.createElement('section');
    panel.className = isPassive ? 'reader-lane passive-lyrics-lane' : 'reader-lane';
    panel.dataset.laneId = presentation.laneId;
    if (shouldHide) panel.style.display = 'none';
    const titleText = presentation.laneId === 'root' ? 'Story' : presentation.laneName;
    const title = document.createElement('h3');
    title.className = isPassive ? 'lyrics-badge-row' : 'reader-lane-title';
    if (isPassive) {
      title.innerHTML = `<span class="lyrics-badge-glyph">♫</span><span class="lyrics-badge-title">${escapeHtml(titleText)}</span>`;
    } else {
      title.textContent = titleText;
    }
    const portraits = document.createElement('div');
    portraits.className = 'lane-portraits';
    const left = document.createElement('div');
    left.className = 'portrait-slot portrait-left';
    const right = document.createElement('div');
    right.className = 'portrait-slot portrait-right';
    portraits.appendChild(left);
    portraits.appendChild(right);
    const stream = document.createElement('div');
    stream.className = isPassive ? 'lane-stream lyrics-stream' : 'lane-stream';
    const hint = document.createElement('div');
    hint.className = isPassive ? 'lane-hint lyrics-hint' : 'lane-hint';
    if (isPassive) hint.hidden = true;
    const progress = document.createElement('div');
    progress.className = isPassive ? 'lane-progress lyrics-progress' : 'lane-progress';
    if (isPassive) progress.style.display = 'none';
    const advance = document.createElement('button');
    advance.className = 'lane-advance';
    advance.type = 'button';
    advance.textContent = primaryReaderState.requestedLocale === 'zh' ? '继续' : primaryReaderState.requestedLocale === 'ja' ? '進む' : 'Continue';
    if (isPassive) advance.hidden = true;
    panel.appendChild(title);
    panel.appendChild(portraits);
    panel.appendChild(stream);
    panel.appendChild(progress);
    panel.appendChild(hint);
    panel.appendChild(advance);
    if (isPassive) {
      const lyricsDock = this.ensureLyricsDock();
      lyricsDock.appendChild(panel);
    } else if (this.container) {
      this.container.appendChild(panel);
    }
    const lane = {
      panel, advance, requiresInput: presentation.requiresInput, isPassive, shouldHide,
      portraits: { left: { id: null, src: null, timer: null }, right: { id: null, src: null, timer: null } },
      state: {
        ...primaryReaderState, laneId: presentation.laneId, currentPresentationId: 0,
        isTyping: false, isHolding: false, typewriterTimer: null, fastForwardLockTimer: null, holdTimer: null,
        currentActivePassageEl: null, currentActiveTypingEl: null, currentActiveSpeakerEl: null,
        currentActiveCursorEl: null, currentActiveChoiceEl: null, activeTags: [], fullTextHtml: ''
      },
      elements: {
        ...primaryReaderElements, storyStream: stream, dockStepHint: hint, dockProgressBar: progress,
        portraitSlotLeft: left, portraitSlotRight: right
      }
    };
    withPresentationContext(lane, () => advance.addEventListener('click', capturePresentationCallback(event => {
      event.stopPropagation();
      if (lane.requiresInput) handleAdvanceAction();
    })));

    return lane;
  }

  syncOutputs(snapshot, languageRefresh) {
    if (this.outputSession !== snapshot.sessionId) {
      this.reset();
      this.outputSession = snapshot.sessionId;
      this.lastOutputSequence = 0;
    }
    if (!this.active) {
      clearTimers();
      this.active = true;
      this.container = document.createElement('div');
      this.container.className = 'parallel-reader';
      primaryReaderElements.storyStream.appendChild(this.container);
    }

    const ensureLane = presentation => {
      const id = presentation.laneId ?? presentation.trackId;
      let lane = this.lanes.get(id);
      if (!lane) {
        const isMain = presentation.isMain === true;
        lane = this.createLane({ ...presentation, laneId: id,
          laneName: presentation.laneName ?? presentation.trackName ?? id,
          requiresInput: isMain }, !isMain, false);
        lane.isMain = isMain;
        if (!isMain) lane.panel.hidden = true;
        lane.state.isAuxiliary = !isMain;
        this.lanes.set(id, lane);
      }
      return lane;
    };
    const display = (presentation, refresh = false) => {
      const lane = ensureLane(presentation);
      const token = presentation.token;
      if (!token) return;
      const isNew = lane.state.currentSessionId !== token.sessionId ||
        lane.state.currentPresentationId !== token.presentationId;
      if (refresh && isNew) return;
      withPresentationContext(lane, () => {
        if (isNew) clearTimers();
        Object.assign(state, {
          currentSessionId: token.sessionId, currentPresentationId: token.presentationId,
          payload: prepareReaderPayload(presentation.payload), choice: presentation.choice,
          requestedLocale: snapshot.requestedLanguage, secondaryLocale: snapshot.secondaryLanguage,
          autoPolicy: presentation.autoPolicy || presentation.payload?.autoPolicy || null,
          isAuxiliary: !lane.isMain
        });
        if (!refresh) {
          state.executionEnded = false;
          state.status = presentation.choice ? 'AwaitingChoice' : 'SuspendedAtBeat';
        }
        lane.requiresInput = lane.isMain && !state.executionEnded;
        lane.advance.hidden = !lane.requiresInput || state.status !== 'SuspendedAtBeat';
        if (state.choice) {
          if (refresh && languageRefresh === 'secondary') refreshSecondaryChoices(state.choice);
          else if (isNew || refresh) renderChoices(state.choice);
        } else if (state.payload) {
          if (isNew && state.payload.content) lane.panel.hidden = false;
          if (refresh && languageRefresh === 'secondary') renderSecondaryText(state.payload);
          else if (isNew) renderBeat(state.payload);
          else if (refresh) renderBeat(state.payload, true);

        }
      });
    };
    const endExecution = lane => {
      lane.requiresInput = false;
      lane.advance.hidden = true;
      lane.state.status = 'Completed';
      lane.state.executionEnded = true;
      // Printing and display lifetime may continue. Only input/advance work is retired.
      clearInterval(lane.state.holdTimer);
      lane.state.holdTimer = null;
      lane.state.isHolding = false;
      lane.state.autoAdvanceOnHoldEnd = false;
      lane.state.currentActiveChoiceEl?.querySelectorAll('.choice-card-btn').forEach(button => button.disabled = true);
    };

    for (const output of snapshot.outputEvents || []) {
      if (output.sessionId !== snapshot.sessionId || output.sequence <= this.lastOutputSequence) continue;
      this.lastOutputSequence = output.sequence;
      const kind = typeof output.kind === 'number'
        ? ['Content', 'LanguageRefresh', 'TrackEnded', 'SessionEnded'][output.kind] : output.kind;
      if (kind === 'Content' || kind === 'LanguageRefresh') display(output, kind === 'LanguageRefresh');
      else if (kind === 'TrackEnded') {
        const lane = this.lanes.get(output.trackId);
        if (lane) endExecution(lane);
      } else if (kind === 'SessionEnded') {
        for (const lane of this.lanes.values()) endExecution(lane);
      }
    }

    // Snapshots support initial observation/reconnection, never replay an existing beat's effects.
    const missingRetainedHeads = new Set((snapshot.retainedPresentations || [])
      .filter(presentation => !this.lanes.has(presentation.laneId)).map(presentation => presentation.laneId));
    for (const presentation of snapshot.retainedPresentations || []) {
      if (missingRetainedHeads.has(presentation.laneId)) display(presentation);
    }
    const active = new Map((snapshot.activePresentations || []).map(p => [p.laneId, p]));
    const gates = new Map((snapshot.pendingClockGates || []).map(gate => [gate.laneId, gate]));
    for (const gate of gates.values()) {
      const lane = ensureLane({ ...gate, isMain: gate.isMain ?? gate.laneId === 'root' });
      lane.clockGate = gate;
      lane.requiresInput = false;
      lane.advance.hidden = !lane.requiresInput;
      lane.state.status = 'WaitingForClock';
      lane.state.clockGate = gate;
      lane.state.requiresInput = lane.requiresInput;
      lane.state.currentSessionId = snapshot.sessionId;
      // A gate has an execution token; it must not replace a displayed lyric's token.
      if (!lane.state.currentPresentationId) lane.state.currentPresentationId = gate.token.presentationId;
      const uiLocale = (window.ktoryReaderHost ? document.documentElement?.lang : snapshot.requestedLanguage) || 'en';
      const hint = readerClocks.waitHint(gate, uiLocale);
      lane.elements.dockStepHint.textContent = hint;
      lane.panel.title = gate.sourceName ? hint : '';
    }
    for (const [id, lane] of this.lanes) {
      if (gates.has(id)) continue;
      lane.clockGate = null;
      lane.panel.title = '';
      if (active.has(id)) {
        const presentation = active.get(id);
        if (lane.state.currentPresentationId !== presentation.token.presentationId) display(presentation);
        lane.requiresInput = lane.isMain;
        lane.advance.hidden = !lane.isMain || Boolean(presentation.choice);
      } else {
        lane.requiresInput = false;
        lane.advance.hidden = true;
      }
    }
    if (snapshot.status === 'Completed' || snapshot.status === 'Error') {
      for (const lane of this.lanes.values()) endExecution(lane);
      primaryReaderElements.storyCompletedBanner.style.display = snapshot.status === 'Completed' ? 'block' : 'none';
    }
    this.checkPassiveState();
    this.updateDock();
    scrollToBottom();
    return true;
  }

  sync(snapshot, languageRefresh) {
    // Current multi-head snapshots always carry ordered output delivery. Legacy
    // single-beat responses still use the primary renderer below this adapter.
    if (!Array.isArray(snapshot.outputEvents)) return false;
    return this.syncOutputs(snapshot, languageRefresh);
  }

  interactiveLane(requireInput = false) {
    if (requireInput) {
      return [...this.lanes.values()].find(lane => !lane.isPassive && lane.panel.style.display !== 'none' && lane.requiresInput) ||
             [...this.lanes.values()].find(lane => lane.requiresInput) ||
             null;
    }
    return [...this.lanes.values()].find(lane => !lane.isPassive && lane.panel.style.display !== 'none' && lane.requiresInput) ||
           [...this.lanes.values()].find(lane => !lane.isPassive && lane.panel.style.display !== 'none') ||
           [...this.lanes.values()][0];
  }

  routeInput(action) {
    const lane = this.interactiveLane(true);
    if (lane) withPresentationContext(lane, action);
    this.updateDock();
  }

  chooseNumber(number) {
    const lane = this.interactiveLane(true);
    const option = Number.isInteger(number) && number >= 1 ? lane?.state.choice?.options[number - 1] : null;
    if (option?.canSelect) this.command('choice', {
      sessionId: lane.state.currentSessionId, presentationId: lane.state.currentPresentationId
    }, option.id);
  }

  toggleAutoPlay() {
    const enabled = !primaryReaderState.autoPlay;
    primaryReaderState.autoPlay = enabled;
    primaryReaderElements.btnAutoPlay.classList.toggle('active', enabled);
    primaryReaderElements.btnAutoPlay.setAttribute('aria-pressed', String(enabled));
    for (const lane of this.lanes.values()) {
      if (lane.state.autoPlay !== enabled) withPresentationContext(lane, toggleAutoPlay);
    }
    this.updateDock();
  }

  updateDock() {
    if (!this.active) return;
    for (const item of this.lanes.values()) {
      if (item.requiresInput || item.isPassive || item.shouldHide) continue;
      const current = item.state;
      if (current.isTyping) {
        item.elements.dockStepHint.textContent = current.requestedLocale === 'zh' ? '自动轨道 · 文字呈现中' :
          current.requestedLocale === 'ja' ? '自動トラック · テキスト表示中' : 'Automatic track · Presenting text';
      } else if (current.isHolding) {
        const remaining = Math.max(0, current.minimumHoldDuration - current.holdElapsed,
          current.autoAdvanceOnHoldEnd ? current.autoAdvanceDuration - current.autoAdvanceElapsed : 0);
        item.elements.dockStepHint.textContent = current.requestedLocale === 'zh' ? `自动轨道 · ${remaining.toFixed(1)} 秒` :
          current.requestedLocale === 'ja' ? `自動トラック · ${remaining.toFixed(1)} 秒` : `Automatic track · ${remaining.toFixed(1)}s`;
      }
    }
    const lane = this.interactiveLane();
    const elements = primaryReaderElements;
    elements.dockStatusText.textContent = statusLabel(primaryReaderState.status, primaryReaderState.requestedLocale);
    elements.dockStatusDot.dataset.status = primaryReaderState.status;
    elements.dockStepHint.textContent = lane?.elements.dockStepHint.textContent ||
      (primaryReaderState.requestedLocale === 'zh' ? '各轨道独立推进中' : primaryReaderState.requestedLocale === 'ja' ? '各トラックを再生中' : 'Tracks running independently');
    if (primaryReaderState.status === 'Completed' || primaryReaderState.status === 'WaitingForTrack') {
      elements.dockStepHint.textContent = statusLabel(primaryReaderState.status, primaryReaderState.requestedLocale);
      if (lane) lane.elements.dockStepHint.textContent = elements.dockStepHint.textContent;
    }
    if (primaryReaderState.status === 'WaitingForClock' || primaryReaderState.status === 'WaitingForTrack') {
      const unavailable = readerClocks.gates.find(gate => {
        const source = gate.sourceName || readerClocks.clocks.get(gate.clockId)?.sourceName;
        return source && (source !== 'music.position' || readerClocks.readMusicPosition() === null);
      });
      if (unavailable) elements.dockStepHint.textContent = readerClocks.waitHint(unavailable, primaryReaderState.requestedLocale);
    }
    elements.dockLocaleTag.textContent = (lane?.state.payload?.actualLanguage || primaryReaderState.requestedLocale || '').toUpperCase();
  }

  findToken(token) {
    return [...this.lanes.values()].find(lane => lane.state.currentSessionId === token.sessionId &&
      lane.state.currentPresentationId === token.presentationId);
  }

  command(kind, token, choiceId) {
    const lane = this.findToken(token);
    if (!lane || lane.isMain === false || lane.state.executionEnded) return Promise.resolve();
    if (kind === 'step' && lane.state.status === 'WaitingForClock') return Promise.resolve();
    if ((kind === 'step' && lane.state.status !== 'SuspendedAtBeat') ||
      (kind === 'choice' && lane.state.status !== 'AwaitingChoice')) return Promise.resolve();
    const key = `${token.sessionId}:${token.presentationId}`;
    if (this.pending.has(key)) return Promise.resolve();
    this.pending.add(key);
    withPresentationContext(lane, () => {
      clearTimers();
      if (kind === 'choice') {
        state.currentActiveChoiceEl?.classList.add('has-selection');
        state.currentActiveChoiceEl?.querySelectorAll('.choice-card-btn').forEach(button => button.disabled = true);
        state.currentActiveChoiceEl = null;
      }
    });
    return enqueueSessionOp(async epoch => {
      try {
        if (epoch !== currentSessionEpoch || !this.findToken(token)) return;
        if (isWasmPending()) await ensureRuntimeReady();
        let data;
        const clockInput = readerClocks.captureInput(token.sessionId);
        if (window.KtoryWasm?.[kind]) {
          data = kind === 'choice'
            ? await window.KtoryWasm.choice(choiceId, token.presentationId, token.sessionId, clockInput.deltaTime, clockInput.sourcePositions)
            : await window.KtoryWasm.step(token.presentationId, token.sessionId, clockInput.deltaTime, clockInput.sourcePositions);
        } else {
          const response = await fetch(`/api/session/${kind}`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ...token, ...clockInput, ...(kind === 'choice' ? { choiceId } : {}) })
          });
          if (epoch !== currentSessionEpoch) return;
          if (!response.ok) throw new Error((await response.json()).error || 'Story input failed');
          data = await response.json();
        }
        if (epoch === currentSessionEpoch && data) updateState(data);
      } catch (error) {
        if (epoch === currentSessionEpoch) reportSessionError(error.message || String(error));
      } finally { this.pending.delete(key); }
    });
  }
}

const multiTrackReader = new MultiTrackReader();

class ReaderClocks {
  constructor() {
    this.sessionId = '';
    this.gates = [];
    this.clocks = new Map();
    this.supportsClocks = false;
    this.pending = new Set();
    this.timer = null;
  }

  now() { return typeof performance !== 'undefined' ? performance.now() : Date.now(); }

  reset() {
    clearInterval(this.timer);
    this.timer = null;
    this.sessionId = '';
    this.gates = [];
    this.clocks.clear();
    this.supportsClocks = false;
    this.pending.clear();
  }

  captureInput(sessionId) {
    if (!this.supportsClocks || sessionId !== this.sessionId) return {};
    const sampledAt = this.now();
    const deltaTime = Math.max(0, (sampledAt - this.sampledAt) / 1000);
    this.sampledAt = sampledAt;
    const sourcePositions = {};
    for (const clock of this.clocks.values()) {
      if (clock.sourceName === 'music.position') sourcePositions[clock.sourceName] = this.readMusicPosition();
    }
    return { deltaTime, sourcePositions };
  }

  sync(snapshot) {
    if (!snapshot.sessionId || snapshot.status === 'Completed' || snapshot.status === 'Error') {
      this.reset();
      return;
    }
    if (this.sessionId !== snapshot.sessionId) {
      this.reset();
      this.sessionId = snapshot.sessionId;
      this.sampledAt = this.now();
    }
    this.supportsClocks = Array.isArray(snapshot.clocks);
    this.clocks = new Map((snapshot.clocks || []).map(clock => [clock.clockId, clock]));
    this.gates = snapshot.pendingClockGates || [];
    const hasResolvableGates = this.gates.some(gate => {
      const clock = this.clocks.get(gate.clockId);
      const source = gate.sourceName || clock?.sourceName;
      return !source || source === 'music.position';
    });
    if (hasResolvableGates && !this.timer) {
      this.timer = setInterval(() => this.tick(), 50);
    } else if (!hasResolvableGates && this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
    this.tick();
  }

  readMusicPosition() {
    // This is the BGM player's actual media position, including pause and seek.
    // Missing/loading/failed players have no value; never substitute wall time.
    const audio = currentBgmAudio;
    if (!audio || audio.readyState < 1 || audio.error || !Number.isFinite(audio.currentTime)) return null;
    return audio.currentTime;
  }

  waitHint(gate, locale = 'en') {
    const clock = this.clocks.get(gate.clockId);
    const source = gate.sourceName || clock?.sourceName;
    const label = `${gate.clockName ?? source ?? 'clock'} ≥ ${gate.position}`;
    if (source && source !== 'music.position') {
      return locale.startsWith('zh') ? `等待 ${label}；Reader 无法解析来源 ${source}` :
        locale.startsWith('ja') ? `${label} を待機中：Reader は参照元 ${source} を解釈できません` :
        `Waiting: ${label}; Reader cannot resolve source ${source}`;
    }
    if (source && this.readMusicPosition() === null) {
      return locale.startsWith('zh') ? `等待 ${label}；BGM 播放器暂无可用进度` :
        locale.startsWith('ja') ? `${label} を待機中：BGM の再生位置を取得できません` :
        `Waiting: ${label}; BGM playback position is unavailable`;
    }
    return locale.startsWith('zh') ? `等待：${label}` : locale.startsWith('ja') ? `待機：${label}` : `Waiting: ${label}`;
  }

  tick() {
    if (!this.sessionId || this.pending.has(this)) return;
    for (const gate of this.gates) {
      const clock = this.clocks.get(gate.clockId);
      if (!clock) continue;
      const source = gate.sourceName || clock.sourceName;
      if (!source) {
        const position = clock.position + Math.max(0, (this.now() - this.sampledAt) / 1000);
        if (position >= gate.position) return this.advance();
      } else if (source === 'music.position') {
        const position = this.readMusicPosition();
        if (position !== (clock.position ?? null)) return this.advance();
      }
    }
  }

  advance() {
    const sessionId = this.sessionId;
    this.pending.add(this);
    return enqueueSessionOp(async epoch => {
      try {
        if (epoch !== currentSessionEpoch || sessionId !== this.sessionId) return;
        const { deltaTime = 0, sourcePositions = {} } = this.captureInput(sessionId);
        let data;
        if (window.KtoryWasm?.advanceClocks) data = await window.KtoryWasm.advanceClocks(deltaTime, sessionId, sourcePositions);
        else {
          const response = await fetch('/api/session/clocks/advance', {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ deltaTime, sessionId, sourcePositions })
          });
          if (epoch !== currentSessionEpoch) return;
          if (!response.ok) throw new Error((await response.json()).error || 'Clock advance failed');
          data = await response.json();
        }
        if (epoch === currentSessionEpoch && sessionId === this.sessionId && data) updateState(data, 'clock');
      } catch (error) {
        if (epoch === currentSessionEpoch) reportSessionError(error.message || String(error));
      } finally { if (sessionId === this.sessionId) this.pending.delete(this); }
    });
  }
}

const readerClocks = new ReaderClocks();
