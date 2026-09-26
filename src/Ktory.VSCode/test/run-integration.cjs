const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const { publisher, name } = require('../package.json');
const { extensionRoot, testsRoot, packagePath } = require('../scripts/paths.cjs');
let extension = extensionRoot;
const executable = process.env.VSCODE_EXECUTABLE_PATH || (process.platform === 'darwin'
  ? '/Applications/Visual Studio Code.app/Contents/MacOS/Electron' : undefined);
if (!executable || !fs.existsSync(executable)) throw new Error('Set VSCODE_EXECUTABLE_PATH to a VS Code executable.');
if (process.argv[2] !== '--vsix' && !fs.existsSync(path.join(extensionRoot, 'reader/build-info.json'))) {
  throw new Error('Build the staged extension first with pnpm build.');
}
fs.mkdirSync(testsRoot, { recursive: true });
const profile = fs.mkdtempSync(path.join(testsRoot, 'run-'));
let succeeded = false;
try {
  fs.mkdirSync(path.join(profile, 'user/User'), { recursive: true });
  fs.writeFileSync(path.join(profile, 'user/User/settings.json'), JSON.stringify({
    'update.mode': 'none', 'telemetry.telemetryLevel': 'off', 'extensions.autoUpdate': false
  }));
  if (process.argv[2] === '--vsix') {
    const vsix = process.argv[3] ? path.resolve(process.argv[3]) : packagePath;
    const cli = process.env.VSCODE_CLI_PATH || (process.platform === 'darwin'
      ? path.resolve(path.dirname(executable), '../Resources/app/bin/code') : executable);
    const install = spawnSync(cli, [
      '--install-extension', vsix, '--force', `--user-data-dir=${path.join(profile, 'user')}`,
      `--extensions-dir=${path.join(profile, 'extensions')}`
    ], { stdio: 'inherit', timeout: 60000 });
    if (install.error) throw install.error;
    if (install.status !== 0) throw new Error(`VSIX installation failed (${install.status ?? install.signal}).`);
    const extensionPrefix = `${publisher}.${name}-`.toLowerCase();
    const installed = fs.readdirSync(path.join(profile, 'extensions')).find(directory => directory.toLowerCase().startsWith(extensionPrefix));
    if (!installed) throw new Error('The VSIX did not install a Ktory extension.');
    extension = path.join(profile, 'extensions', installed);
  }
  const result = spawnSync(executable, [
    `--extensionDevelopmentPath=${extension}`, `--extensionTestsPath=${path.join(__dirname, 'integration.cjs')}`,
    `--user-data-dir=${path.join(profile, 'user')}`, `--extensions-dir=${path.join(profile, 'extensions')}`,
    '--disable-extensions', '--disable-workspace-trust', '--skip-welcome', '--skip-release-notes', '--new-window'
  ], { stdio: 'inherit', timeout: 180000, env: { ...process.env, ELECTRON_RUN_AS_NODE: '' } });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`VS Code integration tests failed (${result.status ?? result.signal}).`);
  succeeded = true;
} finally {
  if (succeeded) {
    fs.rmSync(profile, { recursive: true, force: true });
    // Other runs or retained failure logs may still occupy this directory.
    try { fs.rmdirSync(testsRoot); } catch (error) { if (!['ENOTEMPTY', 'ENOENT'].includes(error.code)) throw error; }
  } else {
    console.error(`Test profile and logs retained: ${profile}`);
  }
}
