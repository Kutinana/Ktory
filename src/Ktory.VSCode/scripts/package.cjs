const fs = require('node:fs');
const path = require('node:path');
const { build } = require('./build.cjs');
const { extensionRoot, packagesRoot, packagePath: defaultPackagePath } = require('./paths.cjs');
module.exports = { packagePath: defaultPackagePath, packageExtension };

async function packageExtension({ version } = {}) {
  if (version && !/^\d+\.\d+\.\d+$/.test(version)) throw new Error("Invalid extension version.");
  const packagePath = version ? path.join(packagesRoot, `ktory-vscode-${version}.vsix`) : defaultPackagePath;
  await build({ version });
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
    return packagePath;
  } finally {
    fs.rmSync(temporaryPackage, { force: true });
  }
}

if (require.main === module) packageExtension().catch(error => { console.error(error); process.exitCode = 1; });
