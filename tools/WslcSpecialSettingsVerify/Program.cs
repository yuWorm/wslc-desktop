using WslcDesktop.Runtime.Providers.WslcCli;

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'");
    }
}

var settings = WslcRuntimeSettings.FromEnvironment(new Dictionary<string, string?>
{
    ["WSLCD_WSLC_HTTP_PROXY"] = "http://127.0.0.1:7890",
    ["WSLCD_WSLC_HTTPS_PROXY"] = "http://127.0.0.1:7890",
    ["WSLCD_WSLC_NO_PROXY"] = "localhost,127.0.0.1",
    ["WSLCD_WSLC_IMAGE_MIRROR"] = "mirror.local/library",
    ["WSLCD_WSLC_REWRITE_IMAGE_TAG"] = "1",
    ["WSLCD_WSLC_REMOVE_REWRITTEN_SOURCE_TAG"] = "1",
    ["WSLCD_WSLC_ENVIRONMENT"] = "FOO=bar\nEMPTY=\nBAD\n HTTP_PROXY=ignored "
});

AssertEqual("http://127.0.0.1:7890", settings.HttpProxy, "http proxy");
AssertEqual("http://127.0.0.1:7890", settings.HttpsProxy, "https proxy");
AssertEqual("localhost,127.0.0.1", settings.NoProxy, "no proxy");
AssertEqual("mirror.local/library", settings.ImageMirror, "image mirror");
AssertEqual(true, settings.RewriteImageTag, "rewrite image tag");
AssertEqual(true, settings.RemoveRewrittenSourceTag, "remove rewritten source tag");
AssertEqual("bar", settings.Environment["FOO"], "custom env");
AssertEqual("", settings.Environment["EMPTY"], "empty env value");
AssertEqual(false, settings.Environment.ContainsKey("BAD"), "invalid env omitted");
AssertEqual(false, settings.Environment.ContainsKey("HTTP_PROXY"), "proxy env reserved");

AssertEqual("mirror.local/library/ubuntu:latest", WslcImageReferencePolicy.ApplyMirror("ubuntu:latest", settings), "mirror bare image");
AssertEqual("mirror.local/library/library/nginx:alpine", WslcImageReferencePolicy.ApplyMirror("library/nginx:alpine", settings), "mirror namespace image");
AssertEqual("ghcr.io/acme/app:1", WslcImageReferencePolicy.ApplyMirror("ghcr.io/acme/app:1", settings), "keep qualified image");
AssertEqual("acme/app:1", WslcImageReferencePolicy.GetRewriteTarget("ghcr.io/acme/app:1", settings), "strip registry host");
AssertEqual("", WslcImageReferencePolicy.GetRewriteTarget("ubuntu:latest", settings), "do not rewrite docker hub image");

var env = settings.BuildProcessEnvironment();
AssertEqual("http://127.0.0.1:7890", env["HTTP_PROXY"], "HTTP_PROXY injected");
AssertEqual("http://127.0.0.1:7890", env["http_proxy"], "http_proxy injected");
AssertEqual("localhost,127.0.0.1", env["NO_PROXY"], "NO_PROXY injected");
AssertEqual("bar", env["FOO"], "custom process env injected");

Console.WriteLine("WSLC_SPECIAL_SETTINGS_VERIFY_OK");
