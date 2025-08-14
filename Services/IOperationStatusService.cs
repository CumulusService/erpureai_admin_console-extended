using AdminConsole.Models;

namespace AdminConsole.Services;

public interface IOperationStatusService
{
    Task<string> StartOperationAsync(string operationId, string operationType, string description);
    Task UpdateStatusAsync(string operationId, string status, string? details = null);
    Task CompleteOperationAsync(string operationId, bool success, string? result = null);
    Task<OperationStatus?> GetStatusAsync(string operationId);
    event EventHandler<OperationStatusEventArgs> StatusUpdated;
}

public class OperationStatus
{
    public string OperationId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string? Details { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsSuccess { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Result { get; set; }
    public List<string> StatusHistory { get; set; } = new();
}

public class OperationStatusEventArgs : EventArgs
{
    public string OperationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Details { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsSuccess { get; set; }
}