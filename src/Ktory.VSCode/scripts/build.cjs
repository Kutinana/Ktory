const fs = require('node:fs');
const path = require('node:path');
const { spawnSync, execFileSync } = require('node:child_process');
const vsce = require('@vscode/vsce');
const { sourceRoot, repoRoot, extensionRoot, buildRoot } = require('./paths.cjs');

async function build() {
  const output = path.join(buildRoot, 'publish');
  const stagedExtension = path.join(buildRoot, 'extension');
  const reader = path.join(stagedExtension, 'reader');
  // Work from an empty publish directory so fingerprinted WASM files cannot accumulate.
  fs.rmSync(buildRoot, { recursive: true, force: true });
  try {
    const result = spawnSync('dotnet', ['publish', 'src/Ktory.Web', '-c', 'Release', '-o', output,
      '-m:1', '-nr:false', '-p:UseSharedCompilation=false'], { cwd: repoRoot, stdio: 'inherit' });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(`Reader build failed (${result.status ?? result.signal}).`);

    // The source .vscodeignore is the single file allowlist for the staged extension.
    const files = await vsce.listFiles({ cwd: sourceRoot, packageManager: vsce.PackageManager.None });
    for (const file of files) {
      if (file.startsWith('reader/')) continue; // Never reuse a legacy generated Reader.
      const target = path.join(stagedExtension, file);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.copyFileSync(path.join(sourceRoot, file), target);
    }
    fs.writeFileSync(path.join(stagedExtension, '.vscodeignore'),
      fs.readFileSync(path.join(sourceRoot, '.vscodeignore'), 'utf8') + '\n!reader/**\n');
    const manifestPath = path.join(stagedExtension, 'package.json');
    const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
    // Build tooling stays in src; the staged directory is a self-contained runtime.
    delete manifest.scripts;
    delete manifest.devDependencies;
    fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
    fs.cpSync(path.join(output, 'wwwroot'), reader, {
      recursive: true,
      filter: source => !/\.(br|gz|pdb)$/.test(source)
    });

    // Keep the runtime's redistribution notices with the offline binaries.
    const assets = JSON.parse(fs.readFileSync(path.join(repoRoot, 'artifacts/obj/Ktory.Web/project.assets.json'), 'utf8'));
    const packages = new Set(Object.values(assets.libraries).filter(item => item.type === 'package').map(item => item.path));
    for (const framework of Object.values(assets.project.frameworks)) {
      for (const dependency of framework.downloadDependencies || []) {
        const version = dependency.version.replace(/^[\[(]/, '').split(',')[0].trim();
        packages.add(`${dependency.name.toLowerCase()}/${version}`);
      }
    }
    for (const relative of packages) {
      const location = Object.keys(assets.packageFolders).map(folder => path.join(folder, relative)).find(folder => fs.existsSync(folder));
      if (!location) continue;
      for (const name of fs.readdirSync(location).filter(name => /^(license|third[-_]?party[-_]?notices)(\.|$)/i.test(name))) {
        const target = path.join(reader, 'notices', relative, name);
        fs.mkdirSync(path.dirname(target), { recursive: true });
        fs.copyFileSync(path.join(location, name), target);
      }
    }
    const git = (...args) => execFileSync('git', args, { cwd: repoRoot, encoding: 'utf8' }).trim();
    fs.writeFileSync(path.join(reader, 'build-info.json'), JSON.stringify({
      sourceCommit: git('rev-parse', 'HEAD'),
      dirty: Boolean(git('status', '--porcelain')),
      builtAt: new Date().toISOString()
    }, null, 2) + '\n');

    fs.rmSync(extensionRoot, { recursive: true, force: true });
    fs.renameSync(stagedExtension, extensionRoot);
    console.log(`Built the offline extension: ${extensionRoot}`);
  } finally {
    fs.rmSync(buildRoot, { recursive: true, force: true });
  }
}

module.exports = { build };
if (require.main === module) build().catch(error => { console.error(error); process.exitCode = 1; });
