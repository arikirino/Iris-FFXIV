# Patch: Brio/Services/DalamudPluginService.cs

Add `IGamepadState` to the service bag so it can be constructor-injected
into `AnalogInputProvider`.

## Change

In `DalamudPluginService.cs`, add one line inside the class body alongside
the other `[PluginService]` properties:

```diff
     [PluginService] public IGameConfig GameConfig { get; private set; } = null!;
+    [PluginService] public IGamepadState GamepadState { get; private set; } = null!;
```

No other changes needed — Dalamud's `pluginInterface.Inject(this)` call in
the constructor automatically populates all `[PluginService]`-attributed
properties.
