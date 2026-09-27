/* global acquireVsCodeApi, Blazor, el, state, startSession,
   handleAdvanceAction, submitChoice, changeLanguage, restartSession, reportSessionError */
(() => {
  const MESSAGES = {
    'en': {
      scriptLocaleLabel: 'Script Language',
      scriptLocaleTitle: 'Switch story dialogue language (requested locale)',
      scriptLocaleAria: 'Script dialogue language',
      sourceButton: 'Script',
      sourceButtonTitle: 'Reveal source in editor',
      sourceButtonNamedTitle: 'Reveal source: {name}',
      reloadButton: 'Reload',
      reloadButtonTitle: 'Reload editor content (including unsaved edits)',
      statusOutdated: '{name} modified; preview is still reading version {version}. Click to reload.',
      errorTimeout: 'Core loading timed out. Please close and reopen the preview panel.',
      errorCoreLoad: 'Failed to load offline core: {reason}',
      errorResource: 'Failed to load resource: {uri}',
      errorCsp: 'Resource blocked by Content Security Policy: {directive} · {uri}',
      brandTitle: 'Ktory Preview',
      fastForward: 'Fast-forward',
      fastForwardTitle: 'Show full text immediately (F)',
      autoPlayTitle: 'Auto-advance mode (A)',
      restartSessionTitle: 'Restart from beginning (R)',
      replayStory: '↺ Restart Preview',
      completionTitle: 'Story sequence concluded',
      diagnosticSummary: 'Script Warnings ({count})',
      diagnosticSummaryZero: 'Script Warnings'
    },
    'zh-cn': {
      scriptLocaleLabel: '剧本语言',
      scriptLocaleTitle: '切换剧本对白语言（请求语言）',
      scriptLocaleAria: '剧本对白语言',
      sourceButton: '剧本',
      sourceButtonTitle: '返回源码',
      sourceButtonNamedTitle: '返回源码：{name}',
      reloadButton: '重新载入',
      reloadButtonTitle: '重新载入编辑器内容（包括未保存的修改）',
      statusOutdated: '{name} 已修改；当前仍在试读版本 {version}。点击重新载入。',
      errorTimeout: '核心加载超时，请关闭试读面板后重新打开。',
      errorCoreLoad: '无法加载离线核心：{reason}',
      errorResource: '资源加载失败：{uri}',
      errorCsp: '资源被内容策略拦截：{directive} · {uri}',
      brandTitle: 'Ktory Preview',
      fastForward: '快显',
      fastForwardTitle: '立即显示全文 (F)',
      autoPlayTitle: '自动阅读模式 (A)',
      restartSessionTitle: '重新从头开始试读 (R)',
      replayStory: '↺ 重新开始试读',
      completionTitle: '剧本演练结束',
      diagnosticSummary: '剧本警告（{count}）',
      diagnosticSummaryZero: '剧本警告'
    },
    'ja': {
      scriptLocaleLabel: '脚本言語',
      scriptLocaleTitle: 'シナリオの表示言語を切り替える（リクエスト言語）',
      scriptLocaleAria: 'シナリオの表示言語',
      sourceButton: 'スクリプト',
      sourceButtonTitle: 'エディタでソースを表示',
      sourceButtonNamedTitle: 'ソースを表示: {name}',
      reloadButton: '再読み込み',
      reloadButtonTitle: 'エディタの内容を再読み込み（未保存の変更を含む）',
      statusOutdated: '{name} が変更されました。現在もバージョン {version} を試読中です。クリックして再読み込み。',
      errorTimeout: 'コアの読み込みがタイムアウトしました。プレビューパネルを一度閉じて再度開いてください。',
      errorCoreLoad: 'オフラインコアの読み込みに失敗しました: {reason}',
      errorResource: 'リソースの読み込みに失敗しました: {uri}',
      errorCsp: 'コンテンツセキュリティポリシーによりリソースがブロックされました: {directive} · {uri}',
      brandTitle: 'Ktory Preview',
      fastForward: '早送り',
      fastForwardTitle: '全文を即時表示 (F)',
      autoPlayTitle: '自動送りモード (A)',
      restartSessionTitle: '最初からやり直す (R)',
      replayStory: '↺ 試読をやり直す',
      completionTitle: 'シナリオ再生終了',
      diagnosticSummary: 'スクリプト警告（{count}）',
      diagnosticSummaryZero: 'スクリプト警告'
    }
  };

  function normalizeUiLocale(locale) {
    if (!locale) return 'en';
    const lower = String(locale).toLowerCase();
    if (lower.startsWith('zh')) return 'zh-cn';
    if (lower.startsWith('ja')) return 'ja';
    return 'en';
  }

  function escapeHtml(value) {
    return String(value).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }
  const escapeAttribute = escapeHtml;

  const scriptTag = document.currentScript || document.querySelector('script[data-reader-root]');
  const readerRoot = scriptTag?.dataset?.readerRoot || '';
  let currentUiLocale = normalizeUiLocale(scriptTag?.dataset?.uiLocale || document.documentElement.lang || navigator.language);

  const vscode = acquireVsCodeApi();
  let currentDocument;
  let toolbar;
  let error;
  let language;
  let bootTimer;
  let isOutdated = false;
  let lastWarningCount = null;

  function t(key, params) {
    const dict = MESSAGES[currentUiLocale] || MESSAGES.en;
    let text = dict[key] || MESSAGES.en[key] || key;
    if (params) {
      for (const [k, v] of Object.entries(params)) {
        text = text.replaceAll(`{${k}}`, v);
      }
    }
    return text;
  }

  const post = data => vscode.postMessage({
    revision: currentDocument?.revision,
    version: currentDocument?.version,
    ...data
  });
  const originalConsoleError = console.error;
  console.error = (...args) => {
    post({ type: 'log', message: args.map(value => String(value)).join(' ') });
    originalConsoleError.apply(console, args);
  };

  function updateSourceButton() {
    const sourceBtn = document.getElementById('vscodeSource');
    if (sourceBtn) {
      const nameEl = sourceBtn.querySelector('.vscode-source-name');
      const dirtyEl = sourceBtn.querySelector('.vscode-source-dirty');
      if (!currentDocument) {
        if (nameEl) nameEl.textContent = t('sourceButton');
        sourceBtn.title = t('sourceButtonTitle');
        if (dirtyEl) dirtyEl.hidden = true;
      } else {
        if (nameEl) nameEl.textContent = currentDocument.name;
        sourceBtn.title = t('sourceButtonNamedTitle', { name: currentDocument.name });
        if (dirtyEl) dirtyEl.hidden = !currentDocument.dirty;
      }
    }
    const reloadBtn = document.getElementById('vscodeReload');
    if (reloadBtn) {
      if (isOutdated && currentDocument) {
        reloadBtn.classList.add('is-outdated');
        reloadBtn.title = t('statusOutdated', { name: currentDocument.name, version: currentDocument.version });
      } else {
        reloadBtn.classList.remove('is-outdated');
        reloadBtn.title = t('reloadButtonTitle');
      }
    }
  }

  function applyUiLocale() {
    document.documentElement.lang = currentUiLocale;
    const localeLabel = toolbar?.querySelector('.vscode-language label');
    if (localeLabel) {
      localeLabel.title = t('scriptLocaleTitle');
      const labelSpan = localeLabel.querySelector('span');
      if (labelSpan) labelSpan.textContent = t('scriptLocaleLabel');
    }
    if (language) {
      language.title = t('scriptLocaleTitle');
      language.setAttribute('aria-label', t('scriptLocaleAria'));
    }
    const reloadBtn = document.getElementById('vscodeReload');
    if (reloadBtn) {
      const labelSpan = reloadBtn.querySelector('.reload-label');
      if (labelSpan) labelSpan.textContent = t('reloadButton');
    }
    const brand = document.querySelector('.brand-title');
    if (brand) brand.textContent = t('brandTitle');

    // Reader UI overrides in webview
    const ffBtn = document.getElementById('btnFastForward');
    if (ffBtn) {
      ffBtn.textContent = t('fastForward');
      ffBtn.title = t('fastForwardTitle');
    }
    const autoBtn = document.getElementById('btnAutoPlay');
    if (autoBtn) autoBtn.title = t('autoPlayTitle');
    const restartBtn = document.getElementById('btnRestartSession');
    if (restartBtn) restartBtn.title = t('restartSessionTitle');
    const replayBtn = document.getElementById('btnReplayStory');
    if (replayBtn) replayBtn.textContent = t('replayStory');
    const compTitle = document.querySelector('.completion-title');
    if (compTitle) compTitle.textContent = t('completionTitle');
    if (el.runtimeDiagnosticSummary) {
      el.runtimeDiagnosticSummary.textContent = lastWarningCount > 0
        ? t('diagnosticSummary', { count: lastWarningCount })
        : t('diagnosticSummaryZero');
    }

    updateSourceButton();
  }

  function selectLanguage(locale) {
    if (!language) return;
    if (!Array.from(language.options).some(option => option.value === locale)) {
      language.add(new Option(locale, locale));
    }
    language.value = locale;
  }

  function updateLanguages(script) {
    if (!language) return;
    const locales = new Set(['zh', 'en', 'ja']);
    // Suggestions only; parsing, language selection and fallback remain in Core.
    for (const raw of script.split(/\r?\n/)) {
      const line = raw.replace(/"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\/\/.*$/g, '');
      const defaultLang = line.match(/^\s*@?defaultLang\s*[:=]\s*([\w-]+)/i);
      if (defaultLang) locales.add(defaultLang[1]);
      const variant = line.match(/^\s*(?:[*+]\s*)?\[?@([\w-]+)[:：]/);
      if (variant && !['speaker', 'defaultlang'].includes(variant[1].toLowerCase())) locales.add(variant[1]);
      const speaker = line.match(/^\s*@speaker(?:\s+[\w-]+)?\s*[:：](.*)$/);
      if (speaker) {
        for (const mapping of speaker[1].matchAll(/(?:^|\|)\s*([\w-]+)\s*=/g)) locales.add(mapping[1]);
      }
    }
    language.replaceChildren(...Array.from(locales, locale => new Option(locale, locale)));
    selectLanguage(state.requestedLocale || 'zh');
  }

  // Rich text is presentation data, never an executable document or resource request.
  function sanitizeHtml(html) {
    const source = new DOMParser().parseFromString(html, 'text/html');
    const allowed = new Set(['B', 'STRONG', 'I', 'EM', 'U', 'S', 'DEL', 'BR', 'RUBY', 'RT', 'RP', 'SPAN', 'SUB', 'SUP']);
    const discarded = new Set(['SCRIPT', 'STYLE', 'IFRAME', 'OBJECT', 'EMBED', 'SVG', 'MATH', 'LINK', 'META']);
    function copy(node, parent) {
      if (node.nodeType === Node.TEXT_NODE) { parent.appendChild(document.createTextNode(node.textContent)); return; }
      if (node.nodeType !== Node.ELEMENT_NODE || discarded.has(node.tagName)) return;
      const output = allowed.has(node.tagName) ? document.createElement(node.tagName.toLowerCase()) : parent;
      if (output !== parent) parent.appendChild(output);
      for (const child of node.childNodes) copy(child, output);
    }
    const result = document.createElement('div');
    for (const child of source.body.childNodes) copy(child, result);
    return result.innerHTML;
  }

  window.ktoryReaderHost = {
    sanitizeHtml,
    onDomReady() {
      document.body.classList.add('vscode-reader');
      if (el.langSwitcher) el.langSwitcher.style.display = 'none';

      toolbar = document.createElement('section');
      toolbar.className = 'vscode-toolbar';
      toolbar.addEventListener('keydown', event => event.stopPropagation());
      toolbar.innerHTML = `<div class="vscode-controls">
        <div class="vscode-controls-left">
          <button id="vscodeSource" type="button" title="${escapeAttribute(t('sourceButtonTitle'))}">
            <svg class="file-icon" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
              <polyline points="14 2 14 8 20 8"/>
            </svg>
            <span class="vscode-source-name">${escapeHtml(t('sourceButton'))}</span>
            <span class="vscode-source-dirty" hidden>●</span>
          </button>
          <div class="vscode-language">
            <label for="vscodeLocale" title="${escapeAttribute(t('scriptLocaleTitle'))}">
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <circle cx="12" cy="12" r="9"/><ellipse cx="12" cy="12" rx="4" ry="9"/><path d="M3 12h18"/>
              </svg>
              <span>${escapeHtml(t('scriptLocaleLabel'))}</span>
            </label>
            <select id="vscodeLocale" aria-label="${escapeAttribute(t('scriptLocaleAria'))}" title="${escapeAttribute(t('scriptLocaleTitle'))}" disabled>
              <option value="zh">zh</option><option value="en">en</option><option value="ja">ja</option>
            </select>
          </div>
        </div>
        <div class="vscode-controls-right">
          <button id="vscodeReload" type="button" title="${escapeAttribute(t('reloadButtonTitle'))}" disabled>
            <svg class="reload-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="M21.5 2v6h-6M2.5 22v-6h6M2 11.5a10 10 0 0 1 18.8-4.3M22 12.5a10 10 0 0 1-18.8 4.2"/>
            </svg>
            <span class="reload-label">${escapeHtml(t('reloadButton'))}</span>
          </button>
        </div>
      </div>
      <pre id="vscodeError" role="alert" hidden></pre>`;
      document.querySelector('.top-nav').after(toolbar);
      error = document.getElementById('vscodeError');
      language = document.getElementById('vscodeLocale');
      language.onchange = () => changeLanguage(language.value);
      document.getElementById('vscodeSource').onclick = () => post({ type: 'reveal' });
      document.getElementById('vscodeReload').onclick = () => post({ type: 'reload' });
      applyUiLocale();
      bootTimer = setTimeout(() => this.onError(t('errorTimeout')), 45000);
      Blazor.start({ loadBootResource: (type, name, uri) => {
        // Keep the webview's own base URI: .NET rejects the '+' in VS Code's
        // file-resource hostname if it is used as the application's base address.
        const filename = new URL(uri, document.baseURI).pathname.split('/').pop();
        return `${readerRoot}/_framework/${filename}`;
      } }).catch(reason => this.onError(t('errorCoreLoad', { reason: reason.message || reason })));
    },
    onRuntimeReady() {
      clearTimeout(bootTimer);
      document.getElementById('vscodeReload').disabled = false;
      if (language) language.disabled = false;
      post({ type: 'ready' });
    },
    onState(snapshot) {
      error.hidden = true;
      selectLanguage(snapshot.requestedLanguage || 'zh');
      if (snapshot.diagnostics) {
        lastWarningCount = snapshot.diagnostics.filter(item => item.kind === 256).length;
        if (el.runtimeDiagnosticSummary) {
          el.runtimeDiagnosticSummary.textContent = lastWarningCount > 0
            ? t('diagnosticSummary', { count: lastWarningCount })
            : t('diagnosticSummaryZero');
        }
      }
      post({ type: 'state', snapshot });
    },
    onError(message) {
      clearTimeout(bootTimer);
      if (error) { error.textContent = String(message); error.hidden = false; }
      post({ type: 'error', message: String(message) });
    }
  };

  window.addEventListener('message', async event => {
    const message = event.data;
    if (!message || typeof message.type !== 'string') return;
    if (message.type === 'document') {
      if (currentDocument && message.revision <= currentDocument.revision) return;
      currentDocument = message;
      isOutdated = false;
      error.hidden = true;
      el.scriptInput.value = message.text;
      document.dispatchEvent(new Event('ktory:source-changed'));
      updateLanguages(message.text);
      updateSourceButton();
      await startSession(message.text, state.requestedLocale || 'zh', null);
    } else if (message.type === 'changed' && currentDocument && message.version !== currentDocument.version) {
      isOutdated = true;
      updateSourceButton();
    } else if (message.type === 'ui-locale' && typeof message.locale === 'string') {
      currentUiLocale = normalizeUiLocale(message.locale);
      applyUiLocale();
    } else if (message.type === 'action' && currentDocument && message.revision === currentDocument.revision) {
      if (message.sessionId !== state.currentSessionId ||
          ((message.action === 'advance' || message.action === 'choice') &&
           message.presentationId !== state.currentPresentationId)) {
        console.debug('[Ktory] Ignored editor action for an expired session or presentation.');
        return;
      }
      // Editor commands share exactly the same presentation/input paths as the buttons.
      if (message.action === 'advance') await handleAdvanceAction();
      if (message.action === 'restart') await restartSession();
      if (message.action === 'language' && typeof message.locale === 'string') await changeLanguage(message.locale);
      if (message.action === 'choice' && typeof message.id === 'string') await submitChoice(message.id, message.presentationId, message.sessionId);
    }
  });
  window.addEventListener('unhandledrejection', event => window.ktoryReaderHost.onError(event.reason?.message || String(event.reason)));
  window.addEventListener('error', event => {
    const message = event.message || t('errorResource', { uri: event.target?.src || event.target?.href || 'unknown' });
    window.ktoryReaderHost.onError(message);
  }, true);
  window.addEventListener('securitypolicyviolation', event => {
    window.ktoryReaderHost.onError(t('errorCsp', { directive: event.violatedDirective, uri: event.blockedURI }));
  });
})();
