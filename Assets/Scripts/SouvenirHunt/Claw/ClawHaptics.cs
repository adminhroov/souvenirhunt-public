using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace SouvenirHunt
{
    /// <summary>
    /// Beam Pro 진동. SDK가 리센터 때 쓰는 것과 같은 경로(XR InputDevice.SendHapticImpulse) — XREAL 타입 의존 없음.
    /// 에디터에선 무동작. 실패해도 게임을 멈추지 않는다.
    /// </summary>
    public static class ClawHaptics
    {
        static InputDevice device;
        static float nextRefresh;
        static readonly List<InputDevice> buffer = new();

        public static void Pulse(float amplitude, float durationSec)
        {
#if UNITY_EDITOR
            return;
#else
            try
            {
                if (!device.isValid && Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 1f;
                    buffer.Clear();
                    InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, buffer);
                    if (buffer.Count > 0) device = buffer[0];
                }
                if (device.isValid) device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), durationSec);
            }
            catch (System.Exception) { /* 진동 실패는 무시 */ }
#endif
        }
    }
}
