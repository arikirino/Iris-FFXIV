using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.GamePad;
using Iris.Services;
using Iris.UI;

namespace Iris;

public sealed class Plugin : IDalamudPlugin
{
    // ── Dalamud services (injected by the plugin framework) ──────
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IGamepadState GamepadState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/iris";

    // ── Plugin state ─────────────────────────────────────────────
    public Configuration Configuration { get; init; }

    private readonly IrisCameraService _cameraService;
    private readonly IrisControllerService _controllerService;

    public readonly WindowSystem WindowSystem = new("Iris");
    private readonly IrisWindow _irisWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // ── Initialise services ──────────────────────────────────
        _cameraService = new IrisCameraService(Log);

        _controllerService = new IrisControllerService(
            Condition,
            GamepadState,
            Framework,
            Log,
            Configuration,
            _cameraService);

        // ── Initialise UI ────────────────────────────────────────
        _irisWindow = new IrisWindow(this, _cameraService, Configuration);
        WindowSystem.AddWindow(_irisWindow);

        // ── Hook playback tick into the framework update loop ────
        Framework.Update += OnFrameworkUpdate;

        // ── Register slash command ───────────────────────────────
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Iris camera path editor.",
        });

        // ── Wire UI draw callbacks ───────────────────────────────
        PluginInterface.UiBuilder.Draw        += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi  += ToggleMainUi;

        Log.Information("[Iris] Plugin loaded. Use /iris to open.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw       -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Framework.Update                     -= OnFrameworkUpdate;

        WindowSystem.RemoveAllWindows();

        _controllerService.Dispose();
        _cameraService.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    // ── Framework update — drives playback tick ──────────────────

    private void OnFrameworkUpdate(IFramework framework)
    {
        var dt = (float)framework.UpdateDelta.TotalSeconds;
        _cameraService.Tick(dt);
    }

    // ── Command handler ──────────────────────────────────────────

    private void OnCommand(string command, string args)
    {
        _irisWindow.Toggle();
    }

    public void ToggleMainUi() => _irisWindow.Toggle();
}
