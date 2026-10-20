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

        out vec3 vNormal;

        void main()
        {
            vNormal = mat3(uNormalMatrix) * aNormal;
            gl_Position = uViewProjection * uModel * vec4(aPosition, 1.0);
        }
        """;

    // Simple directional light + sky ambient: enough to read shapes in the editor.
    public const string MeshFragment = """
        #version 330 core
        in vec3 vNormal;

        uniform vec4 uColor;
        uniform vec3 uLightDirection;
        uniform int uUnlit;

        out vec4 fragColor;

        void main()
        {
            if (uUnlit != 0)
            {
                fragColor = uColor;
                return;
            }
            float diffuse = max(dot(normalize(vNormal), -uLightDirection), 0.0);
            float sky = 0.5 + 0.5 * normalize(vNormal).y;
            vec3 color = uColor.rgb * (0.12 + 0.13 * sky + 0.75 * diffuse);
            // Lighting happens in linear space; the render target stores sRGB.
            fragColor = vec4(pow(color, vec3(1.0 / 2.2)), uColor.a);
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
            fragColor = color;
        }
        """;
}
