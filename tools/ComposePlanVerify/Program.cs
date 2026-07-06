using wslc_desktop.Models;
using wslc_desktop.Services;

string root = Path.Combine(Path.GetTempPath(), "wslc-compose-verify", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Directory.CreateDirectory(Path.Combine(root, "site"));
Directory.CreateDirectory(Path.Combine(root, "db-data"));

string composePath = Path.Combine(root, "compose.yaml");
await File.WriteAllTextAsync(Path.Combine(root, ".env"), "POSTGRES_TAG=16\r\nAPP_ENV=Development\r\n");
await File.WriteAllTextAsync(composePath, """
name: sample-stack
services:
  web:
    image: nginx:latest
    command: ["/docker-entrypoint.sh", "nginx", "-g", "daemon off;"]
    ports:
      - "18080:80/tcp"
    volumes:
      - "./site:/usr/share/nginx/html:ro"
    environment:
      ASPNETCORE_ENVIRONMENT: ${APP_ENV}
      FEATURE_FLAG: "true"
    depends_on:
      - db
    build: .
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost"]
    networks:
      - frontend
  db:
    image: postgres:${POSTGRES_TAG}
    command: "postgres -c max_connections=20"
    ports:
      - "15432:5432"
    volumes:
      - "./db-data:/var/lib/postgresql/data"
    environment:
      - POSTGRES_PASSWORD=example
      - POSTGRES_DB=app
""");

var images = new FakeImageService();
var containers = new FakeContainerService();
var operations = new FakeOperationTracker();
var service = new ComposePlanService(images, containers, operations);

var projectsBeforePreview = await service.ListProjectsAsync();
Expect(projectsBeforePreview.Count == 0, "No projects should be tracked before preview.");

var plan = await service.PreviewAsync(composePath);
Expect(plan.Count == 2, "Two service plans should be generated.");

var web = plan.Single(service => service.ServiceName == "web");
Expect(web.ProjectName == "sample-stack", "Top-level compose name should become project name.");
Expect(web.Image == "nginx:latest", "Web image should be parsed.");
Expect(web.Command.SequenceEqual(["/docker-entrypoint.sh", "nginx", "-g", "daemon off;"]), "Web command sequence should be parsed.");
Expect(web.Ports.Count == 1 && web.Ports[0] == new PortMapping(18080, 80, "tcp"), "Web port should be parsed.");
Expect(web.Mounts.Count == 1, "Web bind mount should be parsed.");
Expect(Path.GetFullPath(web.Mounts[0].Source) == Path.Combine(root, "site"), "Relative bind mount should resolve from compose directory.");
Expect(web.Mounts[0].Target == "/usr/share/nginx/html" && web.Mounts[0].IsReadOnly, "Web mount target and read-only mode should be parsed.");
Expect(web.Environment["ASPNETCORE_ENVIRONMENT"] == "Development", "Environment interpolation should use .env values.");
Expect(web.DependsOn.SequenceEqual(["db"]), "depends_on list should be parsed.");
Expect(web.UnsupportedKeys.OrderBy(key => key).SequenceEqual(["build", "healthcheck", "networks"]), "Unsupported service keys should be explicit.");

var db = plan.Single(service => service.ServiceName == "db");
Expect(db.Image == "postgres:16", "DB image should interpolate .env values.");
Expect(db.Command.SequenceEqual(["postgres", "-c", "max_connections=20"]), "DB scalar command should be split.");
Expect(db.Environment["POSTGRES_PASSWORD"] == "example", "List-form environment should be parsed.");
Expect(db.Ports[0] == new PortMapping(15432, 5432, "tcp"), "Default TCP protocol should be applied.");

var created = await service.CreateAndStartAsync(composePath);
Expect(created.Count == 2, "Two containers should be created.");
Expect(containers.CreatedRequests.Count == 2, "Container service should receive two create requests.");
Expect(containers.CreatedRequests[0].Name == "sample-stack-db", "Dependency service should be created first.");
Expect(containers.CreatedRequests[1].Name == "sample-stack-web", "Dependent service should be created second.");
var dbLabels = containers.CreatedRequests[0].Labels ?? throw new InvalidOperationException("DB container should carry labels.");
Expect(dbLabels["com.docker.compose.project"] == "sample-stack", "DB container should carry Compose project label.");
Expect(dbLabels["com.docker.compose.service"] == "db", "DB container should carry Compose service label.");
Expect(dbLabels["com.docker.compose.container-number"] == "1", "DB container should carry Compose container number label.");
Expect(dbLabels["com.docker.compose.project.config_files"] == Path.GetFullPath(composePath), "Compose config file label should use the absolute compose path.");
Expect(dbLabels["com.docker.compose.project.working_dir"] == root, "Compose working directory label should use the compose directory.");

var webLabels = containers.CreatedRequests[1].Labels ?? throw new InvalidOperationException("Web container should carry labels.");
Expect(webLabels["com.docker.compose.project"] == "sample-stack", "Web container should carry Compose project label.");
Expect(webLabels["com.docker.compose.service"] == "web", "Web container should carry Compose service label.");
Expect(!string.IsNullOrWhiteSpace(webLabels["com.docker.compose.config-hash"]), "Web container should carry a stable config hash label.");
Expect(containers.StartedIds.SequenceEqual(created.Select(container => container.Id)), "Each created container should be started.");
Expect(images.PulledImages.SequenceEqual(["postgres:16", "nginx:latest"]), "Images should be pulled in execution order.");

containers.RuntimeContainers.Add(new ContainerSummary(
    "cli-1",
    "external-web-1",
    "nginx:latest",
    ContainerRuntimeState.Running,
    0,
    "12 MB",
    "now",
    "1m",
    "18080->80/tcp",
    "nginx",
    Labels: new Dictionary<string, string>
    {
        ["com.docker.compose.project"] = "external-stack",
        ["com.docker.compose.service"] = "web",
        ["com.docker.compose.project.config_files"] = Path.Combine(root, "external-compose.yaml"),
        ["com.docker.compose.project.working_dir"] = root
    }));
containers.RuntimeContainers.Add(new ContainerSummary(
    "cli-2",
    "external-db-1",
    "postgres:16",
    ContainerRuntimeState.Stopped,
    0,
    "0 B",
    "now",
    "-",
    "-",
    "postgres",
    Labels: new Dictionary<string, string>
    {
        ["com.docker.compose.project"] = "external-stack",
        ["com.docker.compose.service"] = "db",
        ["com.docker.compose.project.config_files"] = Path.Combine(root, "external-compose.yaml"),
        ["com.docker.compose.project.working_dir"] = root
    }));
containers.RuntimeContainers.Add(new ContainerSummary(
    "other-1",
    "standalone",
    "busybox:latest",
    ContainerRuntimeState.Running,
    0,
    "2 MB",
    "now",
    "1m",
    "-",
    "sleep",
    Labels: new Dictionary<string, string>
    {
        ["com.example.scope"] = "not-compose"
    }));

var projects = await service.ListProjectsAsync();
var externalProject = projects.Single(project => project.Name == "external-stack");
Expect(externalProject.ServiceCount == 2, "Runtime Compose discovery should count distinct services.");
Expect(externalProject.RunningCount == 1, "Runtime Compose discovery should count running service containers.");
Expect(externalProject.SourcePath == Path.Combine(root, "external-compose.yaml"), "Runtime Compose discovery should surface compose config path.");

var sampleProject = projects.Single(project => project.Name == "sample-stack");
Expect(sampleProject.ServiceCount == 2 && sampleProject.RunningCount == 2, "Created project should remain visible from tracked execution when runtime list has no matching labels.");

var details = await service.InspectProjectAsync("external-stack");
Expect(details.Project.Name == "external-stack", "Project details should include the selected project summary.");
Expect(details.Project.RunningCount == 1, "Project details should retain running count.");
Expect(details.Services.Count == 2, "Project details should group containers by service.");
var webRuntime = details.Services.Single(item => item.ServiceName == "web");
Expect(webRuntime.ContainerCount == 1 && webRuntime.RunningCount == 1, "Web service summary should include container and running counts.");
Expect(webRuntime.PortSummary == "18080->80/tcp", "Web service summary should expose ports.");
var dbRuntime = details.Services.Single(item => item.ServiceName == "db");
Expect(dbRuntime.ContainerCount == 1 && dbRuntime.RunningCount == 0, "DB service summary should include stopped container state.");
Expect(details.Containers.Count == 2, "Project details should include only compose project containers.");
Expect(details.Containers.All(container => container.ProjectName == "external-stack"), "Project container rows should carry project names.");

containers.ClearActionLogs();
await service.StopProjectAsync("external-stack");
Expect(containers.StoppedIds.SequenceEqual(["cli-1"]), "Stop project should stop running project containers only.");
Expect(!containers.StoppedIds.Contains("other-1"), "Stop project should ignore non-compose containers.");

containers.ClearActionLogs();
await service.StartProjectAsync("external-stack");
Expect(containers.StartedIds.SequenceEqual(["cli-2"]), "Start project should start stopped project containers only.");

containers.ClearActionLogs();
await service.RestartProjectAsync("external-stack");
Expect(containers.RestartedIds.SequenceEqual(["cli-1"]), "Restart project should restart running project containers only.");

containers.ClearActionLogs();
await service.DeleteProjectAsync("external-stack");
Expect(containers.StoppedIds.SequenceEqual(["cli-1"]), "Delete project should stop running project containers before removal.");
Expect(containers.DeletedIds.OrderBy(id => id).SequenceEqual(["cli-1", "cli-2"]), "Delete project should remove all project containers.");
Expect(!containers.DeletedIds.Contains("other-1"), "Delete project should ignore non-compose containers.");

Console.WriteLine("PHASE7_COMPOSE_PLAN_OK");
return 0;

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class FakeImageService : IWslcImageService
{
    public List<string> PulledImages { get; } = [];

    public Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ImageSummary>>([]);
    }

    public Task<IReadOnlyList<ImagePullTaskSnapshot>> ListPullTasksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ImagePullTaskSnapshot>>([]);
    }

    public async IAsyncEnumerable<ImagePullProgress> PullImageAsync(
        ImagePullRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PulledImages.Add(request.Reference);
        await Task.Yield();
        yield return new ImagePullProgress(request.Reference, "done", 1, 1);
    }

    public Task DeleteImageAsync(string imageReference, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

sealed class FakeContainerService : IWslcContainerService
{
    private int _nextId;

    public List<ContainerCreateRequest> CreatedRequests { get; } = [];

    public List<string> StartedIds { get; } = [];

    public List<string> StoppedIds { get; } = [];

    public List<string> RestartedIds { get; } = [];

    public List<string> DeletedIds { get; } = [];

    public List<ContainerSummary> RuntimeContainers { get; } = [];

    public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ContainerSummary>>(RuntimeContainers.ToArray());
    }

    public Task<ContainerSummary> CreateAsync(ContainerCreateRequest request, CancellationToken cancellationToken = default)
    {
        CreatedRequests.Add(request);
        string id = $"compose-{++_nextId}";
        return Task.FromResult(new ContainerSummary(
            id,
            request.Name,
            request.Image,
            ContainerRuntimeState.Created,
            0,
            "-",
            "now",
            "created",
            "-",
            string.Join(" ", request.Command)));
    }

    public Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        StartedIds.Add(containerId);
        return Task.CompletedTask;
    }

    public Task StopAsync(string containerId, CancellationToken cancellationToken = default)
    {
        StoppedIds.Add(containerId);
        return Task.CompletedTask;
    }

    public Task RestartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        RestartedIds.Add(containerId);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string containerId, CancellationToken cancellationToken = default)
    {
        DeletedIds.Add(containerId);
        return Task.CompletedTask;
    }

    public void ClearActionLogs()
    {
        StartedIds.Clear();
        StoppedIds.Clear();
        RestartedIds.Clear();
        DeletedIds.Clear();
    }
}

sealed class FakeOperationTracker : IOperationTracker
{
    public Task<IReadOnlyList<OperationRecord>> ListRecentAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OperationRecord>>([]);
    }

    public void Track(OperationRecord operation)
    {
    }
}
