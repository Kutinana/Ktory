/* global acquireVsCodeApi, Blazor, el, state, updateSectionSelector, startSession,
   handleAdvanceAction, submitChoice, changeLanguage, restartSession, reportSessionError */
(() => {
  const readerRoot = document.currentScript.dataset.readerRoot;
  const vscode = acquireVsCodeApi();
  let currentDocument;
  let toolbar;
  let status;
  let error;
  let entry;
  let language;
  let bootTimer;
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

  function selectLanguage(locale) {
    if (!Array.from(language.options).some(option => option.value === locale)) {
      language.add(new Option(locale, locale));
    }
    language.value = locale;
  }

  function updateLanguages(script) {
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
      el.langSwitcher.className = 'vscode-language';
      el.langSwitcher.innerHTML = `<label for="vscodeLocale" title="请求语言">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">
          <circle cx="12" cy="12" r="9"/><ellipse cx="12" cy="12" rx="4" ry="9"/><path d="M3 12h18"/>
        </svg>
      </label><select id="vscodeLocale" aria-label="请求语言" title="选择后立即切换语言" disabled>
        <option value="zh">zh</option><option value="en">en</option><option value="ja">ja</option>
      </select>`;
      language = document.getElementById('vscodeLocale');
      language.onchange = () => changeLanguage(language.value);
      toolbar = document.createElement('section');
      toolbar.className = 'vscode-toolbar';
      toolbar.addEventListener('keydown', event => event.stopPropagation());
      toolbar.innerHTML = `<div class="vscode-controls">
        <button id="vscodeSource" type="button" title="返回源码">剧本</button>
        <button id="vscodeReload" type="button" title="重新载入编辑器内容（包括未保存的修改）" disabled>重新载入</button>
        <div class="vscode-entry"><label for="vscodeEntry">入口</label><select id="vscodeEntry" disabled><option value="">默认入口</option></select></div>
      </div><p id="vscodeStatus" role="status">正在加载离线试读核心…</p>
      <p class="vscode-note">试读忽略外部选项条件并跳过未知演出，不模拟游戏状态。修改后重新载入会开始新会话。</p>
      <pre id="vscodeError" role="alert" hidden></pre>`;
      document.querySelector('.top-nav').after(toolbar);
      status = document.getElementById('vscodeStatus');
      error = document.getElementById('vscodeError');
      entry = document.getElementById('vscodeEntry');
      document.getElementById('vscodeSource').onclick = () => post({ type: 'reveal' });
      document.getElementById('vscodeReload').onclick = () => post({ type: 'reload' });
      entry.onchange = () => {
        el.sectionSelect.value = entry.value;
        error.hidden = true;
        restartSession();
      };
      document.querySelector('.brand-title').textContent = 'Ktory Preview';
      bootTimer = setTimeout(() => this.onError('核心加载超时，请关闭试读面板后重新打开。'), 45000);
      Blazor.start({ loadBootResource: (type, name, uri) => {
        // Keep the webview's own base URI: .NET rejects the '+' in VS Code's
        // file-resource hostname if it is used as the application's base address.
        const filename = new URL(uri, document.baseURI).pathname.split('/').pop();
        return `${readerRoot}/_framework/${filename}`;
      } }).catch(reason => this.onError(`无法加载离线核心：${reason.message || reason}`));
    },
    onRuntimeReady() {
      clearTimeout(bootTimer);
      document.getElementById('vscodeReload').disabled = false;
      entry.disabled = false;
      language.disabled = false;
      post({ type: 'ready' });
    },
    onState(snapshot) {
      error.hidden = true;
      selectLanguage(snapshot.requestedLanguage || 'zh');
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
      const changedFile = currentDocument?.uri !== message.uri;
      currentDocument = message;
      error.hidden = true;
      if (changedFile) el.sectionSelect.value = '';
      el.scriptInput.value = message.text;
      updateLanguages(message.text);
      updateSectionSelector(message.text);
      entry.replaceChildren(...Array.from(el.sectionSelect.options, option => option.cloneNode(true)));
      entry.value = el.sectionSelect.value;
      status.textContent = `${message.name} · 编辑版本 ${message.version}${message.dirty ? '（未保存）' : ''}`;
      document.getElementById('vscodeSource').textContent = message.name;
      document.getElementById('vscodeSource').title = `返回源码：${message.name}`;
      await startSession(message.text, state.requestedLocale || 'zh', entry.value);
    } else if (message.type === 'changed' && currentDocument && message.version !== currentDocument.version) {
      status.textContent = `${currentDocument.name} 已修改；当前仍在试读版本 ${currentDocument.version}。请重新载入。`;
    } else if (message.type === 'action' && currentDocument && message.revision === currentDocument.revision) {
      // Editor commands share exactly the same presentation/input paths as the buttons.
      if (message.action === 'advance') await handleAdvanceAction();
      if (message.action === 'restart') await restartSession();
      if (message.action === 'language' && typeof message.locale === 'string') await changeLanguage(message.locale);
      if (message.action === 'choice' && typeof message.id === 'string') await submitChoice(message.id, message.presentationId);
    }
  });
  window.addEventListener('unhandledrejection', event => window.ktoryReaderHost.onError(event.reason?.message || String(event.reason)));
  window.addEventListener('error', event => {
    const message = event.message || `资源加载失败：${event.target?.src || event.target?.href || 'unknown'}`;
    window.ktoryReaderHost.onError(message);
  }, true);
  window.addEventListener('securitypolicyviolation', event => {
    window.ktoryReaderHost.onError(`资源被内容策略拦截：${event.violatedDirective} · ${event.blockedURI}`);
  });
})();
