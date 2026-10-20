namespace Menhir.Editor.Rendering.OpenGL;

internal static class Shaders
{
    public const string MeshVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;

        uniform mat4 uModel;
        uniform mat4 uNormalMatrix;
        uniform mat4 uViewProjection;

        out vec3 vWorldPosition;
        out vec3 vNormal;

        void main()
        {
            vec4 world = uModel * vec4(aPosition, 1.0);
            vWorldPosition = world.xyz;
            vNormal = mat3(uNormalMatrix) * aNormal;
            gl_Position = uViewProjection * world;
        }
        """;

    // URP's Lit (simplified BRDF from URP's BRDF.hlsl): main light, up to 8 point / spot lights
    // with URP's attenuation, and ambient light from the sky as a three-color gradient.
    // Writes linear HDR color.
    public const string MeshFragment = """
        #version 330 core
        in vec3 vWorldPosition;
        in vec3 vNormal;

        uniform vec4 uBaseColor;
        uniform float uMetallic;
        uniform float uSmoothness;

        uniform vec3 uCameraPosition;
        uniform vec3 uToSun;
        uniform vec3 uSunColor;
        uniform vec3 uAmbientTop;
        uniform vec3 uAmbientHorizon;
        uniform vec3 uAmbientBottom;

        uniform int uLightCount;
        uniform vec4 uLightPositionRange[8];   // xyz position, w range
        uniform vec4 uLightColor[8];
        uniform vec4 uLightDirectionCos[8];    // xyz spot direction, w cos(outer) (-1: point light)
        uniform float uLightCosInner[8];

        // Shadows (see GLShadowMaps and Lighting.ShadowSetup).
        uniform int uReceiveShadows;
        uniform int uMainShadowOn;
        uniform sampler2DShadow uMainShadowMap;
        uniform int uCascadeCount;
        uniform mat4 uCascadeMatrix[4];
        uniform vec4 uCascadeSphere[4];   // center, squared radius
        uniform vec4 uCascadeRect[4];
        uniform vec4 uMainShadowParams;   // strength, filter taps, max distance, fade border
        uniform float uMainShadowTexel;

        uniform int uAdditionalShadowOn;
        uniform sampler2DShadow uAdditionalShadowMap;
        uniform mat4 uSliceMatrix[16];
        uniform vec4 uSliceRect[16];
        uniform float uAdditionalShadowTexel;
        uniform int uLightShadowSlice[8];  // first slice, -1 without shadows
        uniform vec2 uLightShadowParams[8]; // strength, filter taps

        out vec4 fragColor;

        struct Brdf
        {
            vec3 diffuse;
            vec3 specular;
            float roughness2;
            float normalizationTerm;
            float grazingTerm;
            float roughness2MinusOne;
        };

        vec3 Ambient(vec3 n)
        {
            float y = n.y;
            vec3 upper = mix(uAmbientHorizon, uAmbientTop, clamp(y, 0.0, 1.0));
            vec3 lower = mix(uAmbientHorizon, uAmbientBottom, clamp(-y, 0.0, 1.0));
            return y >= 0.0 ? upper : lower;
        }

        // Hardware 2x2 comparisons, on a grid of taps × taps for soft shadows (0: one tap, hard).
        // Taps stay inside the slice's tile so neighbors in the atlas do not bleed in.
        float SampleShadow(sampler2DShadow map, vec3 coord, vec4 rect, int taps, float texel)
        {
            if (coord.z >= 1.0)
                return 1.0;
            float margin = texel * (float(taps) * 0.5 + 1.0);
            coord.xy = clamp(coord.xy, rect.xy + margin, rect.zw - margin);
            if (taps == 0)
                return texture(map, coord);
            float sum = 0.0;
            float center = float(taps - 1) * 0.5;
            for (int y = 0; y < taps; y++)
            for (int x = 0; x < taps; x++)
                sum += texture(map, vec3(coord.xy + (vec2(x, y) - center) * texel, coord.z));
            return sum / float(taps * taps);
        }

