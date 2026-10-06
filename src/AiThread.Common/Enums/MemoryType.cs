namespace AiThread.Common.Enums;

public enum MemoryType
{
    // Business rules, validation, flows and conventions of one project.
    Project = 1,

    // Generalizable technical knowledge reusable across projects.
    Knowledge = 2,

    // A page of the project's business wiki. Only in the planner's memory index; pages are stored as BusinessPage.
    Business = 3
}
