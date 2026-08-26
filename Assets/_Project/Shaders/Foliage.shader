// Vent/Foliage — the one shader every leaf, blade and vine in the game uses.
//
// A URP 17 forward shader written against the same library the Lit shader uses (Lighting.hlsl,
// Shadows.hlsl, SurfaceInput.hlsl), so it gets Forward+ clustering, cascaded soft shadows, light
// layers, SSAO, light probes and fog for free, and adds what leaves need:
//
//   * alpha-tested cutout cards from the generated leaf atlas (TextureFactory.FoliageAtlas),
//     rendered from both sides with the normal flipped for the back face;
//   * a tangent-space normal map for the veins and the dome of each leaf;
//   * wind in the vertex shader, driven by vertex colour so it survives static batching:
//       r = how far up the plant this vertex is (the whole plant leans with the gust),
//       g = a random phase per leaf (also picks a tint between _BaseColor and _VariationColor),
//       b = how loose the leaf is (fine flutter along its normal),
//       a = occlusion baked by the mesh builder (dark inside the canopy, at the base of a tuft);
//   * translucency: light that comes through the leaf toward the eye, from the main light and the
//     additional lights, gated by each light's shadow and rendering layer so the sun never glows
//     through an office plant that it does not light.
//
// Foliage is never lightmapped (FoliageLibrary marks it probe-lit), so there are no lightmap
// variants; the Meta and MotionVectors passes are left out for the same reason.
Shader "Vent/Foliage"
{
    Properties
    {
        [MainTexture] _BaseMap("Leaf Atlas (RGB, A = cutout)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _VariationColor("Per-leaf variation (A = amount)", Color) = (0.9, 0.95, 0.75, 0.5)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.45
        _GrazingFade("Edge-on Leaf Fade", Range(0.0, 1.0)) = 0.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.35
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1.0
        _OcclusionStrength("Baked Occlusion Strength", Range(0.0, 1.0)) = 1.0
        _WindDirection("Wind Direction (xz)", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Lean (m at the top)", Range(0.0, 1.0)) = 0.2
        _WindSpeed("Wind Speed", Range(0.0, 5.0)) = 1.0
        _WindGustScale("Gust Size (1/m)", Range(0.0, 1.0)) = 0.12
        _FlutterStrength("Leaf Flutter (m)", Range(0.0, 0.2)) = 0.03
        _FlutterSpeed("Flutter Speed", Range(0.0, 20.0)) = 4.5
        _TranslucencyColor("Translucency Colour", Color) = (1.0, 0.95, 0.6, 1)
        _Translucency("Translucency", Range(0.0, 2.0)) = 0.45
        _TranslucencyPower("Translucency Focus", Range(1.0, 16.0)) = 3.0
        _Wrap("Diffuse Wrap", Range(0.0, 1.0)) = 0.4
        _SkyFill("Sky Fill", Range(0.0, 1.0)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #define _ALPHATEST_ON 1
        #define _NORMALMAP 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

        // Every material property in one block, identical in every pass: the SRP Batcher needs it.
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half4 _VariationColor;
        half _Cutoff;
        half _GrazingFade;
        half _Smoothness;
        half _BumpScale;
        half _OcclusionStrength;
        half4 _WindDirection;
        half _WindStrength;
        half _WindSpeed;
        half _WindGustScale;
        half _FlutterStrength;
        half _FlutterSpeed;
        half4 _TranslucencyColor;
        half _Translucency;
        half _TranslucencyPower;
        half _Wrap;
        half _SkyFill;
        CBUFFER_END

        // Wind in world space, so a static-batched leaf (whose object space is the world) moves the same
        // as a loose one. Two gusts of unrelated period travel along the wind direction; the plant leans
        // with their sum, scaled by the square of its height weight so the base stays planted.
        float3 ApplyWind(float3 positionWS, float3 normalWS, half4 color)
        {
            float2 dir = normalize(_WindDirection.xz + float2(1e-4, 0.0));
            float t = _Time.y * _WindSpeed;
            float phase = color.g * 6.2831853;
            float along = dot(positionWS.xz, dir) * _WindGustScale;
            float gust = sin(t - along + phase * 0.25) * 0.6 + sin(t * 0.37 - along * 0.5 + phase) * 0.4;
            float lean = _WindStrength * (0.55 + 0.45 * gust) * color.r * color.r;
            float3 bend = float3(dir.x, 0.0, dir.y) * lean;
            bend.y = -lean * lean * 0.5; // the tip drops as it leans; the stem keeps its length
            float flutter = sin(t * _FlutterSpeed + phase * 2.0 + positionWS.y * 3.0 + along * 2.0) * _FlutterStrength * color.b;
            return positionWS + bend + normalWS * flutter;
        }

        // A card turned edge-on has almost no projected area but still rasterises a full-width strip of
        // its atlas cell, which reads as a pale blade sticking out of the crown. Raising the cutout
        // threshold as the leaf turns away dissolves it instead. Off (0) for single big indoor leaves,
        // which are meant to be seen from any angle.
        half GrazingCutoff(float3 positionWS, half3 viewDirWS)
        {
            // The quad's own normal, taken from screen-space derivatives -- not the shading normal. Canopy
            // normals are bent radially out of the crown, so a card can claim to face the camera while its
            // quad is edge-on, compressed to a few pixels wide and smearing its atlas cell into a streak.
            float3 faceNormal = normalize(cross(ddy(positionWS), ddx(positionWS)));
            half facing = abs(dot(faceNormal, viewDirWS));
            return saturate(_Cutoff + _GrazingFade * (1.0 - smoothstep(0.0, 0.45, facing)));
        }

        VertexPositionInputs FoliagePositionInputs(float3 positionWS)
        {
            VertexPositionInputs input;
            input.positionWS = positionWS;
            input.positionVS = TransformWorldToView(positionWS);
            input.positionCS = TransformWorldToHClip(positionWS);
            float4 ndc = input.positionCS * 0.5f;
            input.positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
            input.positionNDC.zw = input.positionCS.zw;
            return input;
        }
        ENDHLSL

        // ------------------------------------------------------------------ forward
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One Zero
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex FoliageVertex
            #pragma fragment FoliageFragment

            // Universal Pipeline keywords (the Lit set, minus lightmaps and decals)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // Unity defined keywords
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 texcoord   : TEXCOORD0;
                half4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
                half4 tangentWS   : TEXCOORD3;
                half4 color       : TEXCOORD4;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD5;
            #else
                half fogFactor    : TEXCOORD5;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD8;
            #endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings FoliageVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz), normalInput.normalWS, input.color);
                VertexPositionInputs vertexInput = FoliagePositionInputs(positionWS);

                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif

                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                output.normalWS = normalInput.normalWS;
                real sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = half4(normalInput.tangentWS.xyz, sign);
                output.color = input.color;
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
            #else
                output.fogFactor = fogFactor;
            #endif
                output.positionWS = vertexInput.positionWS;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
            #endif
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            // Light passing through the leaf toward the eye: strongest when the light is behind the leaf.
            half3 Translucency(Light light, half3 viewDirWS, half3 normalWS)
            {
                half3 h = normalize(light.direction + normalWS * 0.3);
                half through = pow(saturate(dot(viewDirWS, -h)), _TranslucencyPower);
                return light.color * (light.distanceAttenuation * light.shadowAttenuation) * through * _Translucency;
            }

            void FoliageFragment(
                Varyings input
                , FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
                half alpha = Alpha(albedoAlpha.a, _BaseColor, GrazingCutoff(input.positionWS, viewDirWS));

                SurfaceData surfaceData = (SurfaceData)0;
                half3 variation = lerp(half3(1.0, 1.0, 1.0), _VariationColor.rgb, input.color.g * _VariationColor.a);
                surfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb * variation;
                surfaceData.alpha = alpha;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = SampleNormal(input.uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
                surfaceData.occlusion = LerpWhiteTo(input.color.a, _OcclusionStrength);

                // A leaf seen from behind is lit as a leaf facing the other way. The test is against the
                // view, not the rasteriser's face: canopy normals are bent toward the outside of the tree,
                // and flipping those by triangle winding turned half of every crown black.
                half3 normalWS = input.normalWS * (dot(input.normalWS, viewDirWS) < 0.0 ? -1.0 : 1.0);
                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.tangentToWorld = tangentToWorld;
                inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(surfaceData.normalTS, tangentToWorld));
                inputData.viewDirectionWS = viewDirWS;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
            #else
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
            #endif
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);
            #if defined(DEBUG_DISPLAY)
                inputData.positionCS = input.positionCS;
                inputData.vertexSH = input.vertexSH;
            #endif
            #if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS,
                    inputData.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
            #endif

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                half3 through = 0;
                uint meshRenderingLayers = GetMeshRenderingLayer();
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
            #ifdef _LIGHT_LAYERS
                if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
            #endif
                {
                    through += Translucency(mainLight, inputData.viewDirectionWS, inputData.normalWS);
                    // A leaf is thin: light wraps round its edge. Add what a wrapped diffuse gives beyond Lambert.
                    half ndl = dot(inputData.normalWS, mainLight.direction);
                    half wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                    wrapped *= wrapped;
                    half extra = max(0.0, wrapped - saturate(ndl));
                    color.rgb += mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation) * extra * surfaceData.albedo * surfaceData.occlusion;
                }

            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
            #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
            #endif
                    {
                        through += Translucency(light, inputData.viewDirectionWS, inputData.normalWS);
                    }
                }
            #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
            #endif
                    {
                        through += Translucency(light, inputData.viewDirectionWS, inputData.normalWS);
                    }
                LIGHT_LOOP_END
            #endif

                // Sky light reaches both faces of a leaf; the probes only counted one.
                color.rgb += inputData.bakedGI * surfaceData.albedo * surfaceData.occlusion * _SkyFill;
                color.rgb += through * surfaceData.albedo * _TranslucencyColor.rgb * surfaceData.occlusion;
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                outColor = half4(color.rgb, 1.0);

            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ shadows
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                half4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.color);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                // A two-sided card biases along whichever face looks at the light; the other way over-biases.
                if (dot(normalWS, lightDirectionWS) < 0.0)
                {
                    normalWS = -normalWS;
                }

                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ depth
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                half4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.color);
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half DepthFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor,
                    GrazingCutoff(input.positionWS, GetWorldSpaceNormalizeViewDir(input.positionWS)));
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ depth + normals (SSAO)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 tangentOS  : TANGENT;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                half4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz), normalInput.normalWS, input.color);
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);
                return output;
            }

            void DepthNormalsFragment(
                Varyings input
                , FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor,
                    GrazingCutoff(input.positionWS, GetWorldSpaceNormalizeViewDir(input.positionWS)));
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS) * IS_FRONT_VFACE(cullFace, 1.0, -1.0);
                outNormalWS = half4(normalWS, 0.0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
