namespace AIThread.Common.Enums;

// The role of a file in its own codebase, whatever the stack: the second part of a memory label. A memory may span
// several layers (a flow from Entry through Logic to Data).
public enum MemoryLayer
{
    // Screens and interaction: views, pages, components, styles.
    UI = 1,

    // Where a request or command comes in: controllers, routes, CLI commands, job handlers, message consumers.
    Entry = 2,

    // Business rules and processes: services, BL classes, validators, stores, outbound clients.
    Logic = 3,

    // Storage and data shapes: repositories, DbContext, migrations, queries, entities, DTOs.
    Data = 4,

    // Wiring, configuration and build: startup, DI, settings, Dockerfile, CI.
    Infra = 5,

    Test = 6
}
