import { roles } from './theme.mjs';
// Rendering only. Lexical rules live in Ktory.VSCode/syntaxes/ktory.tmLanguage.json.
export function escapeHtml(value) {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
export async function createKtoryHighlighter(tm, onig, grammar) {
  const registry = new tm.Registry({
    onigLib: Promise.resolve({ createOnigScanner: patterns => new onig.OnigScanner(patterns), createOnigString: value => new onig.OnigString(value) }),
    loadGrammar: async scope => scope === grammar.scopeName ? grammar : null
  });
  const language = await registry.loadGrammar(grammar.scopeName);
  return {
    // Editor suggestions use the same lexical boundaries as visible highlighting.
    sourceLocales(source) {
      let stack = tm.INITIAL;
      const locales = new Set();
      for (const line of source.split(/\r?\n/)) {
        const result = language.tokenizeLine(line, stack);
        stack = result.ruleStack;
        for (const token of result.tokens) {
          if (token.scopes.includes('constant.other.locale.ktory') && !token.scopes.includes('string.quoted.raw.ktory'))
            locales.add(line.slice(token.startIndex, token.endIndex).replace(/^@/, ''));
        }
      }
      return [...locales];
    },
    highlightLines(source) {
      let stack = tm.INITIAL;
      return source.split(/\r?\n/).map(line => {
        const result = language.tokenizeLine(line, stack);
        stack = result.ruleStack;
        return result.tokens.map(token => {
          const value = escapeHtml(line.slice(token.startIndex, token.endIndex));
          const role = roles.find(role => role.scope.some(prefix => token.scopes.some(scope => scope === prefix || scope.startsWith(prefix + '.'))));
          return role ? `<span class="ktr-token ktr-${role.name}" style="--ktr-light:${role.light};--ktr-dark:${role.dark}">${value}</span>` : value;
        }).join('');
      });
    },
    dispose: () => registry.dispose()
  };
}
