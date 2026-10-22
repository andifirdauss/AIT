namespace AIThread.Common.Enums;

// When the planner adds new unit tests: only when the developer asks, also on its own for changes to business logic,
// or never.
public enum UnitTestAuthoring
{
    OnRequest = 1,
    AutoLogic = 2,
    Off = 3
}
