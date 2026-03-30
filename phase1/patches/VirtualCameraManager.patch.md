# Patch: Brio/Game/Camera/VirtualCameraManager.cs

Wire `AnalogInputProvider` into the existing `Update()` pipeline so the
controller contributes to the same `_forward` and `_lastMousePosition`
vectors that keyboard/mouse already drive.

---

## 1. Constructor — add AnalogInputProvider field + inject it

```diff
 using Brio.Config;
 using Brio.Core;
 using Brio.Entities;
 using Brio.Entities.Camera;
 using Brio.Entities.Core;
 using Brio.Game.GPose;
 using Brio.Game.Input;
 using Brio.Input;
 using Microsoft.Extensions.DependencyInjection;
 using Swan;
 using System;
 using System.Collections.Generic;
 using System.Numerics;

 namespace Brio.Game.Camera;

 public class VirtualCameraManager : IDisposable
 {
     public VirtualCamera? CurrentCamera { get; private set; }
     public FreeCamValues FreeCamValues => CurrentCamera?.FreeCamValues!;

     public int CamerasCount => _createdCameras.Count;

     private readonly IServiceProvider _serviceProvider;
     private readonly GPoseService _gPoseService;
     private readonly EntityManager _entityManager;
     private readonly ConfigurationService _configurationService;
+    private readonly AnalogInputProvider _analogInput;

     private CameraEntity? DefaultCamera;
     private float _moveSpeed = 0.03f;
     private float DefaultMovementSpeed => _configurationService.Configuration.Interface.DefaultFreeCameraMovementSpeed;
     private float DefaultMouseSensitivity => _configurationService.Configuration.Interface.DefaultFreeCameraMouseSensitivity;

-    public VirtualCameraManager(IServiceProvider serviceProvider, GPoseService gPoseService, EntityManager entityManager, ConfigurationService configurationService)
+    public VirtualCameraManager(IServiceProvider serviceProvider, GPoseService gPoseService, EntityManager entityManager, ConfigurationService configurationService, AnalogInputProvider analogInput)
     {
         _serviceProvider = serviceProvider;
         _gPoseService = gPoseService;
         _entityManager = entityManager;
         _configurationService = configurationService;
+        _analogInput = analogInput;

         _gPoseService.OnGPoseStateChange += OnGPoseStateChange;
         _moveSpeed = configurationService.Configuration.Interface.DefaultFreeCameraMovementSpeed;
     }
```

---

## 2. Update() — blend analog input after the keyboard section

Find the existing `_forward = Vector3.Transform(...)` line at the end of `Update()`
and replace the block from the movement-speed section to that line:

```diff
-        // Handle movement speed
-        if(InputManagerService.ActionKeysPressed(InputAction.FreeCamera_IncreaseCamMovement))
-            _moveSpeed = CurrentCamera.FreeCamValues.MovementSpeed * 3;
-        else if(InputManagerService.ActionKeysPressed(InputAction.FreeCamera_DecreaseCamMovement))
-            _moveSpeed = CurrentCamera.FreeCamValues.MovementSpeed * 0.3f;
-
-        _forward = Vector3.Transform(new Vector3(leftRight, upDown, forwardBackward),
-            Quaternion.CreateFromYawPitchRoll(CurrentCamera!.Rotation.X, FreeCamValues.Move2D ? 0 : -CurrentCamera.Rotation.Y, CurrentCamera.Rotation.Z));
-    }
+        // Handle movement speed (keyboard)
+        if(InputManagerService.ActionKeysPressed(InputAction.FreeCamera_IncreaseCamMovement))
+            _moveSpeed = CurrentCamera.FreeCamValues.MovementSpeed * 3;
+        else if(InputManagerService.ActionKeysPressed(InputAction.FreeCamera_DecreaseCamMovement))
+            _moveSpeed = CurrentCamera.FreeCamValues.MovementSpeed * 0.3f;
+
+        // ── Controller analog input ──────────────────────────────────────────
+        // Sample once per frame. Deltas are already dead-zoned, curved, and
+        // speed-scaled relative to FreeCamValues.MovementSpeed.
+        var analog = _analogInput.Sample(FreeCamValues);
+
+        // Blend analog translation into the keyboard integers (floats after cast).
+        // Using float locals so we don't lose sub-integer analog precision.
+        float fbTotal = forwardBackward + analog.ForwardBackward;
+        float lrTotal = leftRight       + analog.LeftRight;
+        float udTotal = upDown          + analog.UpDown;
+
+        // Analog right-stick → rotation: accumulate into _lastMousePosition.
+        // UpdateMatrix() will scale _lastMousePosition by MouseSensitivity, so
+        // we pre-divide by it here so the controller's RotateSpeed is authoritative.
+        float mouseSens = CurrentCamera.FreeCamValues.MouseSensitivity;
+        if(mouseSens > 0f)
+            _lastMousePosition += analog.Rotation / mouseSens;
+
+        // Analog triggers → zoom: clamp within the camera's distance limits.
+        if(analog.ZoomDelta != 0f && CurrentCamera.IsActiveCamera)
+        {
+            unsafe
+            {
+                var cam = CurrentCamera.BrioCamera;
+                cam->Camera.Distance = Math.Clamp(
+                    cam->Camera.Distance + analog.ZoomDelta,
+                    cam->Camera.MinDistance,
+                    cam->Camera.MaxDistance);
+            }
+        }
+
+        _forward = Vector3.Transform(new Vector3(lrTotal, udTotal, fbTotal),
+            Quaternion.CreateFromYawPitchRoll(CurrentCamera!.Rotation.X, FreeCamValues.Move2D ? 0 : -CurrentCamera.Rotation.Y, CurrentCamera.Rotation.Z));
+    }
```

---

## 3. Service registration (Plugin.cs or wherever DI is configured)

Add `AnalogInputProvider` as a singleton so it can be injected:

```diff
 // In the service collection setup (look for where InputManagerService is registered)
 services.AddSingleton<InputManagerService>();
+services.AddSingleton<AnalogInputProvider>();
```

If Brio uses `ActivatorUtilities` rather than a DI container, construct it
manually alongside `InputManagerService`:

```csharp
var analogInput = new AnalogInputProvider(
    dalamudPluginService.GamepadState,
    configurationService);
```

---

## Notes

- `_moveSpeed` is still reset in `UpdateMatrix()` to `FreeCamValues.MovementSpeed`,
  so speed-modifier changes from the keyboard apply correctly for analog too.
- Analog forwardBackward is **negative Y** (up = forward on most sticks) — this
  matches the convention in `FreeCamValues` where `-1` is forward.
- The zoom block is `unsafe` because `BrioCamera*` is a native pointer. The
  surrounding `Update()` method is already marked `unsafe`.
