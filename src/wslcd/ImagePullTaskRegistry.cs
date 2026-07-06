using WslcDesktop.Contracts;

namespace Wslcd;

internal sealed class ImagePullTaskRegistry
{
    private readonly object _gate = new();
    private readonly int _retentionCount;
    private readonly List<ImagePullTaskDto> _tasks = [];

    public ImagePullTaskRegistry(int retentionCount)
    {
        _retentionCount = Math.Max(1, retentionCount);
    }

    public ImagePullTaskDto Start(string reference, string source)
    {
        var task = new ImagePullTaskDto(
            Guid.NewGuid().ToString("N"),
            reference.Trim(),
            source.Trim(),
            "Running",
            DateTimeOffset.UtcNow,
            null,
            reference.Trim(),
            "Queued",
            0,
            0,
            string.Empty);

        lock (_gate)
        {
            _tasks.Add(task);
            PruneLocked();
        }

        return task;
    }

    public void Update(string taskId, ImagePullProgressDto progress)
    {
        Update(taskId, task => task with
        {
            State = "Running",
            ProgressId = progress.Id,
            Status = progress.Status,
            CurrentBytes = progress.CurrentBytes,
            TotalBytes = progress.TotalBytes,
            ErrorMessage = string.Empty
        });
    }

    public void Succeed(string taskId)
    {
        Update(taskId, task => task with
        {
            State = "Succeeded",
            CompletedAt = DateTimeOffset.UtcNow,
            Status = "Completed",
            ErrorMessage = string.Empty,
            CurrentBytes = task.TotalBytes > 0 ? task.TotalBytes : task.CurrentBytes
        });
    }

    public void Fail(string taskId, string errorMessage)
    {
        Update(taskId, task => task with
        {
            State = "Failed",
            CompletedAt = DateTimeOffset.UtcNow,
            Status = "Failed",
            ErrorMessage = errorMessage
        });
    }

    public IReadOnlyList<ImagePullTaskDto> List()
    {
        lock (_gate)
        {
            return _tasks
                .OrderByDescending(task => task.StartedAt)
                .ToArray();
        }
    }

    private void Update(string taskId, Func<ImagePullTaskDto, ImagePullTaskDto> update)
    {
        lock (_gate)
        {
            int index = _tasks.FindIndex(task => task.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            _tasks[index] = update(_tasks[index]);
            PruneLocked();
        }
    }

    private void PruneLocked()
    {
        if (_tasks.Count <= _retentionCount)
        {
            return;
        }

        var removable = _tasks
            .Where(task => !task.State.Equals("Running", StringComparison.OrdinalIgnoreCase))
            .OrderBy(task => task.StartedAt)
            .Take(_tasks.Count - _retentionCount)
            .ToArray();

        foreach (var task in removable)
        {
            _tasks.Remove(task);
        }
    }
}
