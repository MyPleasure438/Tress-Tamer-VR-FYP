// HANDOFF NOTE:
// Optional hair-cap stubble shader used as scalp/buzz support only.
// Realistic short hair still needs authored hair cards/textures; keep this simple for VR.
Shader "Custom/Hair/Hair Cap Stubble"
{
    Properties
    {
        _ScalpColor ("Scalp Color", Color) = (0.72, 0.48, 0.36, 1)
        _StubbleColor ("Stubble Color", Color) = (0.09, 0.045, 0.025, 1)
        _DensityMask ("Density Mask", 2D) = "white" {}
        _StubbleTex ("Stubble Texture", 2D) = "black" {}
        _FlowMap ("Flow Map", 2D) = "gray" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _Density ("Stubble Density", Range(0, 1)) = 0.42
        _Darkness ("Stubble Darkness", Range(0, 3)) = 0.8
        _StubbleScale ("Stubble Scale", Range(20, 650)) = 360
        _StrandLength ("Strand Length", Range(0.02, 1)) = 0.16
        _StrandThinness ("Strand Thinness", Range(0.002, 0.25)) = 0.018
        _NoiseStrength ("Fine Noise Strength", Range(0, 1)) = 0.38
        _DirectionAngle ("Direction Angle", Range(0, 360)) = 90
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _Softness ("Stubble Softness", Range(0, 1)) = 0.65
        _StubbleTextureStrength ("Stubble Texture Strength", Range(0, 1)) = 1
        _FlowMapStrength ("Flow Map Strength", Range(0, 1)) = 0
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0
        _TextureContrast ("Texture Contrast", Range(0.1, 8)) = 3
        _BaseBuzzShadow ("Base Buzz Shadow", Range(0, 1)) = 0.18
        _TextureOnlyDebug ("Texture Only Debug", Range(0, 1)) = 0
        _UseWorldProjection ("Use World Projection", Range(0, 1)) = 1
        _WorldTextureScale ("World Texture Scale", Range(1, 80)) = 18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "HairCapStubble"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 tangentWS : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv : TEXCOORD4;
            };

            TEXTURE2D(_DensityMask);
            SAMPLER(sampler_DensityMask);
            TEXTURE2D(_StubbleTex);
            SAMPLER(sampler_StubbleTex);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _ScalpColor;
                float4 _StubbleColor;
                float4 _DensityMask_ST;
                float4 _StubbleTex_ST;
                float4 _FlowMap_ST;
                float4 _NormalMap_ST;
                float _Density;
                float _Darkness;
                float _StubbleScale;
                float _StrandLength;
                float _StrandThinness;
                float _NoiseStrength;
                float _DirectionAngle;
                float _Opacity;
                float _Smoothness;
                float _Softness;
                float _StubbleTextureStrength;
                float _FlowMapStrength;
                float _NormalStrength;
                float _TextureContrast;
                float _BaseBuzzShadow;
                float _TextureOnlyDebug;
                float _UseWorldProjection;
                float _WorldTextureScale;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Hash22(float2 p)
            {
                float n = Hash21(p);
                return float2(n, Hash21(p + n + 19.19));
            }

            float ValueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float2 RotateUv(float2 uv, float angleDegrees)
            {
                float angle = angleDegrees * 0.01745329252;
                float s = sin(angle);
                float c = cos(angle);
                uv -= 0.5;
                uv = float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
                return uv + 0.5;
            }

            float2 OrientUv(float2 uv, float2 direction)
            {
                direction = normalize(direction);
                float2 side = float2(-direction.y, direction.x);
                return float2(dot(uv, side), dot(uv, direction));
            }

            float StrandLayer(float2 uv, float scale, float density, float thinness, float length, float seedOffset)
            {
                float2 scaledUv = uv * scale;
                float2 cell = floor(scaledUv);
                float2 local = frac(scaledUv) - 0.5;
                float2 random = Hash22(cell + seedOffset);

                float strandEnabled = step(1.0 - density, random.x);
                float centerOffset = (random.y - 0.5) * 0.75;
                float width = max(0.002, thinness * lerp(0.65, 1.35, random.x));
                float strandShape = 1.0 - smoothstep(width, width * 2.5, abs(local.x - centerOffset));

                float halfLength = max(0.02, length * lerp(0.25, 0.75, random.y));
                float lengthMask = 1.0 - smoothstep(halfLength, halfLength + 0.15, abs(local.y));

                return strandShape * lengthMask * strandEnabled;
            }

            float FollicleLayer(float2 uv, float scale, float density, float seedOffset)
            {
                float2 scaledUv = uv * scale;
                float2 cell = floor(scaledUv);
                float2 local = frac(scaledUv);
                float2 random = Hash22(cell + seedOffset);

                float enabled = step(1.0 - saturate(density), random.x);
                float2 center = lerp(float2(0.32, 0.32), float2(0.68, 0.68), random);
                float radius = lerp(0.035, 0.09, random.y);
                float dotMask = 1.0 - smoothstep(radius, radius * 2.4, distance(local, center));

                float dashWidth = lerp(0.018, 0.045, random.x);
                float dashLength = lerp(0.09, 0.2, random.y);
                float2 dashLocal = local - center;
                float dashMask = (1.0 - smoothstep(dashWidth, dashWidth * 2.0, abs(dashLocal.x)))
                    * (1.0 - smoothstep(dashLength, dashLength * 1.7, abs(dashLocal.y)));

                return saturate(max(dotMask, dashMask * 0.45) * enabled);
            }

            float SampleTriplanarStubble(float3 positionWS, float3 normalWS, float textureTile)
            {
                float3 blendWeights = pow(abs(normalWS), 4.0);
                blendWeights /= max(0.0001, blendWeights.x + blendWeights.y + blendWeights.z);

                float worldScale = max(0.01, _WorldTextureScale) * textureTile;
                float2 uvX = positionWS.zy * worldScale;
                float2 uvY = positionWS.xz * worldScale;
                float2 uvZ = positionWS.xy * worldScale;

                float sampleX = SAMPLE_TEXTURE2D(_StubbleTex, sampler_StubbleTex, uvX).r;
                float sampleY = SAMPLE_TEXTURE2D(_StubbleTex, sampler_StubbleTex, uvY).r;
                float sampleZ = SAMPLE_TEXTURE2D(_StubbleTex, sampler_StubbleTex, uvZ).r;

                return sampleX * blendWeights.x + sampleY * blendWeights.y + sampleZ * blendWeights.z;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.bitangentWS = normalInputs.bitangentWS;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float densityMask = SAMPLE_TEXTURE2D(_DensityMask, sampler_DensityMask, TRANSFORM_TEX(input.uv, _DensityMask)).r;
                float2 flowSample = SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, TRANSFORM_TEX(input.uv, _FlowMap)).rg * 2.0 - 1.0;
                float baseAngle = _DirectionAngle * 0.01745329252;
                float2 baseDirection = float2(cos(baseAngle), sin(baseAngle));
                float2 flowDirection = dot(flowSample, flowSample) > 0.01 ? normalize(flowSample) : baseDirection;
                float2 finalDirection = normalize(lerp(baseDirection, flowDirection, saturate(_FlowMapStrength)));
                float2 directedUv = OrientUv(input.uv, finalDirection);
                float density = saturate(_Density * densityMask);
                float thinness = max(0.001, _StrandThinness);

                float strandA = StrandLayer(directedUv, _StubbleScale, density, thinness, _StrandLength, 0.0);
                float strandB = StrandLayer(directedUv + float2(0.137, 0.271), _StubbleScale * 1.9, density * 0.45, thinness * 0.65, _StrandLength * 0.55, 37.0);
                float strandMask = saturate(strandA * 0.45 + strandB * 0.28);
                float follicleA = FollicleLayer(directedUv, _StubbleScale * 3.8, density * 0.92, 11.0);
                float follicleB = FollicleLayer(directedUv + float2(0.219, 0.371), _StubbleScale * 5.6, density * 0.45, 71.0);
                float follicleMask = saturate(follicleA * 0.75 + follicleB * 0.3);

                float fineNoise = ValueNoise(directedUv * _StubbleScale * 0.35);
                float poreNoise = ValueNoise(directedUv * _StubbleScale * 1.25 + 17.0);
                float softStubble = saturate((fineNoise * 0.55 + poreNoise * 0.45) * density);
                float textureTile = max(1.0, _StubbleScale / 140.0);
                float2 tiledStubbleUv = input.uv * textureTile;
                float uvTextureRaw = SAMPLE_TEXTURE2D(_StubbleTex, sampler_StubbleTex, TRANSFORM_TEX(tiledStubbleUv, _StubbleTex)).r;
                float worldTextureRaw = SampleTriplanarStubble(input.positionWS, normalize(input.normalWS), textureTile);
                float textureRaw = lerp(uvTextureRaw, worldTextureRaw, saturate(_UseWorldProjection));
                float darkTextureStubble = saturate((0.82 - textureRaw) * _TextureContrast);
                float softTextureStubble = saturate((0.62 - textureRaw) * _TextureContrast * 0.55);
                float brightTextureStubble = saturate((textureRaw - 0.52) * _TextureContrast * 0.12);
                float textureStubble = saturate(max(darkTextureStubble, max(softTextureStubble, brightTextureStubble))) * densityMask;
                textureStubble = pow(textureStubble, 0.72) * 0.32;

                float fineGrain = saturate((fineNoise * 0.62 + poreNoise * 0.38) * density * _NoiseStrength);
                float generatedStubble = saturate((follicleMask * 0.38) + (strandMask * _Darkness * 0.18) + fineGrain * 0.32);
                float baseBuzzShadow = density * _BaseBuzzShadow;
                float stubbleAmount = lerp(generatedStubble, saturate(generatedStubble + textureStubble), saturate(_StubbleTextureStrength));
                stubbleAmount = saturate(max(stubbleAmount, baseBuzzShadow) * _Darkness);
                stubbleAmount = saturate(stubbleAmount + fineGrain * 0.08);
                stubbleAmount = lerp(stubbleAmount, smoothstep(0.02, 0.72, stubbleAmount), saturate(_Softness) * 0.55);

                float3 baseColor = lerp(_ScalpColor.rgb, _StubbleColor.rgb, stubbleAmount);
                float3 debugColor = textureRaw.xxx;
                baseColor = lerp(baseColor, debugColor, saturate(_TextureOnlyDebug));

                Light mainLight = GetMainLight();
                float3 normalWS = normalize(input.normalWS);
                float3 tangentNormal = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, TRANSFORM_TEX(input.uv, _NormalMap)).xyz * 2.0 - 1.0;
                tangentNormal.xy *= _NormalStrength;
                tangentNormal.z = max(0.001, tangentNormal.z);
                tangentNormal = normalize(tangentNormal);
                float3x3 tangentToWorld = float3x3(normalize(input.tangentWS), normalize(input.bitangentWS), normalWS);
                normalWS = normalize(lerp(normalWS, mul(tangentNormal, tangentToWorld), saturate(_NormalStrength)));
                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float lightAmount = lerp(0.55, 1.0, ndotl);
                float3 litColor = baseColor * (lightAmount * mainLight.color + float3(0.35, 0.35, 0.35));

                float3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                float rim = pow(1.0 - saturate(dot(normalWS, viewDirection)), 3.0) * _Smoothness;
                litColor += rim * 0.08;

                return half4(litColor, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
