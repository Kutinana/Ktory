import { createKtoryHighlighter } from './highlighting/renderer.mjs';

const input = document.getElementById('scriptInput');
const output = document.getElementById('scriptHighlight');
if (input && output) {
  try {
    const base = new URL('./highlighting/', import.meta.url);
    const [grammar, wasm] = await Promise.all([
      fetch(new URL('ktory.tmLanguage.json', base)).then(r => { if (!r.ok) throw new Error('Grammar unavailable'); return r.json(); }),
      fetch(new URL('onig.wasm', base)).then(r => { if (!r.ok) throw new Error('Tokenizer unavailable'); return r.arrayBuffer(); })
    ]);
    await window.onig.loadWASM(wasm);
    const highlighter = await createKtoryHighlighter(window.vscodetextmate, window.onig, grammar);
    const syncScroll = () => { output.scrollTop = input.scrollTop; output.scrollLeft = input.scrollLeft; };
    let pending = false;
    const render = () => {
      if (pending) return;
      pending = true;
      requestAnimationFrame(() => {
        pending = false;
        output.innerHTML = highlighter.highlightLines(input.value).join('\n') + '\n';
        syncScroll();
      });
    };
    input.addEventListener('input', render);
    input.addEventListener('scroll', syncScroll);
    document.addEventListener('ktory:source-changed', render);
    input.parentElement.classList.add('highlight-ready');
    render();
    window.addEventListener('pagehide', () => highlighter.dispose(), { once: true });
  } catch (error) {
    // Keep the textarea readable and editable when highlighting is unavailable.
    console.warn('[Ktory] Source highlighting unavailable:', error.message);
  }
}
