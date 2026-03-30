# Patch: Brio/Config/Configuration.cs

Add `ControllerConfiguration` as a top-level config section, alongside
the existing `InputManagerConfiguration`.

## Change

```diff
     // Input
     public InputManagerConfiguration InputManager { get; set; } = new InputManagerConfiguration();

+    // Controller (gamepad analog input) — Phase 1
+    public ControllerConfiguration Controller { get; set; } = new ControllerConfiguration();

     // AutoSave
     public AutoSaveConfiguration AutoSave { get; set; } = new AutoSaveConfiguration();
```

Dalamud's `IPluginConfiguration` serializes all public properties automatically,
so no additional registration is required — the new section persists to disk
alongside the rest of the config on the next `ConfigurationService.Save()` call.
