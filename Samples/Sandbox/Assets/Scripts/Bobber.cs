using MenhirEngine;

/// <summary>Floats the entity up and down around where it starts.</summary>
public class Bobber : Script
{
    public float height = 0.25f;
    public float period = 2f;

    private Vector3 _start;

    public override void Start()
    {
        _start = transform.position;
        Debug.Log(name + " bobs around " + _start);
    }

    public override void Update()
    {
        float phase = Time.time / period * 2f * Mathf.PI;
        transform.position = _start + Vector3.up * (Mathf.Sin(phase) * height);
    }
}
