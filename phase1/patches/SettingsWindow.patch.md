# Patch: Brio/UI/Windows/SettingsWindow.cs

Add a "Controller" subsection inside the existing **Input** settings tab,
immediately after the existing keybind list. Follows the same ImGui patterns
already in the file (DragFloat, Checkbox, Combo, ApplyChange).

---

## 1. Locate the Input tab draw method

Search for the method containing:
```csharp
bool enableKeybinds = _configurationService.Configuration.InputManager.Enable;
```
This is inside the Input tab drawing code (tab index 5 in the ButtonSelectorStrip).

---

## 2. Add the Controller section at the end of that method

Append the following **after** the last `DrawKeyBind(...)` call (after the
keybind table is closed):

```csharp
// ── Controller (analog gamepad) ──────────────────────────────────────────
ImGui.Spacing();
ImGui.Separator();
ImGui.Spacing();
ImGui.TextColored(new System.Numerics.Vector4(0.8f, 0.8f, 0.4f, 1f), "Controller Camera");
ImGui.Spacing();

var ctrl = _configurationService.Configuration.Controller;

bool controllerEnable = ctrl.Enable;
if(ImGui.Checkbox("Enable Controller Camera", ref controllerEnable))
{
    ctrl.Enable = controllerEnable;
    _configurationService.ApplyChange();
}
ImGui.SameLine();
ImGui.TextDisabled("(Requires a free camera to be active)");

if(ctrl.Enable)
{
    ImGui.Indent();

    // Dead zone
    float deadZone = ctrl.DeadZone;
    if(ImGui.SliderFloat("Dead Zone", ref deadZone, 0f, 0.5f, "%.2f"))
    {
        ctrl.DeadZone = deadZone;
        _configurationService.ApplyChange();
    }
    if(ImGui.IsItemHovered())
        ImGui.SetTooltip("Stick axis values below this threshold are ignored.\nRaise if you experience drift at rest.");

    // Sensitivity curve
    var curveNames = new[] { "Linear", "Quadratic", "Cubic" };
    int curveIndex = (int)ctrl.SensitivityCurve;
    if(ImGui.Combo("Sensitivity Curve", ref curveIndex, curveNames, curveNames.Length))
    {
        ctrl.SensitivityCurve = (Brio.Config.SensitivityCurve)curveIndex;
        _configurationService.ApplyChange();
    }
    if(ImGui.IsItemHovered())
        ImGui.SetTooltip("Quadratic gives finer control near center; Cubic even more so.");

    // Inversion
    bool invertX = ctrl.InvertX;
    if(ImGui.Checkbox("Invert Pan (X)", ref invertX))
    {
        ctrl.InvertX = invertX;
        _configurationService.ApplyChange();
    }
    ImGui.SameLine();
    bool invertY = ctrl.InvertY;
    if(ImGui.Checkbox("Invert Tilt (Y)", ref invertY))
    {
        ctrl.InvertY = invertY;
        _configurationService.ApplyChange();
    }

    ImGui.Spacing();

    // Speed multipliers
    float moveSpeed = ctrl.MoveSpeed;
    if(ImGui.DragFloat("Move Speed", ref moveSpeed, 0.01f, 0.1f, 5f, "%.2f"))
    {
        ctrl.MoveSpeed = moveSpeed;
        _configurationService.ApplyChange();
    }

    float rotateSpeed = ctrl.RotateSpeed;
    if(ImGui.DragFloat("Rotate Speed", ref rotateSpeed, 0.01f, 0.1f, 5f, "%.2f"))
    {
        ctrl.RotateSpeed = rotateSpeed;
        _configurationService.ApplyChange();
    }

    float zoomSpeed = ctrl.ZoomSpeed;
    if(ImGui.DragFloat("Zoom Speed", ref zoomSpeed, 0.01f, 0.1f, 5f, "%.2f"))
    {
        ctrl.ZoomSpeed = zoomSpeed;
        _configurationService.ApplyChange();
    }

    float precisionMod = ctrl.PrecisionModifier;
    if(ImGui.DragFloat("Precision Modifier (L1)", ref precisionMod, 0.01f, 0.05f, 1f, "%.2f"))
    {
        ctrl.PrecisionModifier = precisionMod;
        _configurationService.ApplyChange();
    }
    if(ImGui.IsItemHovered())
        ImGui.SetTooltip("Speed multiplier applied when L1 is held. Default 0.25 = quarter speed.");

    ImGui.Unindent();
}
```

---

## 3. No dependency changes needed

`_configurationService` is already injected in `SettingsWindow`. The
`Configuration.Controller` property added in the Configuration.patch handles
the rest.
