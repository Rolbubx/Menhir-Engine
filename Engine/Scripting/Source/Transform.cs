namespace MenhirEngine
{
    public enum Space
    {
        World,
        Self,
    }

    /// <summary>
    /// An entity's position, rotation and scale, relative to its parent (local*) or to the world.
    /// Changes show in the next frame.
    /// </summary>
    public sealed class Transform
    {
        private readonly int _entity;

        internal Transform(int entity) { _entity = entity; }

        public Entity entity => new Entity(_entity);

        public override bool Equals(object obj) => obj is Transform other && other._entity == _entity;

        public override int GetHashCode() => _entity;
        public string name => Host.GetName(_entity);

        public Vector3 localPosition
        {
            get => new Vector3(Host.GetFloat(_entity, Host.PositionX), Host.GetFloat(_entity, Host.PositionX + 1),
                Host.GetFloat(_entity, Host.PositionX + 2));
            set
            {
                Host.SetFloat(_entity, Host.PositionX, value.x);
                Host.SetFloat(_entity, Host.PositionX + 1, value.y);
                Host.SetFloat(_entity, Host.PositionX + 2, value.z);
            }
        }

        public Quaternion localRotation
        {
            get => new Quaternion(Host.GetFloat(_entity, Host.RotationX), Host.GetFloat(_entity, Host.RotationX + 1),
                Host.GetFloat(_entity, Host.RotationX + 2), Host.GetFloat(_entity, Host.RotationX + 3));
            set
            {
                Host.SetFloat(_entity, Host.RotationX, value.x);
                Host.SetFloat(_entity, Host.RotationX + 1, value.y);
                Host.SetFloat(_entity, Host.RotationX + 2, value.z);
                Host.SetFloat(_entity, Host.RotationX + 3, value.w);
            }
        }

        public Vector3 localScale
        {
            get => new Vector3(Host.GetFloat(_entity, Host.ScaleX), Host.GetFloat(_entity, Host.ScaleX + 1),
                Host.GetFloat(_entity, Host.ScaleX + 2));
            set
            {
                Host.SetFloat(_entity, Host.ScaleX, value.x);
                Host.SetFloat(_entity, Host.ScaleX + 1, value.y);
                Host.SetFloat(_entity, Host.ScaleX + 2, value.z);
            }
        }

        public Vector3 localEulerAngles
        {
            get => localRotation.eulerAngles;
            set => localRotation = Quaternion.Euler(value);
        }

        /// <summary>The parent's transform, or null for a root entity.</summary>
        public Transform parent
        {
            get
            {
                int p = Host.GetParent(_entity);
                return p >= 0 ? new Transform(p) : null;
            }
        }

        private Matrix LocalMatrix => Matrix.TRS(localPosition, localRotation, localScale);

        /// <summary>Local matrix times every ancestor's (like the renderer).</summary>
        internal Matrix WorldMatrix
        {
            get
            {
                Matrix world = LocalMatrix;
                for (int p = Host.GetParent(_entity); p >= 0; p = Host.GetParent(p))
                    world = world * new Transform(p).LocalMatrix;
                return world;
            }
        }

        private Quaternion ParentRotation
        {
            get
            {
                Quaternion rotation = Quaternion.identity;
                for (int p = Host.GetParent(_entity); p >= 0; p = Host.GetParent(p))
                    rotation = new Transform(p).localRotation * rotation;
                return rotation;
            }
        }

        public Vector3 position
        {
            get
            {
                Matrix world = WorldMatrix;
                return new Vector3(world.m41, world.m42, world.m43);
            }
            set
            {
                int p = Host.GetParent(_entity);
                localPosition = p < 0 ? value : new Transform(p).InverseTransformPoint(value);
            }
        }

        public Quaternion rotation
        {
            get => ParentRotation * localRotation;
            set => localRotation = Quaternion.Inverse(ParentRotation) * value;
        }

        public Vector3 eulerAngles
        {
            get => rotation.eulerAngles;
            set => rotation = Quaternion.Euler(value);
        }

        /// <summary>The entity's forward axis (-Z) in the world.</summary>
        public Vector3 forward => (rotation * Vector3.forward).normalized;
        public Vector3 right => (rotation * Vector3.right).normalized;
        public Vector3 up => (rotation * Vector3.up).normalized;

        public Vector3 TransformPoint(Vector3 point) => WorldMatrix.MultiplyPoint(point);

        public Vector3 TransformDirection(Vector3 direction) => rotation * direction;

        public Vector3 InverseTransformPoint(Vector3 point)
        {
            Vector3 relative = point - position;
            Vector3 local = Quaternion.Inverse(rotation) * relative;
            Vector3 scale = LossyScale;
            return new Vector3(local.x / scale.x, local.y / scale.y, local.z / scale.z);
        }

        public Vector3 InverseTransformDirection(Vector3 direction) => Quaternion.Inverse(rotation) * direction;

        private Vector3 LossyScale
        {
            get
            {
                Vector3 scale = localScale;
                for (int p = Host.GetParent(_entity); p >= 0; p = Host.GetParent(p))
                    scale = Vector3.Scale(scale, new Transform(p).localScale);
                return scale;
            }
        }

        public void Translate(Vector3 translation, Space relativeTo = Space.Self)
        {
            position = position + (relativeTo == Space.Self ? rotation * translation : translation);
        }

        public void Translate(float x, float y, float z) => Translate(new Vector3(x, y, z));

        /// <summary>Rotates by Euler angles (degrees), around the entity's own axes or the world's.</summary>
        public void Rotate(Vector3 eulers, Space relativeTo = Space.Self)
        {
            Quaternion delta = Quaternion.Euler(eulers);
            if (relativeTo == Space.Self)
                localRotation = localRotation * delta;
            else
                rotation = delta * rotation;
        }

        public void Rotate(float x, float y, float z) => Rotate(new Vector3(x, y, z));

        public void RotateAround(Vector3 point, Vector3 axis, float angle)
        {
            Quaternion delta = Quaternion.AngleAxis(angle, axis);
            position = point + delta * (position - point);
            rotation = delta * rotation;
        }

        /// <summary>Turns the forward axis (-Z) towards a point.</summary>
        public void LookAt(Vector3 worldPosition) => LookAt(worldPosition, Vector3.up);

        public void LookAt(Vector3 worldPosition, Vector3 worldUp)
        {
            Vector3 direction = worldPosition - position;
            if (direction.sqrMagnitude > 1E-12f)
                rotation = Quaternion.LookRotation(direction, worldUp);
        }

        public void LookAt(Transform target) => LookAt(target.position);
    }
}
