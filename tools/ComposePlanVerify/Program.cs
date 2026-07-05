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
Expect(containers.StartedIds.SequenceEqual(created.Select(container => container.Id)), "Each created container should be started.");
Expect(images.PulledImages.SequenceEqual(["postgres:16", "nginx:latest"]), "Images should be pulled in execution order.");

var projects = await service.ListProjectsAsync();
Expect(projects.Count == 1, "Executed project should be tracked.");
Expect(projects[0].Name == "sample-stack" && projects[0].ServiceCount == 2 && projects[0].RunningCount == 2, "Project summary should include service and running counts.");

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

    public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ContainerSummary>>([]);
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

    public Task StopAsync(string containerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task RestartAsync(string containerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task DeleteAsync(string containerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
