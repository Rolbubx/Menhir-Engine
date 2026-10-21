using MenhirEngine;

/// <summary>Turns the entity around an axis; Space reverses it.</summary>
public class Spinner : Script
{
    [Range(0f, 360f)]
    [Tooltip("Degrees per second")]
    public float speed = 45f;
    public Vector3 axis = Vector3.up;

    public override void Update()
    {
        if (Input.GetKeyDown("space"))
            speed = -speed;
        transform.Rotate(axis * (speed * Time.deltaTime), Space.World);
    }
}
