const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
let extension = path.resolve(__dirname, '..');
const executable = process.env.VSCODE_EXECUTABLE_PATH || (process.platform === 'darwin'
  ? '/Applications/Visual Studio Code.app/Contents/MacOS/Electron' : undefined);
if (!executable || !fs.existsSync(executable)) throw new Error('Set VSCODE_EXECUTABLE_PATH to a VS Code executable.');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'ktory-vscode-test-'));
fs.mkdirSync(path.join(profile, 'user/User'), { recursive: true });
fs.writeFileSync(path.join(profile, 'user/User/settings.json'), JSON.stringify({
  'update.mode': 'none', 'telemetry.telemetryLevel': 'off', 'extensions.autoUpdate': false
}));
if (process.argv[2] === '--vsix') {
  const vsix = path.resolve(process.argv[3]);
  const cli = process.env.VSCODE_CLI_PATH || (process.platform === 'darwin'
    ? path.resolve(path.dirname(executable), '../Resources/app/bin/code') : executable);
  const install = spawnSync(cli, [
    '--install-extension', vsix, '--force', `--user-data-dir=${path.join(profile, 'user')}`,
    `--extensions-dir=${path.join(profile, 'extensions')}`
  ], { stdio: 'inherit', timeout: 60000 });
  if (install.error) throw install.error;
  if (install.status !== 0) process.exit(install.status || 1);
  const installed = fs.readdirSync(path.join(profile, 'extensions')).find(name => /^kutinana\.ktory-/.test(name));
  if (!installed) throw new Error('The VSIX did not install a Ktory extension.');
  extension = path.join(profile, 'extensions', installed);
}
const result = spawnSync(executable, [
  `--extensionDevelopmentPath=${extension}`, `--extensionTestsPath=${path.join(__dirname, 'integration.cjs')}`,
  `--user-data-dir=${path.join(profile, 'user')}`, `--extensions-dir=${path.join(profile, 'extensions')}`,
  '--disable-extensions', '--disable-workspace-trust', '--skip-welcome', '--skip-release-notes', '--new-window'
], { stdio: 'inherit', timeout: 180000, env: { ...process.env, ELECTRON_RUN_AS_NODE: '' } });
if (result.error) throw result.error;
process.exit(result.status ?? 1);
