// One palette for the portal and Reader; VS Code applies its user's theme to these scopes.
export const roles = [
  { name: 'comment', scope: ['comment'], light: '#657464', dark: '#809578' },
  { name: 'metadata', scope: ['variable.language.metadata'], light: '#246b91', dark: '#9cdcfe' },
  { name: 'declaration', scope: ['keyword.declaration'], light: '#8545a2', dark: '#c586c0' },
  { name: 'speaker', scope: ['entity.name.type.speaker'], light: '#157b6e', dark: '#4ec9b0' },
  { name: 'locale', scope: ['constant.other.locale'], light: '#91661c', dark: '#d7ba7d' },
  { name: 'decorator', scope: ['entity.name.function.decorator'], light: '#866c17', dark: '#dcdcaa' },
  { name: 'string', scope: ['string'], light: '#9f542d', dark: '#ce9178' },
  { name: 'keyword', scope: ['keyword'], light: '#245fad', dark: '#569cd6' }
];
export const themes = ['light', 'dark'].map(type => ({
  name: `ktory-${type}`, type,
  colors: { 'editor.background': type === 'dark' ? '#101216' : '#f8f8fa', 'editor.foreground': type === 'dark' ? '#d4d4d4' : '#30343b' },
  tokenColors: roles.map(role => ({ scope: role.scope, settings: { foreground: role[type] } }))
}));
