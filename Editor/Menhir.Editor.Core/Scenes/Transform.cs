using System.Numerics;

namespace Menhir.Editor.Scenes;

/// <summary>
/// Local transform relative to the parent entity.
/// Conventions: right-handed, Y up, meters; matrices use System.Numerics row-vector order.
/// </summary>
public sealed class Transform
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;

    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);

    /// <summary>
    /// Sets position, rotation and scale from a TRS matrix. A matrix with shear (non-uniform
    /// scale under a rotated parent) cannot be represented exactly and is approximated.
    /// </summary>
    public void SetFromMatrix(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out Vector3 scale, out Quaternion rotation, out Vector3 position))
        {
            // Decompose fails on degenerate matrices (e.g. a zero scale); keep at least the position.
            Position = matrix.Translation;
            return;
        }
        Position = position;
        Rotation = Quaternion.Normalize(rotation);
        Scale = scale;
    }
}
