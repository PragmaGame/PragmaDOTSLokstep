using Pragma.Lockstep.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Pragma.Lockstep.Editor
{
    [CustomPropertyDrawer(typeof(FixedVector3))]
    internal sealed class FixedVector3Drawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            FixedPointDrawerUtility.DrawComponents(position, property, label, "x", "y", "z");
        }
    }
}
