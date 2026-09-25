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
  allowClickInterrupt: true,

  // Narrative Stream Data
  beatsCount: 0,
  visitedItems: [],
  recentTags: [],
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

  // Top Nav
  langSwitcher: document.getElementById('langSwitcher'),
  btnAutoPlay: document.getElementById('btnAutoPlay'),
  btnFontToggle: document.getElementById('btnFontToggle'),
  fontToggleLabel: document.getElementById('fontToggleLabel'),
  btnFontSizeToggle: document.getElementById('btnFontSizeToggle'),
  fontSizeLabel: document.getElementById('fontSizeLabel'),
  btnToggleEditor: document.getElementById('btnToggleEditor'),
  btnRestartSession: document.getElementById('btnRestartSession'),

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

// ==========================================================================
// Initialization
// ==========================================================================
document.addEventListener('DOMContentLoaded', async () => {
  setupEventListeners();
  setupDrawerResizer();
  await loadSamples();

  // Start with first catalog sample
  const sampleKeys = Object.keys(state.samples);
  if (sampleKeys.length > 0) {
    state.currentSampleKey = sampleKeys[0];
    el.scriptInput.value = state.samples[state.currentSampleKey];
    updateOverviewStoryInfo(state.currentSampleKey);
    await startSession(state.samples[state.currentSampleKey], 'zh');
  }
});

function setupEventListeners() {
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
          submitChoice(option.id);
        }
      }
    }
  });

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
      updateOverviewStoryInfo(selected);
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
        }

        updateOverviewStoryInfo(title);
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

function toggleAutoPlay() {
  state.autoPlay = !state.autoPlay;
  el.btnAutoPlay.classList.toggle('active', state.autoPlay);

  if (state.autoPlay && state.status === 'SuspendedAtBeat' && !state.isTyping && !state.isHolding) {
    stepSession();
  }
}

// ==========================================================================
// API Interaction
// ==========================================================================
async function loadSamples() {
  try {
    const res = await fetch('/api/samples');
    if (res.ok) {
      state.samples = await res.json();

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
      }
    }
  } catch (err) {
    console.error('Failed to load samples:', err);
  }
}

async function startSession(script, requestedLocale = 'zh', entryBlock = null) {
  clearTimers();
  resetStoryStream();

  // If entryBlock not provided, auto-detect first scene if script has one
  if (!entryBlock) {
    const match = script.match(/===\s*([a-zA-Z0-9_]+)\s*===/);
    if (match) {
      entryBlock = match[1];
    }
  }

  try {
    const res = await fetch('/api/session/start', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ script, requestedLocale, entryBlock })
    });
    if (res.ok) {
      const data = await res.json();
      updateState(data);
    } else {
      const err = await res.json();
      alert(`解析错误: ${err.error}`);
    }
  } catch (err) {
    console.error('Failed to start session:', err);
  }
}

async function stepSession() {
  if (state.status === 'Completed' || state.status === 'AwaitingChoice') return;
  clearTimers();

  try {
    const res = await fetch('/api/session/step', { method: 'POST' });
    if (res.ok) {
      const data = await res.json();
      updateState(data);
    }
  } catch (err) {
    console.error('Failed to step session:', err);
  }
}

async function submitChoice(choiceId) {
  clearTimers();
  if (state.currentActiveChoiceEl) {
    state.currentActiveChoiceEl.classList.add('has-selection');
    state.currentActiveChoiceEl = null;
  }
  try {
    const res = await fetch('/api/session/choice', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ choiceId })
    });
    if (res.ok) {
      const data = await res.json();
      updateState(data);
    }
  } catch (err) {
    console.error('Failed to submit choice:', err);
  }
}

async function changeLanguage(locale) {
  state.requestedLocale = locale;
  el.langSwitcher.querySelectorAll('.pill-btn').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.locale === locale);
  });

  try {
    const res = await fetch('/api/session/language', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ locale })
    });
    if (res.ok) {
      const data = await res.json();
      updateState(data, true);
    }
  } catch (err) {
    console.error('Failed to change language:', err);
  }
}

