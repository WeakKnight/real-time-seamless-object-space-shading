#ifndef ANALYTICAL_SKY_CGINC
#define ANALYTICAL_SKY_CGINC

// HosekWilkieSkyModel https://cgg.mff.cuni.cz/projects/SkylightModelling/HosekWilkie_SkylightModel_SIGGRAPH2012_Preprint_lowres.pdf

float3 ProceduralSky_A;
float3 ProceduralSky_B;
float3 ProceduralSky_C;
float3 ProceduralSky_D;
float3 ProceduralSky_E;
float3 ProceduralSky_F;
float3 ProceduralSky_G;
float3 ProceduralSky_H;
float3 ProceduralSky_I; 
float3 ProceduralSky_Z;

float3 ProceduralSky_SunDirection;

float3 hosek_wilkie(float cos_theta, float gamma, float cos_gamma)
{
    float3 chi = (1 + cos_gamma * cos_gamma) / pow(1 + ProceduralSky_H * ProceduralSky_H - 2 * cos_gamma * ProceduralSky_H, float3(1.5f, 1.5f, 1.5f));
    return (1 + ProceduralSky_A * exp(ProceduralSky_B / (cos_theta + 0.01))) * (ProceduralSky_C + ProceduralSky_D * exp(ProceduralSky_E * gamma) + ProceduralSky_F * (cos_gamma * cos_gamma) + ProceduralSky_G * chi + ProceduralSky_I * sqrt(cos_theta));
}

// ------------------------------------------------------------------
float3 hosek_wilkie_sky_rgb(float3 v)
{
    float cos_theta = clamp(v.y, 0, 1);
    float cos_gamma = clamp(dot(v, ProceduralSky_SunDirection), 0, 1);
    float gamma_ = acos(cos_gamma);

    float3 R = ProceduralSky_Z * hosek_wilkie(cos_theta, gamma_, cos_gamma);
    return R;
}

#endif // ANALYTICAL_SKY_CGINC