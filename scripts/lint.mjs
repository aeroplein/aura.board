import { spawnSync } from "node:child_process";
import { readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

const roots = ["src", "scripts"];
const files = ["vite.config.js"];
const extensions = new Set([".js", ".mjs", ".cjs"]);

function collectJsFiles(dir) {
  for (const entry of readdirSync(dir)) {
    const fullPath = join(dir, entry);
    const stats = statSync(fullPath);

    if (stats.isDirectory()) {
      collectJsFiles(fullPath);
      continue;
    }

    if (extensions.has(fullPath.slice(fullPath.lastIndexOf(".")))) {
      files.push(fullPath);
    }
  }
}

for (const root of roots) {
  collectJsFiles(root);
}

let failed = false;

for (const file of files) {
  const displayPath = relative(process.cwd(), file);
  const result = spawnSync(process.execPath, ["--check", file], { stdio: "inherit" });

  if (result.status !== 0) {
    failed = true;
    console.error(`Syntax check failed: ${displayPath}`);
  }
}

if (failed) {
  process.exit(1);
}

console.log(`Checked ${files.length} JavaScript files.`);
