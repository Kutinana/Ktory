const fs = require('node:fs');
const path = require('node:path');
const { build } = require('./build.cjs');
const { extensionRoot, packagesRoot, packagePath } = require('./paths.cjs');
module.exports = { packagePath };

async function packageExtension() {
  await build();
  fs.mkdirSync(packagesRoot, { recursive: true });
  const temporaryPackage = path.join(packagesRoot, `.${path.basename(packagePath)}`);
  try {
    await require('@vscode/vsce').createVSIX({
      cwd: extensionRoot,
      packagePath: temporaryPackage,
      dependencies: false,
      preRelease: true
    });
    fs.renameSync(temporaryPackage, packagePath);
    console.log(`VSIX ready: ${packagePath}`);
  } finally {
    fs.rmSync(temporaryPackage, { force: true });
  }
}

if (require.main === module) packageExtension().catch(error => { console.error(error); process.exitCode = 1; });
