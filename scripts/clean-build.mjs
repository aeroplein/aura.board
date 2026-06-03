import { rmSync } from 'node:fs';

const generatedPaths = ['wwwroot', 'dist', 'bin', 'obj'];

for (const path of generatedPaths) {
  rmSync(path, { recursive: true, force: true });
  console.log(`Removed ${path}`);
}

console.log('Preserved data/ and data/uploads/.');
