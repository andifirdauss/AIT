// AIT for opencode: runs AIT's gate script before each model call and before each file edit. Written by
// "aithread skill install opencode" and removed by "aithread skill uninstall opencode".
import { spawn } from "node:child_process";

const GATE = "__AIT_GATE__";
const EDIT_TOOLS = new Set(["edit", "write", "apply_patch"]);

// The gate's answer, or null. Nothing here may stop the session: a failing gate means no opinion.
const askGate = (payload) =>
  new Promise((resolve) => {
    try {
      const child = spawn("node", [GATE], { stdio: ["pipe", "pipe", "ignore"], windowsHide: true });
      const timer = setTimeout(() => child.kill(), 3000);
      let output = "";
      child.stdout.on("data", (chunk) => (output += chunk));
      child.on("error", () => resolve(null));
      child.on("close", () => {
        clearTimeout(timer);
        try {
          resolve(JSON.parse(output).hookSpecificOutput ?? null);
        } catch {
          resolve(null);
        }
      });
      child.stdin.on("error", () => {});
      child.stdin.end(JSON.stringify(payload));
    } catch {
      resolve(null);
    }
  });

export const AIThreadGate = async ({ directory }) => ({
  "tool.execute.before": async (input, output) => {
    if (!EDIT_TOOLS.has(input.tool)) return;
    const answer = await askGate({
      hook_event_name: "PreToolUse",
      cwd: directory,
      tool_name: input.tool,
      tool_input: { file_path: output.args?.filePath, patch: output.args?.patchText }
    });
    if (answer?.permissionDecision === "deny") throw new Error(answer.permissionDecisionReason);
  },
  "experimental.chat.system.transform": async (_input, output) => {
    const answer = await askGate({ hook_event_name: "UserPromptSubmit", cwd: directory });
    if (answer?.additionalContext) output.system.push(answer.additionalContext);
  }
});
