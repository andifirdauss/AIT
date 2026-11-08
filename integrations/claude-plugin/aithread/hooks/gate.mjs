#!/usr/bin/env node
// The hook AIT gives a coding CLI. Before a prompt: tells the model where AIT stands for this folder. Before a file edit:
// refuses it inside a project AIT is ready for, so the change goes through AIT.
// One script for every CLI, recognized by its event names: UserPromptSubmit / PreToolUse (Claude Code, Codex, Qwen Code)
// and BeforeAgent / BeforeTool (Gemini CLI). opencode and oh-my-pi have in-process plugins, which call this script with
// the first pair (integrations/adapters).
// It always fails open: whatever goes wrong here, the developer's session keeps working. The text is read by the model,
// and is written as plain facts, as the hook documentation advises.
// "gate.mjs decline" records that the developer said no to installing AIT.
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";

const HOME = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), "AppData", "Local"), "AIThread");
const SETTINGS_FILE = path.join(HOME, "skill.json");
const SKILL = "aithread-orchestrator";
const GATE_TIMEOUT_MS = 1500;
const MANIFESTS = ["package.json", "go.mod", "Cargo.toml", "pom.xml", "build.gradle", "build.gradle.kts", "pyproject.toml", "composer.json"];
// Claude Code's own files and AIT's project settings are not the code AIT works on.
const OWN_FOLDERS = new Set([".claude", ".codex", ".gemini", ".qwen", ".opencode", ".omp", ".agents", ".aithread"]);
const PROMPT_EVENTS = new Set(["UserPromptSubmit", "BeforeAgent"]);
const EDIT_EVENTS = new Set(["PreToolUse", "BeforeTool"]);
const PROXY_VARIABLES = ["ANTHROPIC_BASE_URL", "OPENAI_BASE_URL", "GOOGLE_GEMINI_BASE_URL"];
// Codex and opencode edit through one patch, whose target files are named on marker lines.
const PATCH_TARGET = /^\*\*\* (?:Add File|Update File|Delete File|Move to): (.+?)\s*$/gm;

const readJson = (file) => {
  try {
    return JSON.parse(readFileSync(file, "utf8").replace(/^﻿/, ""));
  } catch {
    return null;
  }
};

const settings = () => readJson(SETTINGS_FILE) ?? {};

const aitPort = () => {
  const port = readJson(path.join(HOME, "cli.json"))?.Port;
  return Number.isInteger(port) && port > 0 && port <= 65535 ? port : 5225;
};

const normalize = (folder) => path.resolve(folder).replace(/[\\/]+$/, "").toLowerCase();

const isInside = (folder, root) => {
  const inner = normalize(folder);
  const outer = normalize(root);
  return inner === outer || inner.startsWith(outer + path.sep);
};

const isDisabled = (config, ...folders) =>
  (config.disabledRoots ?? []).some((root) => folders.some((folder) => folder && isInside(folder, root)));

// The session already goes through AIT's proxy: every prompt is orchestrated there.
const isProxied = (port) =>
  PROXY_VARIABLES.some((name) => {
    try {
      const url = new URL(process.env[name] ?? "");
      const host = url.hostname.replace(/^\[|\]$/g, "");
      return ["localhost", "127.0.0.1", "::1"].includes(host) && Number(url.port) === port;
    } catch {
      return false;
    }
  });

const isInstalled = () => {
  const folders = (process.env.PATH ?? "").split(path.delimiter).filter(Boolean);
  if (process.env.APPDATA) folders.push(path.join(process.env.APPDATA, "npm"));
  return folders.some((folder) => ["aithread.cmd", "aithread.exe", "aithread"].some((name) => existsSync(path.join(folder, name))));
};

// A repository above the folder, or a manifest in it. Without AIT running this is all that can be known.
const looksLikeProject = (start) => {
  let current = path.resolve(start);
  while (true) {
    if (existsSync(path.join(current, ".git"))) return true;
    const parent = path.dirname(current);
    if (parent === current) break;
    current = parent;
  }

  try {
    return readdirSync(start).some((name) => MANIFESTS.includes(name) || /\.(sln|slnx|csproj)$/i.test(name));
  } catch {
    return false;
  }
};

