// AIT for oh-my-pi: runs AIT's gate script before each prompt and before each file edit. Written by
// "aithread skill install omp" and removed by "aithread skill uninstall omp".
import { spawn } from "node:child_process";

const GATE = "__AIT_GATE__";
const EDIT_TOOLS = new Set(["edit", "write", "ast_edit", "notebook"]);

// The gate's answer, or null. A throwing handler would block the tool, so nothing may escape from here.
function askGate(payload: unknown): Promise<any> {
	return new Promise(resolve => {
		try {
			const child = spawn("node", [GATE], { stdio: ["pipe", "pipe", "ignore"], windowsHide: true });
			const timer = setTimeout(() => child.kill(), 3000);
			let output = "";
			child.stdout.on("data", chunk => (output += chunk));
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
}

export default function (pi: any): void {
	pi.on("before_agent_start", async (_event: any, ctx: any) => {
		const answer = await askGate({ hook_event_name: "UserPromptSubmit", cwd: ctx.cwd });
		if (!answer?.additionalContext) return;
		return { message: { customType: "aithread", content: answer.additionalContext, display: false } };
	});

	pi.on("tool_call", async (event: any, ctx: any) => {
		if (!EDIT_TOOLS.has(event.toolName)) return;
		const answer = await askGate({ hook_event_name: "PreToolUse", cwd: ctx.cwd, tool_name: event.toolName, tool_input: event.input });
		if (answer?.permissionDecision === "deny") return { block: true, reason: answer.permissionDecisionReason };
	});
}