        float MainLightShadow(vec3 position)
        {
            if (uMainShadowOn == 0 || uReceiveShadows == 0)
                return 1.0;
            int cascade = -1;
            for (int i = 0; i < uCascadeCount; i++)
            {
                vec3 d = position - uCascadeSphere[i].xyz;
                if (dot(d, d) < uCascadeSphere[i].w)
                {
                    cascade = i;
                    break;
                }
            }
            if (cascade < 0)
                return 1.0;
            vec4 coord = uCascadeMatrix[cascade] * vec4(position, 1.0);
            float shadow = SampleShadow(uMainShadowMap, coord.xyz / coord.w, uCascadeRect[cascade],
                int(uMainShadowParams.y), uMainShadowTexel);
            // Fade out towards Max Distance.
            float distance = length(position - uCameraPosition);
            float fade = clamp((uMainShadowParams.z - distance) / (uMainShadowParams.z * uMainShadowParams.w), 0.0, 1.0);
            return mix(1.0, shadow, uMainShadowParams.x * fade);
        }

        float AdditionalLightShadow(int light, vec3 position)
        {
            if (uAdditionalShadowOn == 0 || uReceiveShadows == 0 || uLightShadowSlice[light] < 0)
                return 1.0;
            int slice = uLightShadowSlice[light];
            if (uLightDirectionCos[light].w <= -1.0)
            {
                // Point light: the cube face facing the point (+X, -X, +Y, -Y, +Z, -Z).
                vec3 d = position - uLightPositionRange[light].xyz;
                vec3 a = abs(d);
                int face = a.x >= a.y && a.x >= a.z ? (d.x > 0.0 ? 0 : 1)
                         : a.y >= a.z ? (d.y > 0.0 ? 2 : 3)
                         : (d.z > 0.0 ? 4 : 5);
                slice += face;
            }
            vec4 coord = uSliceMatrix[slice] * vec4(position, 1.0);
            float shadow = SampleShadow(uAdditionalShadowMap, coord.xyz / coord.w, uSliceRect[slice],
                int(uLightShadowParams[light].y), uAdditionalShadowTexel);
            return mix(1.0, shadow, uLightShadowParams[light].x);
        }

        vec3 DirectLight(Brdf brdf, vec3 n, vec3 v, vec3 l, vec3 radiance)
        {
            vec3 h = normalize(l + v);
            float nh = clamp(dot(n, h), 0.0, 1.0);
            float lh = clamp(dot(l, h), 0.0, 1.0);
            float d = nh * nh * brdf.roughness2MinusOne + 1.00001;
            float specularTerm = brdf.roughness2 / ((d * d) * max(0.1, lh * lh) * brdf.normalizationTerm);
            return (brdf.diffuse + brdf.specular * specularTerm) * radiance;
        }