// reachable false: no server answered. data null: it answered but could not say; then the hook stays out of the way.
const askGate = async (port, folder) => {
  try {
    const response = await fetch(`http://127.0.0.1:${port}/api/skill/gate?cwd=${encodeURIComponent(folder)}`, { signal: AbortSignal.timeout(GATE_TIMEOUT_MS) });
    if (!response.ok) return { reachable: true, data: null };
    return { reachable: true, data: (await response.json())?.data ?? null };
  } catch (error) {
    // fetch reports a refused connection as a TypeError; a slow or garbled answer still came from a server.
    return { reachable: error?.name !== "TypeError", data: null };
  }
};

const readInput = async () => {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    return null;
  }
};

const say = (hookSpecificOutput) => process.stdout.write(JSON.stringify({ hookSpecificOutput }));

const describe = (gate, cwd, config) => {
  if (!gate.reachable) {
    if (!looksLikeProject(cwd)) return null;
    return isInstalled()
      ? `AIT (the aithread orchestrator) is installed but its server is not running. The ${SKILL} skill starts it when a request changes code.`
      : `AIT (the aithread orchestrator) is not installed on this computer. The ${SKILL} skill holds the install steps, which need one confirmation from the developer, when a request changes code.`;
  }

  const data = gate.data;
  if (!data || data.state === "NotAProject" || isDisabled(config, cwd, data.projectRoot)) return null;
  const project = `"${data.projectName}" (${data.projectRoot})`;
  switch (data.state) {
    case "Ready":
      return `This folder is AIT project ${project}. Code changes here are made by AIT through the ${SKILL} skill, and a hook blocks the file edit tools on its files. Questions and explanations do not need AIT.`;
    case "SetupIncomplete":
      return `AIT is running but its model nodes are not set up yet, so it cannot take project ${project}. The ${SKILL} skill completes the setup when a request changes code.`;
    case "ProjectNotRegistered":
      return `AIT is ready but project ${project} is not registered with it. The ${SKILL} skill registers it when a request changes code.`;
    default:
      return null;
  }
};

const onPrompt = async (input, port, config) => {
  const cwd = input.cwd ?? process.cwd();
  const text = describe(await askGate(port, cwd), cwd, config);
  if (text) say({ hookEventName: input.hook_event_name, additionalContext: text });
};

// The files a tool call would change, whichever way this CLI names them.
const targetsOf = (toolInput) => {
  const single = toolInput?.file_path ?? toolInput?.notebook_path ?? toolInput?.path ?? toolInput?.filePath;
  if (typeof single === "string" && single.length > 0) return [single];

  const patch = [toolInput?.patch, toolInput?.patchText, toolInput?.command, toolInput?.input].find((value) => typeof value === "string");
  return patch ? [...patch.matchAll(PATCH_TARGET)].map((match) => match[1]) : [];
};

const onEdit = async (input, port, config) => {
  for (const target of targetsOf(input.tool_input)) {
    const file = path.resolve(input.cwd ?? process.cwd(), target);
    if (file.split(path.sep).some((part) => OWN_FOLDERS.has(part.toLowerCase()))) continue;

    const folder = path.dirname(file);
    const { data } = await askGate(port, folder);
    if (data?.state !== "Ready" || isDisabled(config, folder, data.projectRoot)) continue;

    const reason = `${file} belongs to AIT project "${data.projectName}", whose code changes are made by AIT. The ${SKILL} skill delegates this change. If AIT cannot do it, the developer decides; 'aithread skill off' lets edits through for this project.`;
    if (input.hook_event_name === "BeforeTool") process.stdout.write(JSON.stringify({ decision: "deny", reason }));
    else say({ hookEventName: "PreToolUse", permissionDecision: "deny", permissionDecisionReason: reason });
    return;
  }
};

const main = async () => {
  if (process.argv[2] === "decline") {
    mkdirSync(HOME, { recursive: true });
    writeFileSync(SETTINGS_FILE, JSON.stringify({ ...settings(), declined: true }, null, 2));
    return;
  }

  if (process.env.AITHREAD_CLI_ENGINE || process.platform !== "win32" || process.arch !== "x64") return;

  const input = await readInput();
  const port = aitPort();
  const config = settings();
  if (!input || config.declined || isProxied(port)) return;

  if (PROMPT_EVENTS.has(input.hook_event_name)) await onPrompt(input, port, config);
  else if (EDIT_EVENTS.has(input.hook_event_name)) await onEdit(input, port, config);
};

await main().catch(() => {});
