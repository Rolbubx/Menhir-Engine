namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>
/// The procedural skybox: the same scattering as <see cref="Lighting.SkyModel"/> (which documents
/// it), evaluated per pixel, plus the sun disk. Drawn as a full-screen triangle at the far plane.
/// </summary>
public static class SkyShaders
{
    public const string Vertex = """
        #version 330 core
        uniform mat4 uInverseViewProjection;
        out vec3 vRay;

        void main()
        {
            // Full-screen triangle from the vertex index: (-1,-1), (3,-1), (-1,3).
            vec2 ndc = vec2(gl_VertexID == 1 ? 3.0 : -1.0, gl_VertexID == 2 ? 3.0 : -1.0);
            vec4 nearPoint = uInverseViewProjection * vec4(ndc, -1.0, 1.0);
            vec4 farPoint = uInverseViewProjection * vec4(ndc, 1.0, 1.0);
            vRay = farPoint.xyz / farPoint.w - nearPoint.xyz / nearPoint.w;
            gl_Position = vec4(ndc, 1.0, 1.0);
        }
        """;

    public const string Fragment = """
        #version 330 core
        in vec3 vRay;

        uniform vec3 uToSun;
        uniform vec3 uSunColor;     // light color times intensity; zero: no sun disk
        uniform int uSunDisk;       // 0 none, 1 simple, 2 high quality
        uniform float uSunSize;
        uniform float uSunSizeConvergence;
        uniform float uAtmosphereThickness;
        uniform vec3 uSkyTintGamma;
        uniform vec3 uGroundColor;
        uniform float uExposure;

        out vec4 fragColor;

        const vec3 kDefaultWavelength = vec3(0.65, 0.57, 0.475);
        const vec3 kWavelengthRange = vec3(0.15);
        const float kOuterRadius = 1.025;
        const float kInnerRadius = 1.0;
        const float kCameraHeight = 0.0001;
        const float kMie = 0.0010;
        const float kSunBrightness = 20.0;
        const float kMaxScatter = 50.0;
        const float kScale = 1.0 / (kOuterRadius - 1.0);
        const float kScaleDepth = 0.25;
        const float kScaleOverScaleDepth = kScale / kScaleDepth;
        const int kSamples = 2;
        const float kSkyGroundThreshold = 0.02;
        const float kMieG = -0.990;
        const float kMieG2 = 0.9801;
        const float PI = 3.14159265;

        float ScaleFunction(float cosAngle)
        {
            float x = 1.0 - cosAngle;
            return 0.25 * exp(-0.00287 + x * (0.459 + x * (3.83 + x * (-6.80 + x * 5.25))));
        }

        float MiePhase(float eyeCos, float eyeCos2)
        {
            float temp = 1.0 + kMieG2 - 2.0 * kMieG * eyeCos;
            temp = pow(temp, pow(uSunSize, 0.65) * 10.0);
            temp = max(temp, 1.0e-4);
            return 1.5 * ((1.0 - kMieG2) / (2.0 + kMieG2)) * (1.0 + eyeCos2) / temp;
        }

        float SunAttenuation(vec3 ray)
        {
            if (uSunDisk == 1)
            {
                float dist = length(uToSun - ray);
                float spot = 1.0 - smoothstep(0.0, uSunSize, dist);
                return spot * spot;
            }
            float focusedEyeCos = pow(clamp(dot(uToSun, ray), 0.0, 1.0), uSunSizeConvergence);
            return MiePhase(-focusedEyeCos, focusedEyeCos * focusedEyeCos);
        }

        void main()
        {
            vec3 eyeRay = normalize(vRay);

            vec3 wavelength = mix(kDefaultWavelength - kWavelengthRange, kDefaultWavelength + kWavelengthRange,
                vec3(1.0) - uSkyTintGamma);
            vec3 invWavelength = 1.0 / pow(wavelength, vec3(4.0));
            float rayleigh = 0.0025 * pow(uAtmosphereThickness, 2.5);
            float krESun = rayleigh * kSunBrightness;
            float kr4Pi = rayleigh * 4.0 * PI;
            float kmESun = kMie * kSunBrightness;
            float km4Pi = kMie * 4.0 * PI;
            vec3 extinction = invWavelength * kr4Pi + km4Pi;

            vec3 cameraPos = vec3(0.0, kInnerRadius + kCameraHeight, 0.0);
            vec3 cIn;
            vec3 cOut;

            if (eyeRay.y >= 0.0)
            {
                float far = sqrt(kOuterRadius * kOuterRadius + eyeRay.y * eyeRay.y - 1.0) - eyeRay.y;
                float height = kInnerRadius + kCameraHeight;
                float depth = exp(kScaleOverScaleDepth * -kCameraHeight);
                float startOffset = depth * ScaleFunction(dot(eyeRay, cameraPos) / height);

                float sampleLength = far / float(kSamples);
                float scaledLength = sampleLength * kScale;
                vec3 sampleRay = eyeRay * sampleLength;
                vec3 samplePoint = cameraPos + sampleRay * 0.5;

                vec3 frontColor = vec3(0.0);
                for (int i = 0; i < kSamples; i++)
                {
                    float sampleHeight = length(samplePoint);
                    float sampleDepth = exp(kScaleOverScaleDepth * (kInnerRadius - sampleHeight));
                    float lightAngle = dot(uToSun, samplePoint) / sampleHeight;
                    float cameraAngle = dot(eyeRay, samplePoint) / sampleHeight;
                    float scatter = startOffset + sampleDepth * (ScaleFunction(lightAngle) - ScaleFunction(cameraAngle));
                    vec3 attenuate = exp(-clamp(scatter, 0.0, kMaxScatter) * extinction);
                    frontColor += attenuate * (sampleDepth * scaledLength);
                    samplePoint += sampleRay;
                }
                cIn = frontColor * (invWavelength * krESun);
                cOut = frontColor * kmESun;
            }
            else
            {
                float far = -kCameraHeight / min(-0.001, eyeRay.y);
                vec3 pos = cameraPos + far * eyeRay;
                float depth = exp(-kCameraHeight / kScaleDepth);
                float cameraScale = ScaleFunction(dot(-eyeRay, pos));
                float lightScale = ScaleFunction(dot(uToSun, pos));
                float cameraOffset = depth * cameraScale;
                float temp = lightScale + cameraScale;

                float scaledLength = far * kScale;
                vec3 samplePoint = cameraPos + eyeRay * far * 0.5;
                float sampleDepth = exp(kScaleOverScaleDepth * (kInnerRadius - length(samplePoint)));
                float scatter = sampleDepth * temp - cameraOffset;
                vec3 attenuate = exp(-clamp(scatter, 0.0, kMaxScatter) * extinction);
                vec3 frontColor = attenuate * (sampleDepth * scaledLength);
                cIn = frontColor * (invWavelength * krESun + kmESun);
                cOut = clamp(attenuate, 0.0, 1.0);
            }

            float eyeCos = dot(uToSun, eyeRay);
            vec3 groundColor = uExposure * (cIn + uGroundColor * cOut);
            vec3 skyColor = uExposure * (cIn * (0.75 + 0.75 * eyeCos * eyeCos));

            float y = -eyeRay.y / kSkyGroundThreshold;
            vec3 color = mix(skyColor, groundColor, clamp(y, 0.0, 1.0));

            if (y < 0.0 && uSunDisk != 0)
            {
                float factor = uSunDisk == 1 ? 27.0 : 15.0;
                float lightIntensity = clamp(length(uSunColor), 0.25, 1.0);
                vec3 sunColor = factor * clamp(cOut, 0.0, 1.0) * uSunColor / lightIntensity;
                color += sunColor * SunAttenuation(eyeRay);
            }

            fragColor = vec4(color, 1.0);
        }
        """;
}
