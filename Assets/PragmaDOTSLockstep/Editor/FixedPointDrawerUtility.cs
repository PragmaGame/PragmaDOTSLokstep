using Pragma.Lockstep.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Pragma.Lockstep.Editor
{
    internal static class FixedPointDrawerUtility
    {
        public static void DrawComponents(Rect position, SerializedProperty property, GUIContent label, params string[] names)
        {
            EditorGUI.BeginProperty(position, label, property);
            var fields = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            var indent = EditorGUI.indentLevel;
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = 13f;

            var width = (fields.width - (names.Length - 1) * 4f) / names.Length;
            for (var i = 0; i < names.Length; i++)
            {
                var rect = new Rect(fields.x + i * (width + 4f), fields.y, width, fields.height);
                var raw = property.FindPropertyRelative(names[i]).FindPropertyRelative(nameof(FixedPoint.rawValue));
                EditorGUI.BeginChangeCheck();
                var value = EditorGUI.DoubleField(rect, names[i].ToUpperInvariant(), raw.longValue / (double)FixedPoint.ONE_RAW);
                if (EditorGUI.EndChangeCheck())
                {
                    raw.longValue = ((FixedPoint)value).rawValue;
                }
            }

            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
