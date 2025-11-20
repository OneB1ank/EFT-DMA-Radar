using System.Numerics;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.GameWorld
{
    public sealed class CameraManager
    {
        private static ulong _fpsCamera;
        private static ulong _opticCamera;
        private static bool _isInitialized;

        public float FOV { get; private set; } = 60f;
        public float AspectRatio { get; private set; } = 1.777f;
        public Matrix4x4 ViewMatrix { get; private set; } = Matrix4x4.Identity;
        public bool IsADS { get; private set; }

        private const uint CameraObjectManager = 0x19EE080;
        private const uint ViewMatrixOffset = 0x118;
        private const uint FOVOffset = 0x198;
        private const uint AspectOffset = 0x170;

        public static bool TryInitialize()
        {
            if (_isInitialized)
                return true;

            try
            {
                DebugLogger.LogDebug("CameraManager: Starting initialization...");
                
                var addr = Memory.ReadPtr(Memory.UnityBase + CameraObjectManager, false);
                if (addr == 0)
                {
                    DebugLogger.LogWarning("CameraManager: Failed to read CameraObjectManager address");
                    return false;
                }

                var cameraManager = Memory.ReadPtr(addr, false);
                if (cameraManager == 0)
                {
                    DebugLogger.LogWarning("CameraManager: Failed to read cameraManager pointer");
                    return false;
                }

                DebugLogger.LogDebug($"CameraManager: Searching for cameras at 0x{cameraManager:X}");
                int camerasFound = 0;

                for (int i = 0; i < 100; i++)
                {
                    var camera = Memory.ReadPtr(cameraManager + (ulong)i * 0x8, false);
                    if (camera == 0)
                        continue;

                    Span<uint> nameChain = stackalloc uint[] { 0x48, 0x78 };
                    var namePtr = Memory.ReadPtrChain(camera, false, nameChain);
                    
                    if (namePtr == 0)
                        continue;

                    var name = Memory.ReadUtf8String(namePtr, 128, false);
                    
                    if (!string.IsNullOrEmpty(name))
                    {
                        camerasFound++;
                        if (camerasFound <= 10)
                            DebugLogger.LogDebug($"CameraManager: Found GameObject[{i}] = '{name}'");
                    }

                    if (name == "FPS Camera")
                    {
                        _fpsCamera = camera;
                        DebugLogger.LogInfo($"CameraManager: Found FPS Camera at 0x{camera:X}");
                    }
                    else if (name == "BaseOpticCamera(Clone)")
                    {
                        _opticCamera = camera;
                        DebugLogger.LogInfo($"CameraManager: Found BaseOpticCamera at 0x{camera:X}");
                    }

                    if (_fpsCamera != 0 && _opticCamera != 0)
                    {
                        _isInitialized = true;
                        DebugLogger.LogInfo("CameraManager: Successfully initialized with both cameras");
                        return true;
                    }
                }

                if (_fpsCamera != 0)
                {
                    _isInitialized = true;
                    DebugLogger.LogInfo("CameraManager: Successfully initialized with FPS Camera only");
                    return true;
                }

                DebugLogger.LogWarning($"CameraManager: No cameras found (scanned {camerasFound} objects)");
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.LogException(ex, "CameraManager.TryInitialize");
                return false;
            }
        }


        public void Update(Player.LocalPlayer localPlayer)
        {
            if (!_isInitialized || _fpsCamera == 0)
            {
                TryInitialize();
                return;
            }

            try
            {
                IsADS = localPlayer?.IsAiming ?? false;

                ulong activeCamera = IsADS && _opticCamera != 0 ? _opticCamera : _fpsCamera;

                FOV = Memory.ReadValue<float>(activeCamera + FOVOffset, false);
                AspectRatio = Memory.ReadValue<float>(activeCamera + AspectOffset, false);
                ViewMatrix = Memory.ReadValue<Matrix4x4>(activeCamera + ViewMatrixOffset, false);

                if (FOV < 1f || FOV > 180f)
                    FOV = 60f;
                if (AspectRatio < 0.1f || AspectRatio > 10f)
                    AspectRatio = 1.777f;
            }
            catch (Exception ex)
            {
                DebugLogger.LogException(ex, "CameraManager.Update");
            }
        }
    }
}

