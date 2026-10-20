namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>
/// The shadow caster pass: depth only, with URP's depth and normal biases applied to casters
/// (see <see cref="Lighting.ShadowSetup"/>).
/// </summary>
public static class ShadowShaders
{
    public const string CasterVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;

        uniform mat4 uModel;
        uniform mat4 uNormalMatrix;
        uniform mat4 uLightViewProjection;
        uniform vec3 uLightDirection;   // towards the light (directional)
        uniform vec3 uLightPosition;    // point and spot lights
        uniform int uPunctual;
        uniform float uDepthBias;       // negative: away from the light
        uniform float uNormalBias;      // negative: inwards

        void main()
        {
            vec3 position = (uModel * vec4(aPosition, 1.0)).xyz;
            vec3 normal = normalize(mat3(uNormalMatrix) * aNormal);
            vec3 toLight = uLightDirection;
            float scale = 1.0;
            if (uPunctual != 0)
            {
                toLight = uLightPosition - position;
                // Biases are in texels one unit away from the light; texels grow with distance.
                scale = length(toLight);
                toLight /= max(scale, 1e-4);
            }
            float invNdotL = 1.0 - clamp(dot(toLight, normal), 0.0, 1.0);
            position += toLight * (uDepthBias * scale);
            position += normal * (invNdotL * uNormalBias * scale);
            gl_Position = uLightViewProjection * vec4(position, 1.0);
        }
        """;

    public const string CasterFragment = """
        #version 330 core
        void main()
        {
        }
        """;
}