async function restartSession() {
  const script = el.scriptInput.value || (state.samples[state.currentSampleKey] || '');
  if (script) {
    await startSession(script, state.requestedLocale);
  }
}

// ==========================================================================
// Narrative Stream & State Management
// ==========================================================================
function resetStoryStream() {
  el.storyStream.innerHTML = '';
  el.storyCompletedBanner.style.display = 'none';
  state.beatsCount = 0;
  state.currentActivePassageEl = null;
  state.currentActiveSpeakerEl = null;
  state.currentActiveTypingEl = null;
  state.currentActiveCursorEl = null;
}

function updateOverviewStoryInfo(title) {
  if (el.sampleSelect) el.sampleSelect.value = title;
  document.title = `${title} — Ktory Web Reader`;
}

function updateState(serverState, isLanguageSwitch = false) {
  state.status = serverState.status;
  state.payload = serverState.payload;
  state.choice = serverState.choice;
  state.requestedLocale = serverState.requestedLanguage;
  state.defaultLocale = serverState.defaultLanguage;
  state.visitedItems = serverState.visitedItems || [];
  state.callStackDepth = serverState.callStackDepth || 0;
  state.recentTags = serverState.recentTags || [];

  if (el.dockLocaleTag) el.dockLocaleTag.textContent = (serverState.payload?.actualLanguage || state.requestedLocale || 'ZH').toUpperCase();

  updateDockStatus(state.status);

  // If Completed
  if (state.status === 'Completed') {
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

function renderBeat(payload, isLanguageSwitch = false) {
  // Hot language switch: update current typing/active text and speaker if applicable
  if (isLanguageSwitch && state.currentActiveTypingEl) {
    // 1. Immediately abort typewriter timer and fast-forward lock timer
    clearInterval(state.typewriterTimer);
    clearTimeout(state.fastForwardLockTimer);
    state.typewriterTimer = null;
    state.fastForwardLockTimer = null;
    state.isTyping = false;

    // 2. Hide typing cursor
    if (state.currentActiveCursorEl) {
      state.currentActiveCursorEl.style.display = 'none';
    }

    // 3. Dynamically update speaker display name in DOM
    if (state.currentActiveSpeakerEl) {
      state.currentActiveSpeakerEl.textContent = payload.speaker || '';
    } else if (payload.speaker && state.currentActivePassageEl) {
      const spEl = state.currentActivePassageEl.querySelector('.speaker-name');
      if (spEl) spEl.textContent = payload.speaker;
    }

    // 4. Update the text content
    state.fullTextHtml = payload.content;
    state.currentActiveTypingEl.innerHTML = payload.content;

    // 5. Update prompt hint if not holding
    if (!state.isHolding) {
      el.dockStepHint.textContent = '点击页面或按 [空格] 推进阅读 ▾';
    }

    scrollToBottom();
    return;
  }

  state.beatsCount++;

  // 1. Directives beat (#do, .bg, .sfx, etc.)
  if (payload.stepType === 1 || payload.stepType === 'Directive') {
    renderDirectiveBeat(payload);
    return;
  }

  // 2. Standard Prose / Dialogue Beat
  const passage = document.createElement('div');
  passage.className = 'passage-item';

  const tags = payload.tags || [];
  state.activeTags = tags;

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
  const passage = document.createElement('div');
  passage.className = 'passage-item passage-directive';

  const tagsDesc = (payload.tags || [])
    .map(t => {
      const args = t.positionalArgs.map(a => JSON.stringify(a)).join(', ');
      return `.${t.name}(${args})`;
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

  cursorEl.style.display = 'inline-block';
  el.dockStepHint.textContent = '文字呈现中... 点击可快速显示全文';

  // Parse .skippable(false, duration)
  state.canFastForward = true;
  const skippableTag = tags.find(t => t.name.toLowerCase() === 'skippable');
  if (skippableTag) {
    const allowed = skippableTag.positionalArgs.length > 0 ? skippableTag.positionalArgs[0] : true;
    if (allowed === false) {
      state.canFastForward = false;
      const duration = skippableTag.positionalArgs.length > 1 ? Number(skippableTag.positionalArgs[1]) : 2;
      el.dockStepHint.textContent = `快显锁定中 (${duration}s)...`;
      state.fastForwardLockTimer = setTimeout(() => {
        state.canFastForward = true;
        el.dockStepHint.textContent = '点击可快速显示全文';
      }, duration * 1000);
    }
  }

  const tokens = tokenizeHtml(htmlContent);
  let tokenIdx = 0;
  targetEl.innerHTML = '';

  const speedMs = 18; // literary typewriter speed
  state.typewriterTimer = setInterval(() => {
    if (tokenIdx >= tokens.length) {
      finishTypewriter(cursorEl, tags);
      return;
    }

    targetEl.innerHTML += tokens[tokenIdx++];
    scrollToBottom();
  }, speedMs);
}

function fastForwardTypewriter() {
  if (!state.isTyping || !state.canFastForward) return;
  clearInterval(state.typewriterTimer);
  clearTimeout(state.fastForwardLockTimer);

  if (state.currentActiveTypingEl) {
    state.currentActiveTypingEl.innerHTML = state.fullTextHtml;
  }
  finishTypewriter(state.currentActiveCursorEl, state.activeTags);
  scrollToBottom();
}

function finishTypewriter(cursorEl, tags) {
  clearInterval(state.typewriterTimer);
  state.isTyping = false;
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

// ==========================================================================
// Hold & Auto-Advance Timer (.next / .wait)
// ==========================================================================
function setupHoldTimer(tags, defaultHoldSec = 0) {
  const nextTag = tags.find(t => t.name.toLowerCase() === 'next');
  const waitTag = tags.find(t => t.name.toLowerCase() === 'wait');

  let holdSec = defaultHoldSec;
  state.allowClickInterrupt = true;
  let hasAutoTimer = false;

  if (nextTag) {
    hasAutoTimer = true;
    state.allowClickInterrupt = true; // .next(t): clicking immediately skips hold
    holdSec = nextTag.positionalArgs.length > 0 ? Number(nextTag.positionalArgs[0]) : 0;
  } else if (waitTag) {
    hasAutoTimer = true;
    state.allowClickInterrupt = false; // .wait(t): cannot skip early
    holdSec = waitTag.positionalArgs.length > 0 ? Number(waitTag.positionalArgs[0]) : 2.5;
  } else if (state.autoPlay) {
    hasAutoTimer = true;
    state.allowClickInterrupt = true;
    holdSec = 2.2;
  }

  if (hasAutoTimer && holdSec > 0) {
    state.isHolding = true;
    state.holdDuration = holdSec;
    state.holdElapsed = 0;

    el.dockProgressBar.style.width = '0%';
    el.dockStepHint.textContent = state.allowClickInterrupt
      ? `自动推进倒计时 (${holdSec.toFixed(1)}s)... 点击即刻推进`
      : `强制停留中 (${holdSec.toFixed(1)}s)...`;

    const intervalMs = 25;
    state.holdTimer = setInterval(() => {
      state.holdElapsed += intervalMs / 1000;
      const progress = Math.min(100, (state.holdElapsed / Math.max(0.1, state.holdDuration)) * 100);
      el.dockProgressBar.style.width = `${progress}%`;

      if (state.holdElapsed >= state.holdDuration) {
        clearInterval(state.holdTimer);
        state.isHolding = false;
        el.dockProgressBar.style.width = '0%';
        stepSession();
      }
    }, intervalMs);
  } else {
    // Normal manual step
    state.isHolding = false;
    el.dockProgressBar.style.width = '0%';
    el.dockStepHint.textContent = '点击页面或按 [空格] 推进阅读 ▾';
  }
}

// Stage / Viewport Advance Trigger
function handleAdvanceAction() {
  if (state.status === 'AwaitingChoice') {
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
      clearTimers();
      state.isHolding = false;
      el.dockProgressBar.style.width = '0%';
      stepSession();
    } else {
      el.dockStepHint.textContent = '强制停留中，请稍候...';
    }
    return;
  }

  // 3. Normal step
  stepSession();
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
      btn.classList.add('is-selected');
      choiceGroup.classList.add('has-selection');
      state.currentActiveChoiceEl = null;
      submitChoice(opt.id);
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
