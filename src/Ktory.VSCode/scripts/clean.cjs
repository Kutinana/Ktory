const fs = require('node:fs');
const { artifactsRoot } = require('./paths.cjs');

// Only extension outputs; keep shared .NET caches and the standalone Web Reader.
fs.rmSync(artifactsRoot, { recursive: true, force: true });
console.log(`Cleaned VS Code extension outputs: ${artifactsRoot}`);
