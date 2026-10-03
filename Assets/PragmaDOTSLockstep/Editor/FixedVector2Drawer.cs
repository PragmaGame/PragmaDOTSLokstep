using Pragma.Lockstep.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Pragma.Lockstep.Editor
{
    [CustomPropertyDrawer(typeof(FixedVector2))]
    internal sealed class FixedVector2Drawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            FixedPointDrawerUtility.DrawComponents(position, property, label, "x", "y");
        }
    }
}
