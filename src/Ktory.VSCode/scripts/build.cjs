const fs = require('node:fs');
const path = require('node:path');
const { spawnSync, execFileSync } = require('node:child_process');

const extensionRoot = path.resolve(__dirname, '..');
const root = path.resolve(extensionRoot, '../..');
const output = path.join(root, 'artifacts/vscode-reader');
// Publishing in place retains obsolete fingerprinted assets; start with an empty output.
fs.rmSync(output, { recursive: true, force: true });
const result = spawnSync('dotnet', ['publish', 'src/Ktory.Web', '-c', 'Release', '-o', output,
  '-m:1', '-nr:false', '-p:UseSharedCompilation=false'], { cwd: root, stdio: 'inherit' });
if (result.error) throw result.error;
if (result.status !== 0) process.exit(result.status || 1);

const reader = path.join(extensionRoot, 'reader');
fs.rmSync(reader, { recursive: true, force: true });
fs.cpSync(path.join(output, 'wwwroot'), reader, {
  recursive: true,
  filter: source => !/\.(br|gz|pdb)$/.test(source)
});
// Keep the runtime's redistribution notices with the offline binaries.
const assets = JSON.parse(fs.readFileSync(path.join(root, 'artifacts/obj/Ktory.Web/project.assets.json'), 'utf8'));
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
const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
fs.writeFileSync(path.join(reader, 'build-info.json'), JSON.stringify({
  sourceCommit: git('rev-parse', 'HEAD'),
  dirty: Boolean(git('status', '--porcelain')),
  builtAt: new Date().toISOString()
}, null, 2) + '\n');
console.log('Bundled the shared Ktory.Web runtime and Reader assets.');
