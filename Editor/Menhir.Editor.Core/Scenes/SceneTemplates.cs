using System.Numerics;
using Menhir.Editor.Mathematics;
using Menhir.Editor.PostProcessing;

namespace Menhir.Editor.Scenes;

/// <summary>Starting content of new scenes.</summary>
public static class SceneTemplates
{
    /// <summary>
    /// Like a new URP scene: a sun with soft shadows, the procedural sky and a global volume with tonemapping,
    /// a little bloom and a light vignette.
    /// </summary>
    public static Scene CreateDefault()
    {
        var scene = new Scene();

        Entity light = scene.CreateEntity("Directional Light");
        light.Transform.Position = new Vector3(0, 3, 0);
        light.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(-50, -30, 0));
        light.Components.Add(new Light { Shadows = LightShadows.Soft });

        Entity environment = scene.CreateEntity("Environment");
        environment.Components.Add(new ProceduralSkybox());

        Entity volume = scene.CreateEntity("Global Volume");
        volume.Components.Add(new Tonemapping { Mode = TonemappingMode.ACES });
        volume.Components.Add(new Bloom { Threshold = 1f, Intensity = 0.5f });
        volume.Components.Add(new Vignette { Intensity = 0.2f });

        return scene;
    }
}
