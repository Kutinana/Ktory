const fs = require('node:fs');
const path = require('node:path');

function nextPatch(version, filenames) {
  if (!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(version)) throw new Error('Expected a numeric major.minor.patch version.');
  const [major, minor, patch] = version.split('.').map(Number);
  let latest = patch;
  for (const filename of filenames) {
    const match = /^ktory-vscode-(\d+)\.(\d+)\.(\d+)\.vsix$/.exec(filename);
    if (match && Number(match[1]) === major && Number(match[2]) === minor) latest = Math.max(latest, Number(match[3]));
  }
  if (!Number.isSafeInteger(latest + 1)) throw new Error('Patch version exceeds the safe integer range.');
  return `${major}.${minor}.${latest + 1}`;
}

async function packagePatch({ manifestPath, packagesRoot, packageExtension }) {
  fs.mkdirSync(packagesRoot, { recursive: true });
  const lockPath = path.join(packagesRoot, '.package-patch.lock');
  let lock;
  try { lock = fs.openSync(lockPath, 'wx'); }
  catch (error) {
    if (error.code === 'EEXIST') throw new Error(`Another patch build is running. If a previous process crashed, remove ${lockPath} after checking no build is active.`);
    throw error;
  }
  try {
    const original = fs.readFileSync(manifestPath, 'utf8');
    const manifest = JSON.parse(original);
    const version = nextPatch(manifest.version, fs.readdirSync(packagesRoot));
    const vsix = await packageExtension({ version });
    if (!fs.existsSync(vsix)) throw new Error('Packaging returned without producing a VSIX.');
    // Do not overwrite a developer's concurrent package.json edit.
    if (fs.readFileSync(manifestPath, 'utf8') !== original) throw new Error(`VSIX generated at ${vsix}, but package.json changed during the build. Version was not written back.`);
    manifest.version = version;
    fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
    return { version, vsix };
  } finally {
    fs.closeSync(lock);
    fs.unlinkSync(lockPath);
  }
}
module.exports = { nextPatch, packagePatch };
if (require.main === module) {
  const { sourceRoot, packagesRoot } = require('./paths.cjs');
  packagePatch({ manifestPath: path.join(sourceRoot, 'package.json'), packagesRoot,
    packageExtension: require('./package.cjs').packageExtension
  }).then(({ version, vsix }) => console.log(`Patch ${version} ready: ${vsix}`))
    .catch(error => { console.error(error); process.exitCode = 1; });
}
