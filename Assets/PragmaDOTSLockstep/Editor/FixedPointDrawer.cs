using Pragma.Lockstep.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Pragma.Lockstep.Editor
{
    /// <summary>Shows <see cref="FixedPoint"/> fields as decimal numbers; edits are rounded to the nearest fixed-point step.</summary>
    [CustomPropertyDrawer(typeof(FixedPoint))]
    internal sealed class FixedPointDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var raw = property.FindPropertyRelative(nameof(FixedPoint.rawValue));
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DoubleField(position, label, raw.longValue / (double)FixedPoint.ONE_RAW);
            if (EditorGUI.EndChangeCheck())
            {
                raw.longValue = ((FixedPoint)value).rawValue;
            }
            EditorGUI.EndProperty();
        }
    }
}
