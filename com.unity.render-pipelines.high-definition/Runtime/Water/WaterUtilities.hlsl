// These values are chosen so that an iFFT patch of 1000km^2 will 
// yield a Phillips spectrum distribution in the [-1, 1] range
#define PHILLIPS_GRAVITY_CONSTANT     9.81f
#define PHILLIPS_PATCH_SCALAR         1000.0f
#define PHILLIPS_AMPLITUDE_SCALAR     15.0f
#define OCEAN_AMPLITUDE_NORMALIZATION  10.0f // The maximum waveheight a hurricane of wind 100km^2 can produce

// Ocean simulation data
Texture2DArray<float4> _DisplacementBuffer;
Texture2DArray<float4> _NormalBuffer;

// This array converts an index to the local coordinate shift of the half resolution texture
static const float2 vertexPostion[4] = {float2(0, 0), float2(0, 1), float2(1, 1), float2(1, 0)};
static const uint triangleIndices[6] = {0, 1, 2, 0, 2, 3};

//http://www.dspguide.com/ch2/6.htm
float GaussianDistribution(float u, float v)
{
    return sqrt(-2.0 * log(max(u, 1e-6f))) * cos(PI * v);
}

float Phillips(float2 k, float2 w, float V, float dirDampener)
{
    float kk = k.x * k.x + k.y * k.y;
    float result = 0.0;
    if (kk != 0.0)
    {
	    float L = (V * V) / PHILLIPS_GRAVITY_CONSTANT;
	    // To avoid _any_ directional bias when there is no wind we lerp towards 0.5f
	    float wk = lerp(dot(normalize(k), w), 0.5, dirDampener);
	    float phillips = (exp(-1.0f / (kk * L * L)) / (kk * kk)) * (wk * wk);
	    result = sqrt(phillips * (wk < 0.0f ? dirDampener : 1.0));
    }
    return result;
}

float2 ComplexExp(float arg)
{
    return float2(cos(arg), sin(arg));
}

