const fs = require('node:fs');
const path = require('node:path');
const { version } = require('../package.json');

// One version source and one path shared by packaging and installed-VSIX tests.
const extensionRoot = path.resolve(__dirname, '..');
const packagePath = path.resolve(extensionRoot, '../../artifacts/vscode', `ktory-vscode-${version}.vsix`);
module.exports = { packagePath };

if (require.main === module) {
  fs.mkdirSync(path.dirname(packagePath), { recursive: true });
  require('@vscode/vsce').createVSIX({
    cwd: extensionRoot,
    packagePath,
    dependencies: false,
    skipLicense: true
  }).catch(error => {
    console.error(error);
    process.exitCode = 1;
  });
}
