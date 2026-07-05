using wslc_desktop.Models;
using wslc_desktop.Services;

AssertEqual("zh-CN", AppLanguage.ResolveSystemLanguage(["zh-CN"]));
AssertEqual("zh-CN", AppLanguage.ResolveSystemLanguage(["zh-Hans-CN"]));
AssertEqual("zh-CN", AppLanguage.ResolveSystemLanguage(["zh"]));
AssertEqual("en-US", AppLanguage.ResolveSystemLanguage(["en-US"]));
AssertEqual("en-US", AppLanguage.ResolveSystemLanguage(["fr-FR"]));
AssertEqual("en-US", AppLanguage.ResolveSystemLanguage([]));

AssertEqual("System", AppLanguage.GetDisplayName(AppLanguage.System));
AssertEqual("中文", AppLanguage.GetDisplayName(AppLanguage.Chinese));
AssertEqual("English", AppLanguage.GetDisplayName(AppLanguage.English));

AssertEqual("en-US", AppLanguage.GetEffectiveLanguage(AppLanguage.System, ["en-US"]));
AssertEqual("zh-CN", AppLanguage.GetEffectiveLanguage(AppLanguage.System, ["zh-CN"]));
AssertEqual("zh-CN", AppLanguage.GetEffectiveLanguage(AppLanguage.Chinese, ["en-US"]));
AssertEqual("en-US", AppLanguage.GetEffectiveLanguage(AppLanguage.English, ["zh-CN"]));

var strings = new AppStringLocalizer();
AssertEqual("Settings saved. Restart the app or wslcd-desktop to apply runtime and language changes.", strings.Get("SettingsRestartRequired", "en-US"));
AssertEqual("设置已保存。请重启应用或 wslcd-desktop 以应用运行时和语言更改。", strings.Get("SettingsRestartRequired", "zh-CN"));
AssertEqual("Settings", strings.Get("SettingsPageTitle", "fr-FR"));
AssertEqual("数据卷", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "NavVolumes.Content"));
AssertEqual("数据卷", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "VolumesTitle.Text"));
AssertEqual("新建数据卷", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "VolumesNewButtonText.Text"));
AssertEqual("拉取任务", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "ImagesPullTasksTitle.Text"));
AssertEqual("{0} 个数据卷", strings.Get("VolumesSummary", "zh-CN"));
AssertEqual("新建数据卷", strings.Get("CreateVolumeTitle", "zh-CN"));
AssertEqual("排队中", strings.Get("ImagePullTaskQueued", "zh-CN"));
AssertEqual("{0} active · {1} total", strings.Get("ImagePullTasksSummary", "en-US"));
AssertEqual("Daemon OK", strings.Get("ShellStatusDaemonOk", "en-US"));
AssertEqual("Daemon 离线", strings.Get("ShellStatusDaemonOffline", "zh-CN"));
AssertEqual("运行状态", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "ShellStatusFlyoutTitle.Text"));
AssertEqual("Backend", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "en-US", "Resources.resw"), "ShellStatusBackendLabel.Text"));
AssertEqual("Settings saved. Restart wslcd-desktop to apply daemon runtime changes.", strings.Get("SettingsSavedDaemonRestartRecommended", "en-US"));
AssertEqual("启动 wslcd-desktop？", strings.Get("DaemonStartDialogTitle", "zh-CN"));
AssertEqual("主窗口", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "TrayMainWindowItem.Text"));
AssertEqual("Stop daemon", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "en-US", "Resources.resw"), "TrayStopDaemonItem.Text"));
AssertEqual("Daemon 状态", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "SettingsDaemonStatusInfo.Title"));
AssertEqual("需要 WSLC", strings.Get("WslcRequiredDialogTitle", "zh-CN"));
AssertEqual("wslc.exe is unavailable. Run: {0}", strings.Get("CliToolsWslcMissing", "en-US"));
AssertEqual("CLI 工具", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "SettingsCliToolsHeading.Text"));
AssertEqual("Add user PATH", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "en-US", "Resources.resw"), "SettingsCliToolsAddPathText.Text"));
AssertEqual("添加系统 PATH", ReadReswValue(Path.Combine(Directory.GetCurrentDirectory(), "Strings", "zh-CN", "Resources.resw"), "SettingsCliToolsAddSystemPathText.Text"));

string root = Path.Combine(Path.GetTempPath(), "wslc-localization-verify", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var settingsService = new FileAppSettingsService(root);
    var saved = new AppSettingsSnapshot(
        Path.Combine(root, "Data"),
        2,
        2048,
        "/bin/bash",
        true,
        AppLanguage.Chinese);

    await settingsService.SaveAsync(saved);
    AppSettingsSnapshot loaded = await settingsService.LoadAsync();
    AssertEqual(AppLanguage.Chinese, loaded.Language);
    AssertEqual("/bin/bash", loaded.DefaultShell);
}
finally
{
    Directory.Delete(root, recursive: true);
}

Console.WriteLine("LOCALIZATION_VERIFY_OK");

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static string ReadReswValue(string path, string key)
{
    var document = System.Xml.Linq.XDocument.Load(path);
    return document.Root?
        .Elements("data")
        .FirstOrDefault(element => string.Equals((string?)element.Attribute("name"), key, StringComparison.Ordinal))
        ?.Element("value")
        ?.Value
        ?? throw new InvalidOperationException($"Missing resource '{key}' in {path}.");
}
