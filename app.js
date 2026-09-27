/**
 * Ktory Archives - Minimalist Ink Pure-Text Reader Client
 * Compliant with Ktory Implementation Specification v1.0
 */

const state = {
  status: 'Ready',
  payload: null,
  choice: null,
  requestedLocale: 'zh',
  defaultLocale: 'zh',
  autoPlay: false,

  // Font & Typography
  fontMode: 'serif', // 'serif' | 'sans'
  fontSizeIndex: 1, // 0: sm, 1: md, 2: lg
  fontSizes: ['font-size-sm', 'font-size-md', 'font-size-lg'],
  fontSizeLabels: ['85%', '100%', '115%'],

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

// DOM Elements Cache
const el = {
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
  btnAutoPlay: document.getElementById('btnAutoPlay'),
  btnFontToggle: document.getElementById('btnFontToggle'),
  fontToggleLabel: document.getElementById('fontToggleLabel'),
  btnFontSizeToggle: document.getElementById('btnFontSizeToggle'),
  fontSizeLabel: document.getElementById('fontSizeLabel'),
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

  // Floating Dock
  floatingDock: document.getElementById('floatingDock'),
  dockProgressBar: document.getElementById('dockProgressBar'),
  dockStatusDot: document.getElementById('dockStatusDot'),
  dockStatusText: document.getElementById('dockStatusText'),
  dockStepHint: document.getElementById('dockStepHint'),
  dockLocaleTag: document.getElementById('dockLocaleTag'),
  btnFastForward: document.getElementById('btnFastForward'),

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

const PORTAL_I18N = {
  'zh': {
    tagline: '轻量 · 优雅 · 对白驱动的叙事创作与接入系统',
    sampleTitle: '尝试示例剧本',
    writeTitle: '撰写我的剧本',
    home: '官网'
  },
  'en': {
    tagline: 'Lightweight, elegant, dialogue-driven narrative engine and runtime',
    sampleTitle: 'Try Sample Script',
    writeTitle: 'Write My Script',
    home: 'Home'
  },
  'ja': {
    tagline: '軽量・優雅・対話主導のシナリオ制作・接続システム',
    sampleTitle: 'サンプルを試読',
    writeTitle: '脚本を作成する',
    home: '公式サイト'
  }
};

function updatePortalLabels(locale) {
  const texts = PORTAL_I18N[locale] || PORTAL_I18N['zh'];
  if (el.portalTagline) el.portalTagline.textContent = texts.tagline;
  if (el.portalSampleTitle) el.portalSampleTitle.textContent = texts.sampleTitle;
  if (el.portalWriteTitle) el.portalWriteTitle.textContent = texts.writeTitle;
  if (el.navHomeText) el.navHomeText.textContent = texts.home;
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
    } else if (e.code === 'Escape') {
      closeAllDrawers();
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
      const sampleKey = state.currentSampleKey || Object.keys(state.samples)[0];
      const script = (sampleKey && state.samples[sampleKey]) || el.scriptInput.value;
      if (script) {
        await startSession(script, state.requestedLocale || 'zh');
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

  // Fast forward dock button
  el.btnFastForward.addEventListener('click', (e) => {
    e.stopPropagation();
    fastForwardTypewriter();
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
        const customKey = `[上传] ${title}`;
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
  } catch {}

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
      } catch {}
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
        } catch {}
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

    let selectedText = '-- 选择示例剧本 --';

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
  state.autoPlay = !state.autoPlay;
  el.btnAutoPlay.classList.toggle('active', state.autoPlay);

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
// WebAssembly Bridge (For Vercel / GitHub Pages Static Hosting)
// ==========================================================================
window.registerKtoryWasmBridge = function(dotNetRef) {
  window.KtoryWasm = {
    async start(script, requestedLocale, entryBlock) {
      const json = await dotNetRef.invokeMethodAsync('Start', script, requestedLocale, entryBlock || null);
      return JSON.parse(json);
    },
    async step(expectedPresentationId = null, expectedSessionId = null) {
      const json = await dotNetRef.invokeMethodAsync('Step', expectedPresentationId, expectedSessionId);
      return JSON.parse(json);
    },
    async choice(choiceId, expectedPresentationId = null, expectedSessionId = null) {
      const json = await dotNetRef.invokeMethodAsync('Choice', choiceId, expectedPresentationId, expectedSessionId);
      return JSON.parse(json);
    },
    async break(expectedPresentationId = null, expectedSessionId = null) {
      const json = await dotNetRef.invokeMethodAsync('Break', expectedPresentationId, expectedSessionId);
      return JSON.parse(json);
    },
    async setLanguage(locale, expectedPresentationId = null, expectedSessionId = null) {
      const json = await dotNetRef.invokeMethodAsync('SetLanguage', locale, expectedPresentationId, expectedSessionId);
      return JSON.parse(json);
    },
    async getSamples() {
      const json = await dotNetRef.invokeMethodAsync('GetSamples');
      return JSON.parse(json);
    }
  };
  console.log('[Ktory] WebAssembly in-browser engine is ready.');

  if (window.ktoryReaderHost) {
    window.ktoryReaderHost.onRuntimeReady();
    return;
  }

  // If page loaded before WASM booted, load samples and kick off session
  if (Object.keys(state.samples).length === 0) {
    loadSamples().then(() => {
      const sampleKeys = Object.keys(state.samples);
      if (sampleKeys.length > 0 && !state.payload) {
        state.currentSampleKey = sampleKeys[0];
        el.scriptInput.value = state.samples[state.currentSampleKey];
        document.dispatchEvent(new Event('ktory:source-changed'));
        syncSampleSelect(state.currentSampleKey);
        startSession(state.samples[state.currentSampleKey], 'zh');
      }
    });
  }
};

// ==========================================================================
// API Interaction (Dual-Mode: Local REST API or In-Browser WASM)
// ==========================================================================
async function loadSamples() {
  try {
    let samples = null;
    if (window.KtoryWasm && window.KtoryWasm.getSamples) {
      samples = await window.KtoryWasm.getSamples();
    } else {
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
        el.sampleSelect.innerHTML = '<option value="">-- 选择示例剧本 --</option>';

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

function enqueueSessionOp(opFn) {
  const opEpoch = currentSessionEpoch;
  sessionOpChain = sessionOpChain.then(async () => {
    // If epoch changed before this op began, discard it immediately
    if (opEpoch !== currentSessionEpoch) {
      return;
    }
    try {
      await opFn(opEpoch);
    } catch (err) {
      console.error('Session operation failed:', err);
    }
  });
  return sessionOpChain;
}

async function startSession(script, requestedLocale = 'zh', entryBlock = null) {
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
  currentSessionEpoch++;
  const sessionEpoch = currentSessionEpoch;

  // If entryBlock is not provided or empty, normalize to null (defaults to root block)
  if (!entryBlock) {
    entryBlock = null;
  }

  return enqueueSessionOp(async (opEpoch) => {
    if (sessionEpoch !== currentSessionEpoch) return;

    try {
      let data = null;
      if (window.KtoryWasm && window.KtoryWasm.start) {
        data = await window.KtoryWasm.start(script, requestedLocale, entryBlock);
      } else {
        const res = await fetch('/api/session/start', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ script, requestedLocale, entryBlock })
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
      let data = null;
      if (window.KtoryWasm && window.KtoryWasm.step) {
        data = await window.KtoryWasm.step(targetPresentationId, targetSessionId);
      } else {
        const res = await fetch('/api/session/step', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ presentationId: targetPresentationId, sessionId: targetSessionId })
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
      let data = null;
      if (window.KtoryWasm && window.KtoryWasm.choice) {
        data = await window.KtoryWasm.choice(choiceId, targetPresentationId, targetSessionId);
      } else {
        const res = await fetch('/api/session/choice', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ choiceId, presentationId: targetPresentationId, sessionId: targetSessionId })
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
  updatePortalLabels(locale);
  el.langSwitcher.querySelectorAll('.pill-btn').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.locale === locale);
  });
  if (state.status === 'Error' || state.status === 'Loading' || state.status === 'Ready') return;

  const targetSessionId = state.currentSessionId;

  return enqueueSessionOp(async (opEpoch) => {
    if (opEpoch !== currentSessionEpoch) return;

    try {
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

function updateState(serverState, isLanguageSwitch = false) {
  window.ktoryReaderHost?.onState(serverState);
  if (window.ktoryReaderHost?.sanitizeHtml && serverState.payload &&
      serverState.payload.stepType !== 1 && serverState.payload.stepType !== 'Directive') {
    serverState = { ...serverState, payload: { ...serverState.payload,
      content: window.ktoryReaderHost.sanitizeHtml(serverState.payload.content || '') } };
  }
  state.status = serverState.status;
  state.payload = serverState.payload;
  state.choice = serverState.choice;
  state.currentSessionId = serverState.sessionId || '';
  state.currentPresentationId = serverState.presentationId ??
                                serverState.payload?.presentationId ??
                                serverState.choice?.presentationId ??
                                0;
  state.requestedLocale = serverState.requestedLanguage;
  state.defaultLocale = serverState.defaultLanguage;
  state.visitedItems = serverState.visitedItems || [];
  state.callStackDepth = serverState.callStackDepth || 0;
  state.recentTags = serverState.recentTags || [];
  state.diagnostics = serverState.diagnostics || [];
  renderDiagnostics();
  state.autoPolicy = serverState.autoPolicy || serverState.payload?.autoPolicy || null;

  if (el.dockLocaleTag) el.dockLocaleTag.textContent = (serverState.payload?.actualLanguage || state.requestedLocale || 'ZH').toUpperCase();

  updateDockStatus(state.status);

  // If Completed
  if (state.status === 'Completed') {
    clearAllPortraits();
    el.storyCompletedBanner.style.display = 'block';
    el.dockStepHint.textContent = '剧本演练已全部结束';
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
  }
}

function renderDiagnostics() {
  if (!el.runtimeDiagnostics) return;
  // InputIgnored (512) is a background diagnostic, never a player-facing warning.
  const warnings = state.diagnostics.filter(item => item.kind === 'Warning' || item.kind === 256);
  el.runtimeDiagnostics.hidden = warnings.length === 0;
  el.runtimeDiagnosticSummary.textContent = `剧本警告（${warnings.length}）`;
  el.runtimeDiagnosticMessages.textContent = warnings.map(item =>
    `${item.block || 'Root'} · L${item.lineNumber}: ${item.message}`).join('\n');
}

// ==========================================================================
// Portrait Stage & Character Presentation Manager
// ==========================================================================
const portraitState = {
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
    try { audio.currentTime = 0; } catch (_) {}
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
        try { audio.currentTime = 0; } catch (_) {}
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

  // If already playing this track, adjust volume if needed
  if (currentBgmAudio && currentBgmTrack === resolvedUrl && !currentBgmAudio.paused) {
    setBgmVolume(volume, 0);
    return;
  }

  stopBgm(0);

  const audio = new Audio();
  audio.loop = true;
  audio.volume = volume;
  audio.src = resolvedUrl;

  audio.onerror = () => {
    if (resolvedUrl.startsWith('audio/')) {
      const fallbackUrl = resolvedUrl.slice(6);
      if (audio.src !== fallbackUrl) {
        audio.src = fallbackUrl;
        audio.play().catch(() => {});
      }
    }
  };

  currentBgmAudio = audio;
  currentBgmTrack = resolvedUrl;

  const playPromise = audio.play();
  if (playPromise !== undefined) {
    playPromise.catch(err => {
      if (err.name === 'NotAllowedError' || err.name === 'AbortError') {
        pendingBgm = { audio, targetVolume: volume };
      } else {
        console.warn('BGM playback failed:', err);
      }
    });
  }
}

function resumePendingAudio() {
  if (pendingBgm && pendingBgm.audio) {
    const { audio, targetVolume } = pendingBgm;
    pendingBgm = null;
    audio.volume = targetVolume;
    audio.play().catch(e => {
      console.warn('Failed to resume pending audio:', e);
    });
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
    } else if (name === 'stopbgm' || name === 'stop_bgm' || name === 'pausebgm' || name === 'pause_bgm') {
      let duration = parseDurationArg(namedArgs.duration ?? namedArgs.fade);
      if (duration == null && posArgs.length > 0) {
        duration = parseDurationArg(posArgs[0]);
      }
      if (duration == null) duration = 0;

      stopBgm(duration);
    }
  }
}

function renderBeat(payload, isLanguageSwitch = false) {
  const isDirective = payload.stepType === 1 || payload.stepType === 'Directive';

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
    if (el.btnFastForward) el.btnFastForward.disabled = true;

    // 2. Hide typing cursor
    if (state.currentActiveCursorEl) {
      state.currentActiveCursorEl.style.display = 'none';
    }

    // 3. Dynamically update speaker display name in DOM
    if (state.currentActiveSpeakerEl) {
      state.currentActiveSpeakerEl.textContent = payload.speaker || '';
      state.currentActiveSpeakerEl.title = payload.speakerActualLanguage ? `名字语言：${payload.speakerActualLanguage}` : '';
    } else if (payload.speaker && state.currentActivePassageEl) {
      const spEl = state.currentActivePassageEl.querySelector('.speaker-name');
      if (spEl) {
        spEl.textContent = payload.speaker;
        spEl.title = payload.speakerActualLanguage ? `名字语言：${payload.speakerActualLanguage}` : '';
      }
    }

    // 4. Update the text content
    state.fullTextHtml = payload.content;
    state.currentActiveTypingEl.innerHTML = payload.content;

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
      el.dockStepHint.textContent = '点击页面或按 [空格] 推进阅读 ▾';
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
      state.currentActiveSpeakerEl.title = payload.speakerActualLanguage ? `名字语言：${payload.speakerActualLanguage}` : '';
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

  state.currentActivePassageEl = passage;
  el.storyStream.appendChild(passage);
  scrollToBottom();

  startTypewriter(textContainer, cursorEl, payload.content, tags);
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
  el.dockStepHint.textContent = '文字呈现中... 点击可快速显示全文';

  // Parse .skippable(false, [duration])
  state.canFastForward = true;
  if (el.btnFastForward) el.btnFastForward.disabled = false;
  const skippableTag = tags.find(t => t.name.toLowerCase() === 'skippable');
  if (skippableTag) {
    const rawAllowed = skippableTag.positionalArgs.length > 0 ? skippableTag.positionalArgs[0] : true;
    const allowed = rawAllowed !== false && rawAllowed !== 'false';
    if (!allowed) {
      state.canFastForward = false;
      if (el.btnFastForward) el.btnFastForward.disabled = true;

      // Check if explicit duration parameter t is provided
      const hasDuration = skippableTag.positionalArgs.length > 1 &&
                          skippableTag.positionalArgs[1] !== null &&
                          skippableTag.positionalArgs[1] !== '';
      if (hasDuration) {
        const duration = Number(skippableTag.positionalArgs[1]);
        if (duration > 0) {
          el.dockStepHint.textContent = `快显锁定中 (${duration}s)...`;
          state.fastForwardLockTimer = setTimeout(() => {
            if (typewriterPresentationId !== state.currentPresentationId || typewriterEpoch !== currentSessionEpoch) {
              return;
            }
            state.canFastForward = true;
            if (el.btnFastForward) el.btnFastForward.disabled = false;
            el.dockStepHint.textContent = '点击可快速显示全文';
          }, duration * 1000);
        } else {
          state.canFastForward = true;
          if (el.btnFastForward) el.btnFastForward.disabled = false;
        }
      } else {
        // When t is omitted, fast-forward is prohibited for the entire printing duration per Spec §2.6
        el.dockStepHint.textContent = '文字呈现中 (禁止快显)...';
      }
    }
  }

  const tokens = tokenizeHtml(htmlContent);
  let tokenIdx = 0;
  targetEl.innerHTML = '';

  const speedMs = 18; // literary typewriter speed
  state.typewriterTimer = setInterval(() => {
    if (typewriterPresentationId !== state.currentPresentationId || typewriterEpoch !== currentSessionEpoch) {
      clearInterval(state.typewriterTimer);
      state.typewriterTimer = null;
      return;
    }
    if (tokenIdx >= tokens.length) {
      finishTypewriter(cursorEl, tags, typewriterPresentationId, typewriterEpoch);
      return;
    }

    targetEl.innerHTML += tokens[tokenIdx++];
    scrollToBottom();
  }, speedMs);
}

function fastForwardTypewriter() {
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
  if (el.btnFastForward) el.btnFastForward.disabled = true;
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
    tokens.push(html[i]);
    i++;
  }
  return tokens;
}

function estimateReadingTime(text, lang = 'zh') {
  if (!text) return 1.0;
  const l = (lang || '').toLowerCase();
  if (l.startsWith('zh') || l.startsWith('ja')) {
    return Math.max(1.0, text.length / 7.0);
  }
  const words = text.trim().split(/\s+/).length;
  return Math.max(1.0, words / 3.5);
}

// ==========================================================================
// Hold & Auto-Advance Timer (.next / .wait / #AUTO)
// ==========================================================================
function setupHoldTimer(tags, defaultHoldSec = 0) {
  if (state.holdTimer) {
    clearInterval(state.holdTimer);
    state.holdTimer = null;
  }

  const holdPresentationId = state.currentPresentationId;
  const holdEpoch = currentSessionEpoch;

  const nextTag = tags.find(t => t.name.toLowerCase() === 'next');
  const waitTag = tags.find(t => t.name.toLowerCase() === 'wait');
  const autoPolicy = state.autoPolicy || (state.payload && state.payload.autoPolicy);

  const readingTime = estimateReadingTime(state.payload?.content || '', state.payload?.actualLanguage || state.requestedLocale);
  state.usesEstimatedWait = !!waitTag && waitTag.positionalArgs.length === 0;
  state.usesEstimatedAuto = false;
  state.minimumHoldDuration = waitTag
    ? (state.usesEstimatedWait ? readingTime : Math.max(0, Number(waitTag.positionalArgs[0]) || 0)) : 0;
  state.autoAdvanceOnHoldEnd = !!nextTag || !!autoPolicy?.enabled || state.autoPlay;
  if (nextTag) {
    state.autoAdvanceDuration = Math.max(0, Number(nextTag.positionalArgs[0]) || 0);
  } else if (waitTag) {
    // A local wait replaces the automatic default delay without enabling automatic playback.
    state.autoAdvanceDuration = 0;
  } else if (autoPolicy && autoPolicy.enabled) {
    state.usesEstimatedAuto = autoPolicy.useEstimatedReadingTime &&
      (state.payload?.stepType === 0 || state.payload?.stepType === 'Text');
    state.autoAdvanceDuration = state.usesEstimatedAuto ? readingTime : Math.max(0, Number(autoPolicy.defaultWaitSeconds) || 0);
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
    state.holdTimer = setInterval(() => {
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
          el.dockStepHint.textContent = '点击页面或按 [空格] 推进阅读 ▾';
        }
      }
    }, intervalMs);
  } else {
    // Normal manual step
    state.isHolding = false;
    el.dockProgressBar.style.width = '0%';
    el.dockStepHint.textContent = '点击页面或按 [空格] 推进阅读 ▾';
  }
}

function updateHoldHint() {
  const waitRemaining = Math.max(0, state.minimumHoldDuration - state.holdElapsed);
  const autoRemaining = Math.max(0, state.autoAdvanceDuration - state.autoAdvanceElapsed);
  el.dockStepHint.textContent = !state.allowClickInterrupt
    ? `强制停留中 (${waitRemaining.toFixed(1)}s)... 点击无效`
    : state.autoAdvanceOnHoldEnd
      ? `自动推进倒计时 (${autoRemaining.toFixed(1)}s)... 点击即刻推进`
      : '点击页面或按 [空格] 推进阅读 ▾';
}

function refreshHoldLanguage(payload) {
  const duration = estimateReadingTime(payload.content || '', payload.actualLanguage || state.requestedLocale);
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
    ? (state.requestedLocale === 'en' ? 'Investigation Hub' : '调查中 (Investigation Hub)')
    : (state.requestedLocale === 'en' ? 'Make a Choice' : '请做出抉择');

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

    btn.innerHTML = `
      <div class="choice-btn-left">
        <span class="choice-marker">${escapeHtml(opt.marker || '*')}</span>
        <span class="choice-text">${escapeHtml(opt.label)}</span>
      </div>
      ${opt.isConsumed ? `<span class="choice-state-tag">${state.requestedLocale === 'en' ? '✓ Visited' : '✓ 已探索'}</span>` : ''}
    `;

    btn.addEventListener('click', (e) => {
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
    });

    listEl.appendChild(btn);
  });

  el.storyStream.appendChild(choiceGroup);
  scrollToBottom();

  el.dockStepHint.textContent = state.requestedLocale === 'en'
    ? 'Please choose an option (press 1, 2... or click)'
    : '请做出选择 (按数字键 1, 2... 或点击选项)';
}

// ==========================================================================
// Status Updates
// ==========================================================================
function updateDockStatus(status) {
  el.dockStatusText.textContent = status;

  if (status === 'Completed') {
    el.dockStatusDot.style.background = 'var(--accent-gold)';
    el.dockStatusDot.style.boxShadow = '0 0 6px var(--accent-gold)';
  } else if (status === 'AwaitingChoice') {
    el.dockStatusDot.style.background = 'var(--accent-blue)';
    el.dockStatusDot.style.boxShadow = '0 0 6px var(--accent-blue)';
  } else {
    el.dockStatusDot.style.background = 'var(--accent-green)';
    el.dockStatusDot.style.boxShadow = '0 0 6px var(--accent-green)';
  }
}

// Helpers
function reportSessionError(message) {
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
