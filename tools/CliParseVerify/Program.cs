using wslc_desktop.Models;
using wslc_desktop.Services;

var images = WslcCliOutputParser.ParseImages("""
REPOSITORY    TAG      IMAGE ID       CREATED        SIZE
hello-world   latest   e2ac70e7319a   3 months ago   0.01 MB
""");

Expect(images.Count == 1, "Expected one image from wslc images output.");
Expect(images[0].Repository == "hello-world", "Expected repository hello-world.");
Expect(images[0].Tag == "latest", "Expected tag latest.");
Expect(images[0].Id == "e2ac70e7319a", "Expected image id.");
Expect(images[0].Created == "3 months ago", "Expected created text.");
Expect(images[0].Size == "0.01 MB", "Expected size text.");

var jsonImages = WslcCliOutputParser.ParseImages("""
[
  {
    "Created": 1782533859,
    "Id": "sha256:4aaf0b273f92a76e458efc72cef4893c2c54ae2f1451d07112f1ce79f3ac0487",
    "Repository": "ubuntu",
    "Size": 100150272,
    "Tag": "latest"
  }
]
""");
Expect(jsonImages.Count == 1, "Expected one image from JSON output.");
Expect(jsonImages[0].Id == "4aaf0b273f92", "Expected short image id from JSON output.");
Expect(jsonImages[0].Repository == "ubuntu", "Expected JSON image repository.");
Expect(jsonImages[0].Size.Contains("MB", StringComparison.Ordinal), "Expected formatted JSON image size.");

var emptyContainers = WslcCliOutputParser.ParseContainers("""
容器 ID   名称   映像   已创建   状态   端口
""");
Expect(emptyContainers.Count == 0, "Expected empty container table to parse.");

var containers = WslcCliOutputParser.ParseContainers("""
容器 ID        名称          映像                 已创建          状态        端口
abc123def456   demo-web      nginx:latest         3 minutes ago   Running     8080->80/tcp
""");

Expect(containers.Count == 1, "Expected one container from wslc list output.");
Expect(containers[0].Id == "abc123def456", "Expected container id.");
Expect(containers[0].Name == "demo-web", "Expected container name.");
Expect(containers[0].Image == "nginx:latest", "Expected container image.");
Expect(containers[0].State == ContainerRuntimeState.Running, "Expected running state.");
Expect(containers[0].PortSummary == "8080->80/tcp", "Expected port summary.");

var jsonContainers = WslcCliOutputParser.ParseContainers("""
[
  {
    "CreatedAt": 1783178374,
    "Id": "89bb042af303572a18eecc90f19b86312bdbcc7628a81f9c6525e663ab884760",
    "Image": "ubuntu",
    "Name": "whispering_teton",
    "Ports": [],
    "State": 2,
    "StateChangedAt": 1783178375
  }
]
""");
Expect(jsonContainers.Count == 1, "Expected one container from JSON output.");
Expect(jsonContainers[0].Id == "89bb042af303", "Expected short container id from JSON output.");
Expect(jsonContainers[0].State == ContainerRuntimeState.Running, "Expected state 2 to map to running.");

var runner = new FakeCommandRunner();
runner.Enqueue("image list --format json", """
[
  {
    "Created": 1782533859,
    "Id": "sha256:4aaf0b273f92a76e458efc72cef4893c2c54ae2f1451d07112f1ce79f3ac0487",
    "Repository": "ubuntu",
    "Size": 100150272,
    "Tag": "latest"
  }
]
""");
runner.Enqueue("pull ubuntu:latest", "pulled");
runner.Enqueue("rmi ubuntu:latest", "deleted");

var imageService = new WslcImageService(runner);
var listedImages = await imageService.ListImagesAsync();
Expect(listedImages.Count == 1, "CLI image service should list JSON images.");
await foreach (var _ in imageService.PullImageAsync(new ImagePullRequest("ubuntu:latest")))
{
}
await imageService.DeleteImageAsync("ubuntu:latest");
Expect(runner.Commands.SequenceEqual(["image list --format json", "pull ubuntu:latest", "rmi ubuntu:latest"]), "Image service should use wslc CLI commands.");

runner = new FakeCommandRunner();
runner.Enqueue("container list --all --format json", """
[
  {
    "CreatedAt": 1783178374,
    "Id": "89bb042af303572a18eecc90f19b86312bdbcc7628a81f9c6525e663ab884760",
    "Image": "ubuntu",
    "Name": "whispering_teton",
    "Ports": [],
    "State": 2,
    "StateChangedAt": 1783178375
  }
]
""");
runner.Enqueue("stats --all --format json", """
[
  {
    "CPUPerc": "1.50%",
    "ID": "89bb042af303572a18eecc90f19b86312bdbcc7628a81f9c6525e663ab884760",
    "MemUsage": "2.45 MiB / 15.43 GiB",
    "Name": "whispering_teton"
  }
]
""");
runner.Enqueue("container stop 89bb042af303", "");
runner.Enqueue("container start 89bb042af303", "");
runner.Enqueue("container remove -f 89bb042af303", "");
runner.Enqueue("logs --tail 200 89bb042af303", "hello from logs");
runner.Enqueue("exec 89bb042af303 uname -a", "Linux demo");

var containerService = new WslcContainerService(runner);
var processService = new WslcProcessService(runner);
var listedContainers = await containerService.ListContainersAsync();
Expect(listedContainers.Count == 1, "CLI container service should list JSON containers.");
Expect(listedContainers[0].CpuPercent == 1.5, "CLI container service should merge stats CPU.");
Expect(listedContainers[0].MemoryUsed == "2.45 MiB / 15.43 GiB", "CLI container service should merge stats memory.");
await containerService.RestartAsync("89bb042af303");
await containerService.DeleteAsync("89bb042af303");
var logLines = new List<ContainerLogLine>();
await foreach (var line in processService.StreamLogsAsync("89bb042af303"))
{
    logLines.Add(line);
}
Expect(logLines.Count == 1 && logLines[0].Message == "hello from logs", "CLI process service should read wslc logs.");
var exec = await processService.ExecuteAsync(new ProcessExecutionRequest("89bb042af303", ["uname", "-a"], ""));
Expect(exec.StandardOutput == "Linux demo", "CLI process service should run wslc exec.");
Expect(runner.Commands.SequenceEqual([
    "container list --all --format json",
    "stats --all --format json",
    "container stop 89bb042af303",
    "container start 89bb042af303",
    "container remove -f 89bb042af303",
    "logs --tail 200 89bb042af303",
    "exec 89bb042af303 uname -a"
]), "Container/process services should use CLI commands.");

Console.WriteLine("CLI_PARSE_VERIFY_OK");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class FakeCommandRunner : IWslcCommandFallback
{
    private readonly Queue<(string Command, CommandResult Result)> _results = new();

    public List<string> Commands { get; } = [];

    public void Enqueue(string command, string standardOutput, int exitCode = 0, string standardError = "")
    {
        _results.Enqueue((command, new CommandResult(exitCode, standardOutput, standardError, TimeSpan.Zero)));
    }

    public Task<CommandResult> RunAsync(string arguments, CancellationToken cancellationToken = default)
    {
        Commands.Add(arguments);

        if (_results.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected command: {arguments}");
        }

        var (expected, result) = _results.Dequeue();
        if (!string.Equals(expected, arguments, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected command '{expected}', got '{arguments}'.");
        }

        return Task.FromResult(result);
    }
}
