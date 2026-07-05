using WslcDesktop.Contracts;
using WslcDesktop.Runtime;

namespace Wslcd;

internal sealed class OperationTracker
{
    private readonly object _gate = new();
    private readonly List<OperationRecordDto> _records = [];
    private readonly int _retentionCount;
    private readonly string _provider;

    public OperationTracker(string provider, int retentionCount)
    {
        _provider = provider;
        _retentionCount = retentionCount;
    }

    public IReadOnlyList<OperationRecordDto> List()
    {
        lock (_gate)
        {
            return _records
                .OrderByDescending(record => record.StartedAt)
                .ToArray();
        }
    }

    public async Task TrackAsync(string resourceType, string resourceId, string action, Func<Task> operation)
    {
        string id = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        Add(new OperationRecordDto(id, _provider, resourceType, resourceId, action, "Running", started, null, null, string.Empty, string.Empty));

        try
        {
            await operation();
            Complete(id, "Succeeded", null, string.Empty, string.Empty);
        }
        catch (RuntimeCommandException ex)
        {
            Complete(id, "Failed", ex.Result.ExitCode, Tail(ex.Result.StandardOutput), Tail(ex.Result.StandardError));
            throw;
        }
        catch
        {
            Complete(id, "Failed", null, string.Empty, string.Empty);
            throw;
        }
    }

    public async Task<T> TrackResultAsync<T>(string resourceType, string resourceId, string action, Func<Task<T>> operation)
    {
        string id = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        Add(new OperationRecordDto(id, _provider, resourceType, resourceId, action, "Running", started, null, null, string.Empty, string.Empty));

        try
        {
            T result = await operation();
            Complete(id, "Succeeded", null, string.Empty, string.Empty);
            return result;
        }
        catch (RuntimeCommandException ex)
        {
            Complete(id, "Failed", ex.Result.ExitCode, Tail(ex.Result.StandardOutput), Tail(ex.Result.StandardError));
            throw;
        }
        catch
        {
            Complete(id, "Failed", null, string.Empty, string.Empty);
            throw;
        }
    }

    private void Add(OperationRecordDto record)
    {
        lock (_gate)
        {
            _records.Add(record);
            if (_records.Count > _retentionCount)
            {
                _records.RemoveRange(0, _records.Count - _retentionCount);
            }
        }
    }

    private void Complete(string operationId, string status, int? exitCode, string stdoutTail, string stderrTail)
    {
        lock (_gate)
        {
            int index = _records.FindIndex(record => record.OperationId == operationId);
            if (index < 0)
            {
                return;
            }

            var current = _records[index];
            _records[index] = current with
            {
                Status = status,
                CompletedAt = DateTimeOffset.UtcNow,
                ExitCode = exitCode,
                StdoutTail = stdoutTail,
                StderrTail = stderrTail
            };
        }
    }

    private static string Tail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 4096 ? value : value[^4096..];
    }
}
