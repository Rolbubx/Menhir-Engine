namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>
/// Post-processing passes, after URP's (PostProcessPass, UberPost, LutBuilderHdr) and, for the
/// bokeh depth of field, Unity's Post Processing Stack v2. All draw a full-screen triangle.
/// </summary>
internal static class PostProcessShaders
{
    public const string FullscreenVertex = """
        #version 330 core
        out vec2 vUV;

        void main()
        {
            vec2 position = vec2(gl_VertexID == 1 ? 3.0 : -1.0, gl_VertexID == 2 ? 3.0 : -1.0);
            vUV = position * 0.5 + 0.5;
            gl_Position = vec4(position, 0.0, 1.0);
        }
        """;

    private const string LinearDepth = """
        uniform sampler2D uDepth;
        uniform float uNear;
        uniform float uFar;

        float LinearEyeDepth(vec2 uv)
        {
            float z = texture(uDepth, uv).r * 2.0 - 1.0;
            return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear));
        }
        """;

    // ---- Depth of field ---------------------------------------------------------------------

    /// <summary>Gaussian: 0 in front of Start, 1 past End (only the background blurs).</summary>
    public const string CocGaussian = """
        #version 330 core
        in vec2 vUV;
        uniform float uStart;
        uniform float uEnd;
        """ + LinearDepth + """
        out float fragCoc;

        void main()
        {
            float depth = LinearEyeDepth(vUV);
            fragCoc = clamp((depth - uStart) / max(uEnd - uStart, 1e-4), 0.0, 1.0);
        }
        """;

    /// <summary>Bokeh: thin-lens circle of confusion, stored as 0..1 with 0.5 in focus.</summary>
    public const string CocBokeh = """
        #version 330 core
        in vec2 vUV;
        uniform float uFocusDistance;
        uniform float uLensCoefficient;
        uniform float uMaxCoc;
        """ + LinearDepth + """
        out float fragCoc;

        void main()
        {
            float depth = LinearEyeDepth(vUV);
            float coc = (depth - uFocusDistance) * uLensCoefficient / max(depth, 1e-5);
            fragCoc = clamp(coc / uMaxCoc * 0.5 + 0.5, 0.0, 1.0);
        }
        """;

    /// <summary>Half resolution color, with the CoC in alpha (Gaussian: 0..1; Bokeh: signed radius).</summary>
    public const string DofPrefilter = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform sampler2D uCoc;
        uniform int uBokeh;
        uniform float uMaxCoc;
        uniform vec2 uTexelSize;   // of the half resolution target
        out vec4 fragColor;

        void main()
        {
            vec3 color = texture(uSource, vUV).rgb;
            float coc = texture(uCoc, vUV).r;
            if (uBokeh == 0)
            {
                fragColor = vec4(color * coc, coc);
                return;
            }
            float radius = (coc - 0.5) * 2.0 * uMaxCoc;
            // Focused pixels fade out of the blurred image, so they do not smear into it.
            color *= smoothstep(0.0, uTexelSize.y * 2.0, abs(radius));
            fragColor = vec4(color, radius);
        }
        """;

    /// <summary>9-tap Gaussian, each tap weighted by its own CoC (premultiplied in the prefilter).</summary>
    public const string DofGaussianBlur = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform vec2 uDirection;   // texel step, scaled by the max radius
        out vec4 fragColor;

        const float kWeights[5] = float[](0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162);

        void main()
        {
            vec4 sum = texture(uSource, vUV) * kWeights[0];
            for (int i = 1; i < 5; i++)
            {
                sum += texture(uSource, vUV + uDirection * float(i)) * kWeights[i];
                sum += texture(uSource, vUV - uDirection * float(i)) * kWeights[i];
            }
            fragColor = sum;
        }
        """;

    public const string DofGaussianComposite = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform sampler2D uBlurred;
        uniform sampler2D uCoc;
        out vec4 fragColor;