float2 ComplexMult(float2 a, float2 b)
{
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

struct OceanSimulationCoordinates
{
    float2 uvBand0;
    float2 uvBand1;
    float2 uvBand2;
    float2 uvBand3;
};

void ComputeOceanUVs(float3 positionWS, out OceanSimulationCoordinates oceanCoord)
{
    float2 uv = positionWS.xz + _WindCurrent;
    uv /= _BandPatchSize.x;

    float R0 = _BandPatchUVScale.x;
    float O0 = 0 / R0;
    oceanCoord.uvBand0 = ((uv + O0) * R0);

    float R1 = _BandPatchUVScale.y;
    float O1 = 0.5f / R1;
    oceanCoord.uvBand1 = ((uv + O1) * R1);

    float R2 = _BandPatchUVScale.z;
    float O2 = 0.25 / R2;
    oceanCoord.uvBand2 = ((uv + O2) * R2);

    float R3 = _BandPatchUVScale.w;
    float O3 = 0.125 / R3;
    oceanCoord.uvBand3 = ((uv + O3) * R3);
}

#if !defined(WATER_SIMULATION)
float3 GetVertexPositionFromVertexID(uint vertexID, uint gridResolution, float3 patchOffset, float2 cameraOffset)
{
    // Compute the data about the quad of this vertex
    uint quadID = vertexID / 6;
    uint quadX = quadID / gridResolution;
    uint quadZ = quadID & (gridResolution - 1);

    // Evaluate the local position in the quad of this pixel
    int localVertexID = vertexID % 6;
    float2 localPos = vertexPostion[triangleIndices[localVertexID]];

    // Compute the position in the patch
    float3 worldPos = float3(localPos.x + quadX - gridResolution / 2, 0.0, localPos.y + quadZ - gridResolution / 2) / gridResolution;
    worldPos.x *= _GridSize.x;
    worldPos.z *= _GridSize.y;

    // If this is a local surface we do not want to move it with the camera
    cameraOffset = lerp(0.0, cameraOffset, _GlobalSurface);

    // Offset the tile and place it under the camera's relative position
    worldPos += float3(patchOffset.x + cameraOffset.x, patchOffset.y, patchOffset.z + cameraOffset.y);

    // Return the final world space position
    return worldPos;
}

struct OceanDisplacementData
{
    float3 displacement;
    float3 displacementNoChopiness;
    float lowFrequencyHeight;
    float foamFromHeight;
    float sssMask;
};

float EvaluateSSSMask(float3 positionWS, float3 cameraPositionWS)
{
    float3 viewWS = normalize(cameraPositionWS - positionWS);
    float distanceToCamera = distance(cameraPositionWS, positionWS);
    float angleWithOceanPlane = pow(saturate(viewWS.y), .2);
    return (1.f - exp(-distanceToCamera * _SSSMaskCoefficient)) * angleWithOceanPlane;
}

void EvaluateOceanDisplacement(float3 positionAWS, out OceanDisplacementData displacementData)
{
    // Compute the simulation coordinates
    OceanSimulationCoordinates oceanCoord;
    ComputeOceanUVs(positionAWS, oceanCoord);

    // Compute the displacement normalization factor
    float4 patchSizes = _BandPatchSize / PHILLIPS_PATCH_SCALAR;
    float4 patchSizes2 = patchSizes * patchSizes;
    float4 displacementNormalization = _PatchSizeScaleRatio * _WaveAmplitude / patchSizes;
    displacementNormalization *= OCEAN_AMPLITUDE_NORMALIZATION;

    // Accumulate the displacement from the various layers
    float3 totalDisplacement = 0.0;
    float3 totalDisplacementNoChopiness = 0.0;
    float lowFrequencyHeight = 0.0;
    float normalizedDisplacement = 0.0;
    float foamFromHeight = 0.0;

    // First band
    float3 rawDisplacement = SAMPLE_TEXTURE2D_ARRAY_LOD(_DisplacementBuffer, s_linear_repeat_sampler, oceanCoord.uvBand0, 0, 0).xyz * displacementNormalization.x;
    totalDisplacementNoChopiness += rawDisplacement;
    rawDisplacement.yz *= (_Choppiness.x / max(_WaveAmplitude.x, 0.00001f));
    totalDisplacement += rawDisplacement;
    lowFrequencyHeight += rawDisplacement.x;
    normalizedDisplacement = rawDisplacement.x / patchSizes2.x;
    foamFromHeight += pow(max(0, (1.f + normalizedDisplacement) * 0.5f * _FoamFromHeightWeights.x), _FoamFromHeightFalloff.x);

    // Second band
    rawDisplacement = SAMPLE_TEXTURE2D_ARRAY_LOD(_DisplacementBuffer, s_linear_repeat_sampler, oceanCoord.uvBand1, 1, 0).xyz * displacementNormalization.y;
    totalDisplacementNoChopiness += rawDisplacement;
    rawDisplacement.yz *= (_Choppiness.y / max(_WaveAmplitude.y, 0.00001f));
    totalDisplacement += rawDisplacement;
    lowFrequencyHeight += rawDisplacement.x * 0.75;
    normalizedDisplacement = rawDisplacement.x / patchSizes2.y;
    foamFromHeight += pow(max(0, (1.f + normalizedDisplacement) * 0.5f * _FoamFromHeightWeights.y), _FoamFromHeightFalloff.y);

#if defined(HIGH_RESOLUTION_WATER)
    // Third band
    rawDisplacement = SAMPLE_TEXTURE2D_ARRAY_LOD(_DisplacementBuffer, s_linear_repeat_sampler, oceanCoord.uvBand2, 2, 0).xyz * displacementNormalization.z;
    totalDisplacementNoChopiness += rawDisplacement;
    rawDisplacement.yz *= (_Choppiness.z / max(_WaveAmplitude.z, 0.00001f));
    totalDisplacement += rawDisplacement;
    lowFrequencyHeight += rawDisplacement.x * 0.5;
    normalizedDisplacement = rawDisplacement.x / patchSizes2.z;
    foamFromHeight += pow(max(0, (1.f + normalizedDisplacement) * 0.5f * _FoamFromHeightWeights.z), _FoamFromHeightFalloff.z);

    // Fourth band
    rawDisplacement = SAMPLE_TEXTURE2D_ARRAY_LOD(_DisplacementBuffer, s_linear_repeat_sampler, oceanCoord.uvBand3, 3, 0).xyz * displacementNormalization.w;
    totalDisplacementNoChopiness += rawDisplacement;
    rawDisplacement.yz *= (_Choppiness.w / max(_WaveAmplitude.w, 0.00001f));
    totalDisplacement += rawDisplacement;
    lowFrequencyHeight += rawDisplacement.x * 0.25;
    normalizedDisplacement = rawDisplacement.x / patchSizes2.w;
    foamFromHeight += pow(max(0, (1.f + normalizedDisplacement) * 0.5f * _FoamFromHeightWeights.w), _FoamFromHeightFalloff.w);
#endif

    // The vertical displacement is stored in the X channel and the XZ displacement in the YZ channel
    displacementData.displacement = float3(-totalDisplacement.y, totalDisplacement.x, totalDisplacement.z);
    displacementData.displacementNoChopiness = float3(-totalDisplacementNoChopiness.y, totalDisplacementNoChopiness.x - positionAWS.y, totalDisplacementNoChopiness.z);
    displacementData.lowFrequencyHeight = (_MaxWaveHeight - lowFrequencyHeight) / _MaxWaveHeight - 0.5f + _WaveTipsScatteringOffset;
    displacementData.foamFromHeight = foamFromHeight;
    displacementData.sssMask = EvaluateSSSMask(positionAWS, _WorldSpaceCameraPos);
}

struct OceanAdditionalData
{
    float3 surfaceGradient;
    float3 lowFrequencySurfaceGradient;
    float3 phaseSurfaceGradient;
    float simulationFoam;
};

// Should this be brought back in HDRP's 'ShaderLibrary\Filtering.hlsl'?
float4 SampleTexture2DBicubic(TEXTURE2D_ARRAY_PARAM(tex, smp), float2 coord, float slice, float4 texSize)
{
    float2 xy = coord * texSize.xy + 0.5;
    float2 ic = floor(xy);
    float2 fc = frac(xy);

    float2 weights[2], offsets[2];
    BicubicFilter(fc, weights, offsets);

    return weights[0].y * (weights[0].x * SAMPLE_TEXTURE2D_ARRAY(tex, smp, (ic + float2(offsets[0].x, offsets[0].y) - 0.5) * texSize.zw, slice).rgba  +
                           weights[1].x * SAMPLE_TEXTURE2D_ARRAY(tex, smp, (ic + float2(offsets[1].x, offsets[0].y) - 0.5) * texSize.zw, slice).rgba) +
           weights[1].y * (weights[0].x * SAMPLE_TEXTURE2D_ARRAY(tex, smp, (ic + float2(offsets[0].x, offsets[1].y) - 0.5) * texSize.zw, slice).rgba  +
                           weights[1].x * SAMPLE_TEXTURE2D_ARRAY(tex, smp, (ic + float2(offsets[1].x, offsets[1].y) - 0.5) * texSize.zw, slice).rgba);
}

void EvaluateOceanAdditionalData(float3 positionAWS, out OceanAdditionalData oceanAdditionalData)
{
    // Compute the simulation coordinates
    OceanSimulationCoordinates oceanCoord;
    ComputeOceanUVs(positionAWS, oceanCoord);

    // Compute the texture size param for the filtering
    float4 texSize = 0.0;
    texSize.xy = _BandResolution;
    texSize.zw = 1.0f / _BandResolution;

    // First band
    float4 additionalData = SampleTexture2DBicubic(TEXTURE2D_ARRAY_ARGS(_NormalBuffer, s_linear_repeat_sampler), oceanCoord.uvBand0, 0, texSize);
    float3 surfaceGradient = float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.x;
    float3 lowFrequencySurfaceGradient = surfaceGradient;
    float3 phaseSurfaceGradient = surfaceGradient;
    float foam = additionalData.z;

    // Second band
    additionalData = SampleTexture2DBicubic(TEXTURE2D_ARRAY_ARGS(_NormalBuffer, s_linear_repeat_sampler), oceanCoord.uvBand1, 1, texSize);
    surfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.y;
    lowFrequencySurfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.y * 0.75;
    phaseSurfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.y * 0.75;
    foam += additionalData.z ;

#if defined(HIGH_RESOLUTION_WATER)
    // Third band
    additionalData = SampleTexture2DBicubic(TEXTURE2D_ARRAY_ARGS(_NormalBuffer, s_linear_repeat_sampler), oceanCoord.uvBand2, 2, texSize);
    surfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.z;
    lowFrequencySurfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.z * 0.5;
    phaseSurfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.z * 0.5;
    foam += additionalData.z;

    // Fourth band
    additionalData = SampleTexture2DBicubic(TEXTURE2D_ARRAY_ARGS(_NormalBuffer, s_linear_repeat_sampler), oceanCoord.uvBand3, 3, texSize);
    surfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.w;
    lowFrequencySurfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.w * 0.25;
    phaseSurfaceGradient += float3(additionalData.x, 0, additionalData.y) * _WaveAmplitude.w * 0.25;
    foam += additionalData.z;
#endif

    // Blend the various surface gradients
    oceanAdditionalData.surfaceGradient = surfaceGradient;
    oceanAdditionalData.lowFrequencySurfaceGradient = lowFrequencySurfaceGradient;
    oceanAdditionalData.phaseSurfaceGradient = phaseSurfaceGradient;
    oceanAdditionalData.simulationFoam = foam;
}

float3 ComputeDebugNormal(float3 worldPos)
{
    float3 worldPosDdx = normalize(ddx(worldPos));
    float3 worldPosDdy = normalize(ddy(worldPos));
    return normalize(-cross(worldPosDdx, worldPosDdy));
}

float2 EvaluateFoamUV(float3 positionAWS)
{
    return positionAWS.xz / 10 + _FoamOffsets.xy;
}

struct FoamData
{
    float foamValue;
    float foamTransition;
    float3 foamSurfaceGradient;
};

void EvaluateFoamData(float3 foamNormal, float3 lowFrequencySurfaceGradient, 
    float surfaceFoam, float shallowFoam, float simulationFoam, float foamFromHeight, float lowFrequencyHeight,
    float3 inputNormalWS, out FoamData foamData)
{   
    float  normalizedDistanceToMaxWaveHeightPlane = max(0.01, lowFrequencyHeight);
    float foamFromHeightFalloff = pow(max(0, normalizedDistanceToMaxWaveHeightPlane), _FoamFromHeightMinMaxFalloff.z);
    float simulationFoamValue = simulationFoam * simulationFoam + foamFromHeight;
    simulationFoamValue *= lerp(_FoamFromHeightMinMaxFalloff.y, _FoamFromHeightMinMaxFalloff.x, foamFromHeightFalloff);

    float dilatedShallowFoam = max(0.00001, simulationFoamValue * _CloudTexturedDilation);

    float deepFoam = _DeepFoamAmount * simulationFoamValue * 0.1;
    float shallowFoamV = _ShallowFoamAmount * saturate((shallowFoam - (1 - dilatedShallowFoam)) / dilatedShallowFoam);
    float dilatedSurfaceFoam = pow(max(0, simulationFoam * _SurfaceFoamDilation), _SurfaceFoamFalloff);
    foamData.foamValue = simulationFoam;

    foamData.foamTransition = foamData.foamValue;

    float3 surfaceFoamNormals = foamNormal.xyz;
    surfaceFoamNormals -= float3(0.5, 0.5, 0);
    surfaceFoamNormals = normalize(surfaceFoamNormals.xzy);
    
    // Compute the surface gradient of the foam
    foamData.foamSurfaceGradient = SurfaceGradientFromPerturbedNormal(inputNormalWS, surfaceFoamNormals) * _SurfaceFoamNormalsWeight + lowFrequencySurfaceGradient;
}
#endif
