#!/usr/bin/env node
// Thin launcher: finds the platform package that carries AIThread.exe and runs it with the same arguments and console.
"use strict";

const { spawn } = require("node:child_process");
const path = require("node:path");

const PLATFORMS = { "win32-x64": "aithread-win-x64" };
const key = `${process.platform}-${process.arch}`;
const packageName = PLATFORMS[key];

if (!packageName) {
  console.error(`aithread: platform ${key} belum didukung (hanya ${Object.keys(PLATFORMS).join(", ")}).`);
  process.exit(1);
}

let exe;
try {
  exe = path.join(path.dirname(require.resolve(`${packageName}/package.json`)), "bin", "AIThread.exe");
} catch {
  console.error(`aithread: paket ${packageName} tidak ditemukan. Pasang ulang tanpa --no-optional / --omit=optional:`);
  console.error("  npm i -g aithread");
  process.exit(1);
}

const child = spawn(exe, process.argv.slice(2), { stdio: "inherit" });

// Ctrl+C reaches the exe through the shared console; keep this process alive so the exe can shut down cleanly.
process.on("SIGINT", () => {});
process.on("SIGTERM", () => child.kill());

child.on("error", (error) => {
  console.error(`aithread: gagal menjalankan ${exe}: ${error.message}`);
  process.exit(1);
});
child.on("exit", (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
