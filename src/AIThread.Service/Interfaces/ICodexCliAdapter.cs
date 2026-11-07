namespace AIThread.Service.Interfaces;

// Runs the official Codex CLI headless (`codex exec --json`, read-only sandbox, nothing saved) as a model.
public interface ICodexCliAdapter : ICliEngine;
