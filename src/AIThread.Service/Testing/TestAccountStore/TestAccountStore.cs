using AIThread.Data.Memory;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Testing;

// Reads and writes the test accounts and testing settings next to long memory (LongMemoryStore.CreateTestingAsync), so a
// shared store shares them with every developer of the project. Usernames are unique per project whatever their case.
public sealed partial class TestAccountStore(LongMemoryStore store) : ITestAccountStore
{
}
