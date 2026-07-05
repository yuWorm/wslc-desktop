using Microsoft.Terminal.Control;
using Microsoft.Terminal.Settings.Model;
using Microsoft.Terminal.TerminalConnection;

Probe("type TermControl", () => typeof(TermControl).FullName);
Probe("type ConptyConnection", () => typeof(ConptyConnection).FullName);
Probe("type CascadiaSettings", () => typeof(CascadiaSettings).FullName);
Probe("type TerminalSettings", () => typeof(TerminalSettings).FullName);
Probe("CascadiaSettings.LoadDefaults", () => CascadiaSettings.LoadDefaults().ToString());
Probe("CascadiaSettings.LoadAll", () => CascadiaSettings.LoadAll().ToString());
Probe("new ConptyConnection", () => new ConptyConnection().ToString());

static void Probe(string name, Func<string?> action)
{
    try
    {
        Console.WriteLine($"OK {name}: {action()}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {name}: {ex.GetType().FullName}: {ex.Message}");

        Exception? inner = ex.InnerException;
        while (inner is not null)
        {
            Console.WriteLine($"  inner: {inner.GetType().FullName}: {inner.Message}");
            inner = inner.InnerException;
        }
    }
}
