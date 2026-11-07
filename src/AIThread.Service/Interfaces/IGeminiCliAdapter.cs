namespace AIThread.Service.Interfaces;

// Runs the official Gemini CLI headless (`gemini -p --output-format stream-json`, tools denied) as a model.
public interface IGeminiCliAdapter : ICliEngine;