        void main()
        {
            vec3 color = texture(uSource, vUV).rgb;
            vec4 blurred = texture(uBlurred, vUV);
            vec3 blurredColor = blurred.rgb / max(blurred.a, 1e-4);
            float coc = texture(uCoc, vUV).r;
            fragColor = vec4(mix(color, blurredColor, coc), 1.0);
        }
        """;

    /// <summary>Gathers the disk (polygon) of kernel samples, separating background and foreground.</summary>
    public const string DofBokeh = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform vec2 uKernel[42];
        uniform float uMaxCoc;
        uniform float uRcpAspect;
        uniform vec2 uTexelSize;
        out vec4 fragColor;

        const float PI = 3.14159265;

        void main()
        {
            vec4 center = texture(uSource, vUV);
            float margin = uTexelSize.y * 2.0;
            // The center sample (distance 0) always counts for the background, and for the
            // foreground when it is blurry itself.
            vec4 background = vec4(center.rgb, 1.0);
            float centerForeground = clamp((-center.a + margin) / margin, 0.0, 1.0) * step(uTexelSize.y, -center.a);
            vec4 foreground = vec4(center.rgb, 1.0) * centerForeground;

            for (int i = 0; i < 42; i++)
            {
                vec2 offset = uKernel[i] * uMaxCoc;
                float distance = length(offset);
                vec4 s = texture(uSource, vUV + vec2(offset.x * uRcpAspect, offset.y));

                // Background: a sample counts when its blur (and the center's) reaches this far.
                float backgroundCoc = max(min(center.a, s.a), 0.0);
                float backgroundWeight = clamp((backgroundCoc - distance + margin) / margin, 0.0, 1.0);
                // Foreground: blurry near samples spread over everything behind them.
                float foregroundWeight = clamp((-s.a - distance + margin) / margin, 0.0, 1.0);
                foregroundWeight *= step(uTexelSize.y, -s.a);

                background += vec4(s.rgb, 1.0) * backgroundWeight;
                foreground += vec4(s.rgb, 1.0) * foregroundWeight;
            }

            background.rgb /= background.a + (background.a == 0.0 ? 1.0 : 0.0);
            foreground.rgb /= foreground.a + (foreground.a == 0.0 ? 1.0 : 0.0);
            float foregroundAlpha = clamp(foreground.a * PI / 43.0, 0.0, 1.0);
            fragColor = vec4(mix(background.rgb, foreground.rgb, foregroundAlpha), foregroundAlpha);
        }
        """;

    /// <summary>Small tent filter to smooth out the gather's sampling pattern.</summary>
    public const string DofPostFilter = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform vec2 uTexelSize;
        out vec4 fragColor;

        void main()
        {
            vec4 d = uTexelSize.xyxy * vec4(-0.5, -0.5, 0.5, 0.5);
            fragColor = (texture(uSource, vUV + d.xy) + texture(uSource, vUV + d.zy) +
                         texture(uSource, vUV + d.xw) + texture(uSource, vUV + d.zw)) * 0.25;
        }
        """;

    public const string DofBokehComposite = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform sampler2D uBlurred;
        uniform sampler2D uCoc;
        uniform float uMaxCoc;
        uniform vec2 uTexelSize;   // of the half resolution blur
        out vec4 fragColor;

        void main()
        {
            vec3 color = texture(uSource, vUV).rgb;
            vec4 dof = texture(uBlurred, vUV);
            float coc = (texture(uCoc, vUV).r - 0.5) * 2.0 * uMaxCoc;
            float backgroundAlpha = smoothstep(uTexelSize.y * 2.0, uTexelSize.y * 4.0, coc);
            float alpha = backgroundAlpha + dof.a - backgroundAlpha * dof.a;
            fragColor = vec4(mix(color, dof.rgb, alpha), 1.0);
        }
        """;

    // ---- Motion blur and Panini -------------------------------------------------------------

    /// <summary>Camera motion blur: each pixel is reprojected with last frame's camera.</summary>
    public const string MotionBlur = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform sampler2D uDepth;
        uniform mat4 uInverseViewProjection;
        uniform mat4 uPreviousViewProjection;
        uniform float uIntensity;
        uniform float uClamp;
        uniform int uSamples;
        out vec4 fragColor;

