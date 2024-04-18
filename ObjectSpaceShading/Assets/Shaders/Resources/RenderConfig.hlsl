#ifndef RENDER_CONFIG_HLSL
#define RENDER_CONFIG_HLSL

/*
Do not modify
*/
static const int _VRT_ShadelGroupSize = 8;
static const int _VRT_ShadelSize = 8;
static const int _VRT_PersistentLayerLODIndex = 3;

/*
Configurable
*/
static const bool _VRT_MipmapFiltering = true;
static const float _VRT_MipBias = 0.0f;

static const bool _UseIrradianceCaching = true;

static const uint _MaxAccumulatedFrameCount = 8;

static const float _ObjectSpaceSpatialReuseSearchRadius = 12;

/*
Set By Code
*/
int _EnablePersistentLayer;
int _VisualizationMode;

#endif // RENDER_CONFIG_HLSL
