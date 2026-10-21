using Menhir.Editor.Inspection;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Inspection;

public class InspectablePropertiesTests
{
    private sealed class SampleComponent : Component
    {
        public float Speed { get; set; }
        public string Label { get; set; } = "";
        [HideInInspector] public int Hidden { get; set; }
        public int ReadOnly => 3;
        public int PrivateSetter { get; private set; }
        public bool Enabled { get; set; }
    }

    [Fact]
    public void Get_ReturnsPublicReadWritePropertiesInDeclarationOrder()
    {
        string[] names = InspectableProperties.Get(typeof(SampleComponent)).Select(p => p.Name).ToArray();

        Assert.Equal(["Speed", "Label", "Enabled"], names);
    }

    [Fact]
    public void Get_FindsMeshRendererProperties()
    {
        string[] names = InspectableProperties.Get(typeof(MeshRenderer)).Select(p => p.Name).ToArray();

        Assert.Equal(["Mesh", "Model", "Color", "Metallic", "Smoothness", "CastShadows", "ReceiveShadows"], names);
    }

    [Theory]
    [InlineData("MeshRenderer", "Mesh Renderer")]
    [InlineData("castShadows", "Cast Shadows")]
    [InlineData("UIScale", "UI Scale")]
    [InlineData("Layer2", "Layer 2")]
    [InlineData("X", "X")]
    public void NicifyName_SplitsWords(string name, string expected)
    {
        Assert.Equal(expected, InspectableProperties.NicifyName(name));
    }
}