        void main()
        {
            float depth = texture(uDepth, vUV).r;
            vec4 world = uInverseViewProjection * vec4(vUV * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            world /= world.w;
            vec4 previous = uPreviousViewProjection * world;
            vec2 previousUV = previous.xy / previous.w * 0.5 + 0.5;

            vec2 velocity = (vUV - previousUV) * uIntensity;
            float speed = length(velocity);
            if (speed > uClamp)
                velocity *= uClamp / speed;

            vec3 sum = vec3(0.0);
            for (int i = 0; i < uSamples; i++)
            {
                float t = float(i) / float(uSamples - 1) - 0.5;
                sum += texture(uSource, vUV + velocity * t).rgb;
            }
            fragColor = vec4(sum / float(uSamples), 1.0);
        }
        """;

    public const string Panini = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform vec4 uParams;   // view extents xy, distance, crop scale
        out vec4 fragColor;

        vec2 PaniniGeneric(vec2 viewPos, float d)
        {
            float viewDist = 1.0 + d;
            float viewHypSq = viewPos.x * viewPos.x + viewDist * viewDist;
            float isectD = viewPos.x * d;
            float isectDiscrim = viewHypSq - isectD * isectD;
            float cylDistMinusD = (-isectD * viewPos.x + viewDist * sqrt(isectDiscrim)) / viewHypSq;
            float cylDist = cylDistMinusD + d;
            vec2 cylPos = viewPos * (cylDistMinusD / viewDist);
            return cylPos / (cylDist - d);
        }

        void main()
        {
            vec2 viewPos = (2.0 * vUV - 1.0) * uParams.xy * uParams.w;
            vec2 projPos = PaniniGeneric(viewPos, uParams.z);
            vec2 coords = projPos / uParams.xy * 0.5 + 0.5;
            fragColor = texture(uSource, coords);
        }
        """;

    // ---- Bloom ------------------------------------------------------------------------------

    /// <summary>Downsamples (4 bilinear taps) and keeps what is above the threshold, with a soft knee.</summary>
    public const string BloomPrefilter = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform vec2 uSourceTexelSize;
        uniform float uThreshold;   // linear
        uniform float uKnee;
        uniform float uClampMax;
        out vec4 fragColor;

        void main()
        {
            vec4 d = uSourceTexelSize.xyxy * vec4(-1.0, -1.0, 1.0, 1.0);
            vec3 color = (texture(uSource, vUV + d.xy).rgb + texture(uSource, vUV + d.zy).rgb +
                          texture(uSource, vUV + d.xw).rgb + texture(uSource, vUV + d.zw).rgb) * 0.25;
            color = min(vec3(uClampMax), color);

            float brightness = max(color.r, max(color.g, color.b));
            float softness = clamp(brightness - uThreshold + uKnee, 0.0, 2.0 * uKnee);
            softness = (softness * softness) / (4.0 * uKnee + 1e-4);
            float multiplier = max(brightness - uThreshold, softness) / max(brightness, 1e-4);
            fragColor = vec4(max(color * multiplier, 0.0), 1.0);
        }
        """;

    /// <summary>Halves the resolution with a horizontal 9-tap Gaussian (in source texels × 2).</summary>
    public const string BloomBlurH = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform vec2 uSourceTexelSize;
        out vec4 fragColor;

        void main()
        {
            float texel = uSourceTexelSize.x * 2.0;
            vec3 c0 = texture(uSource, vUV - vec2(texel * 4.0, 0.0)).rgb;
            vec3 c1 = texture(uSource, vUV - vec2(texel * 3.0, 0.0)).rgb;
            vec3 c2 = texture(uSource, vUV - vec2(texel * 2.0, 0.0)).rgb;
            vec3 c3 = texture(uSource, vUV - vec2(texel * 1.0, 0.0)).rgb;
            vec3 c4 = texture(uSource, vUV).rgb;
            vec3 c5 = texture(uSource, vUV + vec2(texel * 1.0, 0.0)).rgb;
            vec3 c6 = texture(uSource, vUV + vec2(texel * 2.0, 0.0)).rgb;
            vec3 c7 = texture(uSource, vUV + vec2(texel * 3.0, 0.0)).rgb;
            vec3 c8 = texture(uSource, vUV + vec2(texel * 4.0, 0.0)).rgb;
            vec3 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459 + c4 * 0.22702703
                       + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;
            fragColor = vec4(color, 1.0);
        }
        """;

    /// <summary>Vertical 5-tap Gaussian using bilinear filtering between taps.</summary>
    public const string BloomBlurV = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform vec2 uSourceTexelSize;
        out vec4 fragColor;

        void main()
        {
            float texel = uSourceTexelSize.y;
            vec3 c0 = texture(uSource, vUV - vec2(0.0, texel * 3.23076923)).rgb;
            vec3 c1 = texture(uSource, vUV - vec2(0.0, texel * 1.38461538)).rgb;
            vec3 c2 = texture(uSource, vUV).rgb;
            vec3 c3 = texture(uSource, vUV + vec2(0.0, texel * 1.38461538)).rgb;
            vec3 c4 = texture(uSource, vUV + vec2(0.0, texel * 3.23076923)).rgb;
            vec3 color = c0 * 0.07027027 + c1 * 0.31621622 + c2 * 0.22702703 + c3 * 0.31621622 + c4 * 0.07027027;
            fragColor = vec4(color, 1.0);
        }
        """;

    private const string Bicubic = """
        // Bicubic B-spline filtering from 4 bilinear taps (used by High Quality Filtering).
        vec4 SampleBicubic(sampler2D tex, vec2 uv, vec2 texelSize)
        {
            vec2 size = 1.0 / texelSize;
            vec2 xy = uv * size - 0.5;
            vec2 ic = floor(xy);
            vec2 fc = fract(xy);
            vec2 fc2 = fc * fc;
            vec2 fc3 = fc2 * fc;
            vec2 w0 = (1.0 / 6.0) * (-fc3 + 3.0 * fc2 - 3.0 * fc + 1.0);
            vec2 w1 = (1.0 / 6.0) * (3.0 * fc3 - 6.0 * fc2 + 4.0);
            vec2 w2 = (1.0 / 6.0) * (-3.0 * fc3 + 3.0 * fc2 + 3.0 * fc + 1.0);
            vec2 w3 = (1.0 / 6.0) * fc3;
            vec2 g0 = w0 + w1;
            vec2 g1 = w2 + w3;
            vec2 h0 = (w1 / g0 - 0.5 + ic) * texelSize;
            vec2 h1 = (w3 / g1 + 1.5 + ic) * texelSize;
            return g0.y * (g0.x * texture(tex, vec2(h0.x, h0.y)) + g1.x * texture(tex, vec2(h1.x, h0.y)))
                 + g1.y * (g0.x * texture(tex, vec2(h0.x, h1.y)) + g1.x * texture(tex, vec2(h1.x, h1.y)));
        }
        """;

    /// <summary>Adds the lower (wider) mip onto this one: Scatter decides how much of each.</summary>
    public const string BloomUpsample = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;      // this mip
        uniform sampler2D uLowMip;      // the next, smaller mip
        uniform vec2 uLowMipTexelSize;
        uniform float uScatter;
        uniform int uHighQuality;
        out vec4 fragColor;
        """ + Bicubic + """

        void main()
        {
            vec3 high = texture(uSource, vUV).rgb;
            vec3 low = uHighQuality != 0 ? SampleBicubic(uLowMip, vUV, uLowMipTexelSize).rgb : texture(uLowMip, vUV).rgb;
            fragColor = vec4(mix(high, low, uScatter), 1.0);
        }
        """;

    // ---- Uber -------------------------------------------------------------------------------

    /// <summary>
    /// Everything else in one pass, in URP's order: lens distortion, chromatic aberration, bloom,
    /// vignette, color grading, tonemapping, then (on the display range) curves, the lookup texture,
    /// film grain, gamma encoding and dithering.
    /// </summary>
    public const string Uber = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D uSource;
        uniform vec2 uScreenSize;

        uniform sampler2D uBloom;
        uniform int uBloomOn;
        uniform int uBloomHighQuality;
        uniform vec2 uBloomTexelSize;
        uniform float uBloomIntensity;
        uniform vec3 uBloomTint;

        uniform int uDistortionOn;
        uniform vec4 uDistortionCenter;   // center xy, axis multipliers zw
        uniform vec4 uDistortionParams;   // theta, sigma, 1/scale, intensity

        uniform float uChromaAmount;

        uniform int uVignetteOn;
        uniform vec3 uVignetteColor;
        uniform vec2 uVignetteCenter;
        uniform vec3 uVignetteParams;     // intensity, smoothness, roundness

        uniform float uPostExposure;
        uniform int uWhiteBalanceOn;
        uniform vec3 uWhiteBalance;
        uniform vec3 uHueSatCon;          // hue shift (turns), saturation, contrast
        uniform vec3 uColorFilter;
        uniform int uSplitToningOn;
        uniform vec4 uSplitShadows;       // gamma color, balance
        uniform vec3 uSplitHighlights;
        uniform int uChannelMixerOn;
        uniform vec3 uChannelMixerRed;
        uniform vec3 uChannelMixerGreen;
        uniform vec3 uChannelMixerBlue;
        uniform int uSmhOn;
        uniform vec3 uSmhShadows;
        uniform vec3 uSmhMidtones;
        uniform vec3 uSmhHighlights;
        uniform vec4 uSmhLimits;
        uniform int uLiftGammaGainOn;
        uniform vec3 uLift;
        uniform vec3 uGamma;
        uniform vec3 uGain;
        uniform int uCurvesOn;
        uniform sampler2D uCurves;        // 8 rows: master, red, green, blue, hue/hue, hue/sat, sat/sat, lum/sat
        uniform int uTonemapping;         // 0 none, 1 neutral, 2 ACES

        uniform int uLutOn;
        uniform sampler2D uLut;
        uniform vec4 uLutParams;          // 1 / width, 1 / height, size - 1, contribution

        uniform int uGrainOn;
        uniform sampler2D uGrain;
        uniform vec2 uGrainParams;        // intensity, response
        uniform vec4 uGrainScaleOffset;

        uniform float uDitherSeed;

        out vec4 fragColor;
        """ + Bicubic + """

        const int kCurveSamples = 128;

        float Luminance(vec3 c) { return dot(c, vec3(0.2126729, 0.7151522, 0.0721750)); }

        vec2 DistortUV(vec2 uv)
        {
            if (uDistortionOn == 0)
                return uv;
            uv = (uv - 0.5) * uDistortionParams.z + 0.5;
            vec2 ruv = uDistortionCenter.zw * (uv - 0.5 - uDistortionCenter.xy);
            float ru = length(ruv);
            if (uDistortionParams.w > 0.0)
            {
                float wu = ru * uDistortionParams.x;
                ru = tan(wu) * (1.0 / (ru * uDistortionParams.y));
                uv = uv + ruv * (ru - 1.0);
            }
            else
            {
                ru = (1.0 / ru) * uDistortionParams.x * atan(ru * uDistortionParams.y);
                uv = uv + ruv * (ru - 1.0);
            }
            return uv;
        }

        float Curve(int row, float x, bool loop)
        {
            float n = float(kCurveSamples);
            float u = loop ? x + 0.5 / n : clamp(x, 0.0, 1.0) * (n - 1.0) / n + 0.5 / n;
            return texture(uCurves, vec2(u, (float(row) + 0.5) / 8.0)).r;
        }

        // ARRI LogC, where URP applies contrast.
        vec3 LinearToLogC(vec3 x)
        {
            return mix(5.301883 * x + 0.092819, 0.244161 * log(5.555556 * x + 0.047996) / log(10.0) + 0.386036,
                       step(vec3(0.011361), x));
        }

        vec3 LogCToLinear(vec3 x)
        {
            return mix((x - 0.092819) / 5.301883, (pow(vec3(10.0), (x - 0.386036) / 0.244161) - 0.047996) / 5.555556,
                       step(vec3(5.301883 * 0.011361 + 0.092819), x));
        }

        vec3 RgbToHsv(vec3 c)
        {
            const vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
            vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
            vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
            float d = q.x - min(q.w, q.y);
            const float e = 1.0e-4;
            return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
        }

        vec3 HsvToRgb(vec3 c)
        {
            const vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
            vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
            return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
        }

        vec3 SoftLight(vec3 base, vec3 blend)
        {
            vec3 r1 = 2.0 * base * blend + base * base * (1.0 - 2.0 * blend);
            vec3 r2 = sqrt(base) * (2.0 * blend - 1.0) + 2.0 * base * (1.0 - blend);
            vec3 t = step(0.5, blend);
            return r2 * t + (1.0 - t) * r1;
        }

        vec3 NeutralCurve(vec3 x, float a, float b, float c, float d, float e, float f)
        {
            return ((x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f)) - e / f;
        }

        vec3 NeutralTonemap(vec3 x)
        {
            const float a = 0.2, b = 0.29, c = 0.24, d = 0.272, e = 0.02, f = 0.3;
            const float whiteLevel = 5.3;
            vec3 whiteScale = vec3(1.0) / NeutralCurve(vec3(whiteLevel), a, b, c, d, e, f);
            return NeutralCurve(x * whiteScale, a, b, c, d, e, f) * whiteScale;
        }

        // ACES RRT + ODT (sRGB, 100 nits), Stephen Hill's fit.
        vec3 AcesTonemap(vec3 color)
        {
            mat3 inputMat = transpose(mat3(0.59719, 0.35458, 0.04823,
                                                 0.07600, 0.90834, 0.01566,
                                                 0.02840, 0.13383, 0.83777));
            mat3 outputMat = transpose(mat3(1.60475, -0.53108, -0.07367,
                                                  -0.10208, 1.10813, -0.00605,
                                                  -0.00327, -0.07276, 1.07602));
            vec3 v = inputMat * color;
            vec3 a = v * (v + 0.0245786) - 0.000090537;
            vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
            return clamp(outputMat * (a / b), 0.0, 1.0);
        }

        vec3 ColorGrade(vec3 c)
        {
            if (uWhiteBalanceOn != 0)
            {
                mat3 linToLms = transpose(mat3(3.90405e-1, 5.49941e-1, 8.92632e-3,
                                                     7.08416e-2, 9.63172e-1, 1.35775e-3,
                                                     2.31082e-2, 1.28021e-1, 9.36245e-1));
                mat3 lmsToLin = transpose(mat3(2.85847e+0, -1.62879e+0, -2.48910e-2,
                                                     -2.10182e-1, 1.15820e+0, 3.24281e-4,
                                                     -4.18120e-2, -1.18169e-1, 1.06867e+0));
                c = lmsToLin * ((linToLms * c) * uWhiteBalance);
            }

            if (uHueSatCon.z != 1.0)
            {
                const float midGray = 0.4135884;
                vec3 logC = LinearToLogC(c);
                c = LogCToLinear((logC - midGray) * uHueSatCon.z + midGray);
            }

            c *= uColorFilter;
            c = max(c, 0.0);

            if (uSplitToningOn != 0)
            {
                // Like Adobe's, in gamma space.
                vec3 gamma = pow(c, vec3(1.0 / 2.2));
                float luma = clamp(Luminance(clamp(gamma, 0.0, 1.0)) + uSplitShadows.w, 0.0, 1.0);
                vec3 shadows = mix(vec3(0.5), uSplitShadows.rgb, 1.0 - luma);
                vec3 highlights = mix(vec3(0.5), uSplitHighlights, luma);
                gamma = SoftLight(gamma, shadows);
                gamma = SoftLight(gamma, highlights);
                c = pow(max(gamma, 0.0), vec3(2.2));
            }

            if (uChannelMixerOn != 0)
                c = vec3(dot(c, uChannelMixerRed), dot(c, uChannelMixerGreen), dot(c, uChannelMixerBlue));

            if (uSmhOn != 0)
            {
                float luma = Luminance(c);
                float shadowsFactor = 1.0 - smoothstep(uSmhLimits.x, uSmhLimits.y, luma);
                float highlightsFactor = smoothstep(uSmhLimits.z, uSmhLimits.w, luma);
                float midtonesFactor = 1.0 - shadowsFactor - highlightsFactor;
                c = c * uSmhShadows * shadowsFactor + c * uSmhMidtones * midtonesFactor + c * uSmhHighlights * highlightsFactor;
            }

            if (uLiftGammaGainOn != 0)
            {
                c = c * uGain + uLift;
                c = sign(c) * pow(abs(c), uGamma);
            }

            float satMult = 1.0;
            if (uCurvesOn != 0 || uHueSatCon.x != 0.0)
            {
                vec3 hsv = RgbToHsv(max(c, 0.0));
                float hue = hsv.x + uHueSatCon.x;
                if (uCurvesOn != 0)
                {
                    satMult = Curve(5, hsv.x, true) * 2.0;
                    satMult *= Curve(6, hsv.y, false) * 2.0;
                    satMult *= Curve(7, Luminance(c), false) * 2.0;
                    hue += Curve(4, hue, true) - 0.5;
                }
                hsv.x = hue < 0.0 ? hue + 1.0 : hue > 1.0 ? hue - 1.0 : hue;
                c = HsvToRgb(hsv);
            }

            float luma = Luminance(c);
            c = vec3(luma) + (uHueSatCon.y * satMult) * (c - vec3(luma));
            return max(c, 0.0);
        }

        vec3 ApplyLut(vec3 color)
        {
            // color is gamma-encoded, 0..1. The strip has size tiles of size × size, blue across tiles.
            vec3 scaled = clamp(color, 0.0, 1.0) * uLutParams.z;
            float blue = floor(scaled.b);
            float blueFrac = scaled.b - blue;
            vec2 uv = vec2((scaled.r + 0.5) * uLutParams.x + blue * uLutParams.y, (scaled.g + 0.5) * uLutParams.y);
            uv.y = 1.0 - uv.y; // images are stored top row first
            vec3 a = texture(uLut, uv).rgb;
            vec3 b = texture(uLut, uv + vec2(uLutParams.y, 0.0)).rgb;
            return mix(a, b, blueFrac);
        }

        float Hash(vec2 p)
        {
            return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
        }

        void main()
        {
            vec2 uvDistorted = DistortUV(vUV);
            vec3 color;
            if (uChromaAmount > 0.0)
            {
                vec2 coords = 2.0 * vUV - 1.0;
                vec2 end = vUV - coords * dot(coords, coords) * uChromaAmount;
                vec2 delta = (end - vUV) / 3.0;
                color = vec3(texture(uSource, uvDistorted).r,
                             texture(uSource, DistortUV(delta + vUV)).g,
                             texture(uSource, DistortUV(delta * 2.0 + vUV)).b);
            }
            else
            {
                color = texture(uSource, uvDistorted).rgb;
            }

            if (uBloomOn != 0)
            {
                vec3 bloom = uBloomHighQuality != 0 ? SampleBicubic(uBloom, uvDistorted, uBloomTexelSize).rgb
                                                    : texture(uBloom, uvDistorted).rgb;
                color += bloom * uBloomIntensity * uBloomTint;
            }

            if (uVignetteOn != 0)
            {
                vec2 dist = abs(uvDistorted - uVignetteCenter) * uVignetteParams.x;
                dist.x *= uVignetteParams.z;
                float factor = pow(clamp(1.0 - dot(dist, dist), 0.0, 1.0), uVignetteParams.y);
                color *= mix(uVignetteColor, vec3(1.0), factor);
            }

            color = ColorGrade(color * uPostExposure);

            if (uTonemapping == 1)
                color = NeutralTonemap(color);
            else if (uTonemapping == 2)
                color = AcesTonemap(color);
            color = clamp(color, 0.0, 1.0);

            vec3 gamma = pow(color, vec3(1.0 / 2.2));
            if (uCurvesOn != 0)
            {
                gamma = vec3(Curve(0, gamma.r, false), Curve(0, gamma.g, false), Curve(0, gamma.b, false));
                gamma = vec3(Curve(1, gamma.r, false), Curve(2, gamma.g, false), Curve(3, gamma.b, false));
            }
            if (uLutOn != 0)
                gamma = mix(gamma, ApplyLut(gamma), uLutParams.w);
            color = pow(clamp(gamma, 0.0, 1.0), vec3(2.2));

            if (uGrainOn != 0)
            {
                float grain = (texture(uGrain, vUV * uGrainScaleOffset.xy + uGrainScaleOffset.zw).r - 0.5) * 2.0;
                float response = mix(1.0, 1.0 - sqrt(Luminance(color)), uGrainParams.y);
                color += color * grain * uGrainParams.x * response;
            }

            // Gamma encoding for the 8-bit display buffer, dithered against banding.
            vec3 encoded = pow(clamp(color, 0.0, 1.0), vec3(1.0 / 2.2));
            encoded += (Hash(vUV * uScreenSize + uDitherSeed) - 0.5) / 255.0;
            fragColor = vec4(encoded, 1.0);
        }
        """;
}