        void main()
        {
            vec3 n = normalize(vNormal);
            if (!gl_FrontFacing)
                n = -n;
            vec3 v = normalize(uCameraPosition - vWorldPosition);
            vec3 albedo = uBaseColor.rgb;

            float oneMinusReflectivity = 0.96 * (1.0 - uMetallic);
            float perceptualRoughness = 1.0 - uSmoothness;
            float roughness = max(perceptualRoughness * perceptualRoughness, 0.0078125);
            Brdf brdf;
            brdf.diffuse = albedo * oneMinusReflectivity;
            brdf.specular = mix(vec3(0.04), albedo, uMetallic);
            brdf.roughness2 = roughness * roughness;
            brdf.normalizationTerm = roughness * 4.0 + 2.0;
            brdf.grazingTerm = clamp(uSmoothness + (1.0 - oneMinusReflectivity), 0.0, 1.0);
            brdf.roughness2MinusOne = brdf.roughness2 - 1.0;

            // Indirect light: the sky's gradient for diffuse, and in the reflected direction for specular.
            float nv = clamp(dot(n, v), 0.0, 1.0);
            float fresnel = pow(1.0 - nv, 4.0);
            vec3 reflected = reflect(-v, n);
            vec3 indirectSpecular = mix(Ambient(reflected), Ambient(n), perceptualRoughness);
            vec3 color = Ambient(n) * brdf.diffuse
                + indirectSpecular * (1.0 / (brdf.roughness2 + 1.0)) * mix(brdf.specular, vec3(brdf.grazingTerm), fresnel);

            // Main light.
            float nl = clamp(dot(n, uToSun), 0.0, 1.0);
            color += DirectLight(brdf, n, v, uToSun, uSunColor * (nl * MainLightShadow(vWorldPosition)));

            // Additional lights.
            for (int i = 0; i < uLightCount; i++)
            {
                vec3 toLight = uLightPositionRange[i].xyz - vWorldPosition;
                float distanceSq = max(dot(toLight, toLight), 1e-4);
                vec3 l = toLight * inversesqrt(distanceSq);
                float range = uLightPositionRange[i].w;
                float factor = distanceSq / (range * range);
                float smoothFactor = clamp(1.0 - factor * factor, 0.0, 1.0);
                float attenuation = smoothFactor * smoothFactor / distanceSq;

                float cosOuter = uLightDirectionCos[i].w;
                if (cosOuter > -1.0)
                {
                    float invAngleRange = 1.0 / max(uLightCosInner[i] - cosOuter, 0.001);
                    float spot = clamp((dot(uLightDirectionCos[i].xyz, -l) - cosOuter) * invAngleRange, 0.0, 1.0);
                    attenuation *= spot * spot;
                }
                float nli = clamp(dot(n, l), 0.0, 1.0) * AdditionalLightShadow(i, vWorldPosition);
                color += DirectLight(brdf, n, v, l, uLightColor[i].rgb * attenuation * nli);
            }

            fragColor = vec4(color, uBaseColor.a);
        }
        """;

    public const string UnlitFragment = """
        #version 330 core
        uniform vec4 uBaseColor;
        out vec4 fragColor;

        void main()
        {
            fragColor = uBaseColor;
        }
        """;

    // A large quad on y = 0 that follows the camera; the lines are computed per pixel.
    public const string GridVertex = """
        #version 330 core
        layout(location = 0) in vec2 aCorner;

        uniform mat4 uViewProjection;
        uniform vec3 uCameraPosition;
        uniform float uExtent;

        out vec3 vWorldPosition;

        void main()
        {
            vec3 world = vec3(uCameraPosition.x + aCorner.x * uExtent, 0.0, uCameraPosition.z + aCorner.y * uExtent);
            vWorldPosition = world;
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    public const string GridFragment = """
        #version 330 core
        in vec3 vWorldPosition;

        uniform vec3 uCameraPosition;
        uniform float uFadeDistance;

        out vec4 fragColor;

        // 1 on a line of the given cell size, 0 elsewhere, anti-aliased over ~1 pixel.
        float gridLine(vec2 coord, float cellSize)
        {
            vec2 c = coord / cellSize;
            vec2 derivative = fwidth(c);
            vec2 distanceToLine = abs(fract(c - 0.5) - 0.5) / derivative;
            return 1.0 - min(min(distanceToLine.x, distanceToLine.y), 1.0);
        }

        void main()
        {
            vec2 coord = vWorldPosition.xz;
            float minor = gridLine(coord, 1.0);
            float major = gridLine(coord, 10.0);

            vec4 color = vec4(0.55, 0.55, 0.55, 0.25 * minor);
            color = mix(color, vec4(0.65, 0.65, 0.65, 0.5), major);

            // X axis (z = 0) in red, Z axis (x = 0) in blue.
            vec2 derivative = fwidth(coord);
            float xAxis = 1.0 - min(abs(coord.y) / derivative.y, 1.0);
            float zAxis = 1.0 - min(abs(coord.x) / derivative.x, 1.0);
            color = mix(color, vec4(0.85, 0.25, 0.25, 0.9), xAxis);
            color = mix(color, vec4(0.25, 0.45, 0.9, 0.9), zAxis);

            float distance = length(vWorldPosition - uCameraPosition);
            color.a *= 1.0 - smoothstep(uFadeDistance * 0.3, uFadeDistance, distance);

            if (color.a <= 0.001)
                discard;
            // The colors above are display (gamma) values; the scene buffer is linear.
            fragColor = vec4(pow(color.rgb, vec3(2.2)), color.a);
        }
        """;
}
