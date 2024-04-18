using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum RenderMode
{
    ObjectSpaceShading,
    Reference,
    ScreenSpaceReSTIR,
}

[ExecuteInEditMode]
public class RenderSetting : MonoBehaviour
{
    public RenderMode RenderMode = RenderMode.ObjectSpaceShading;

    public bool EnableAccumulation = true;

    public bool LightingOnly = false;

    [Range(-8, 8)]
    public float Exposure = 0;

    float prevExposure;
    bool prevLightingOnly;
    bool prevEnableAccumulation;
    RenderMode prevRenderMode;

    [NonSerialized]
    public bool dirty = true;

    private void Update()
    {
        if (!Mathf.Approximately(prevExposure, Exposure)
            || prevLightingOnly != LightingOnly
            || prevEnableAccumulation != EnableAccumulation
            || prevRenderMode != RenderMode)
        {
            dirty = true;
        }

        prevExposure = Exposure;
        prevLightingOnly = LightingOnly;
        prevEnableAccumulation = EnableAccumulation;
        prevRenderMode = RenderMode;
    }
}
