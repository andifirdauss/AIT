using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    public SecretValueDto? RevealSecret(string id) => secrets.Reveal(id) is { } value ? new SecretValueDto(id, value) : null;
}
