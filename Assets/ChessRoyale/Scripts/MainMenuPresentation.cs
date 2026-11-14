using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>Keeps the palace composition intact on desktop and the existing tracked canvas in VR.</summary>
[RequireComponent(typeof(Canvas))]
public sealed class MainMenuPresentation : MonoBehaviour
{
    private void Start()
    {
        if (XRSettings.isDeviceActive)
            return;

        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }
}
