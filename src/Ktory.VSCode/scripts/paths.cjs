const path = require('node:path');
const { version } = require('../package.json');

const sourceRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(sourceRoot, '../..');
const artifactsRoot = path.join(repoRoot, 'artifacts/vscode');
const extensionRoot = path.join(artifactsRoot, 'extension');
const packagesRoot = path.join(artifactsRoot, 'packages');

module.exports = {
  sourceRoot,
  repoRoot,
  artifactsRoot,
  extensionRoot,
  readerRoot: path.join(extensionRoot, 'reader'),
  buildRoot: path.join(artifactsRoot, '.build'),
  testsRoot: path.join(artifactsRoot, 'tests'),
  packagesRoot,
  packagePath: path.join(packagesRoot, `ktory-vscode-${version}.vsix`)
};
