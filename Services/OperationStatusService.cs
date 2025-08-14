using AdminConsole.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace AdminConsole.Services;

public class OperationStatusService : IOperationStatusService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<OperationStatusService> _logger;
    private readonly ConcurrentDictionary<string, OperationStatus> _operations = new();
    
    public event EventHandler<OperationStatusEventArgs>? StatusUpdated;

    public OperationStatusService(IMemoryCache cache, ILogger<OperationStatusService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> StartOperationAsync(string operationId, string operationType, string description)
    {
        var operation = new OperationStatus
        {
            OperationId = operationId,
            OperationType = operationType,
            Description = description,
            CurrentStatus = "Starting...",
            StartTime = DateTime.UtcNow,
            IsCompleted = false,
            IsSuccess = false
        };

        operation.StatusHistory.Add($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - Starting operation: {description}");
        
        _operations.TryAdd(operationId, operation);
        _cache.Set(operationId, operation, TimeSpan.FromMinutes(30));
        
        _logger.LogInformation("Started operation {OperationId} - {OperationType}: {Description}", 
            operationId, operationType, description);

        await NotifyStatusUpdated(operationId, "Starting...", description);
        
        return operationId;
    }

    public async Task UpdateStatusAsync(string operationId, string status, string? details = null)
    {
        if (_operations.TryGetValue(operationId, out var operation))
        {
            operation.CurrentStatus = status;
            operation.Details = details;
            
            var statusEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {status}";
            if (!string.IsNullOrEmpty(details))
            {
                statusEntry += $": {details}";
            }
            operation.StatusHistory.Add(statusEntry);
            
            _cache.Set(operationId, operation, TimeSpan.FromMinutes(30));
            
            _logger.LogInformation("Updated operation {OperationId} status: {Status} - {Details}", 
                operationId, status, details ?? "");

            await NotifyStatusUpdated(operationId, status, details);
        }
    }

    public async Task CompleteOperationAsync(string operationId, bool success, string? result = null)
    {
        if (_operations.TryGetValue(operationId, out var operation))
        {
            operation.IsCompleted = true;
            operation.IsSuccess = success;
            operation.EndTime = DateTime.UtcNow;
            operation.Result = result;
            operation.CurrentStatus = success ? "Completed successfully" : "Failed";
            
            var statusEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {(success ? "Completed successfully" : "Failed")}";
            if (!string.IsNullOrEmpty(result))
            {
                statusEntry += $": {result}";
            }
            operation.StatusHistory.Add(statusEntry);
            
            _cache.Set(operationId, operation, TimeSpan.FromHours(2));
            
            var duration = operation.EndTime - operation.StartTime;
            _logger.LogInformation("Completed operation {OperationId} - Success: {Success}, Duration: {Duration}ms, Result: {Result}", 
                operationId, success, duration?.TotalMilliseconds ?? 0, result ?? "");

            await NotifyStatusUpdated(operationId, operation.CurrentStatus, result, true, success);
        }
    }

    public async Task<OperationStatus?> GetStatusAsync(string operationId)
    {
        _operations.TryGetValue(operationId, out var operation);
        return await Task.FromResult(operation);
    }

    private async Task NotifyStatusUpdated(string operationId, string status, string? details = null, bool isCompleted = false, bool isSuccess = false)
    {
        try
        {
            var args = new OperationStatusEventArgs
            {
                OperationId = operationId,
                Status = status,
                Details = details,
                IsCompleted = isCompleted,
                IsSuccess = isSuccess
            };

            StatusUpdated?.Invoke(this, args);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying status update for operation {OperationId}", operationId);
        }
    }
}