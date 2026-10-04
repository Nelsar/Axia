namespace Nelsar.Axia.Contracts.Requests;

public class BulkUserRequest
{
    public string Operation { get; set; } = "create";
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? NewEmail { get; set; }
}
