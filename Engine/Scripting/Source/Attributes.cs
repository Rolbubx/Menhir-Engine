using System;

namespace MenhirEngine
{
    /// <summary>Shows a float or int field as a slider in the inspector.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public readonly float min, max;

        public RangeAttribute(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }

    /// <summary>Help shown when hovering the field's name in the inspector.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public readonly string tooltip;

        public TooltipAttribute(string tooltip) { this.tooltip = tooltip; }
    }

    /// <summary>Keeps a public field out of the inspector (and out of the saved scene).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HideInInspectorAttribute : Attribute
    {
    }
}
