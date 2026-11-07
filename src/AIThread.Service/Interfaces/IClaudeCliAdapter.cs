namespace AIThread.Service.Interfaces;

// Runs the official Claude Code CLI headless (`claude -p`, stream-json) as a model. Models: opus, sonnet, haiku,
// fable or a full model name.
public interface IClaudeCliAdapter : ICliEngine;
