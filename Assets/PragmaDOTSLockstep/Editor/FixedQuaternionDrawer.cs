using Pragma.Lockstep.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Pragma.Lockstep.Editor
{
    /// <summary>Shows <see cref="FixedQuaternion"/> as Euler angles in degrees, like the Transform inspector.</summary>
    [CustomPropertyDrawer(typeof(FixedQuaternion))]
    internal sealed class FixedQuaternionDrawer : PropertyDrawer
    {
        private static readonly string[] Names = { "x", "y", "z", "w" };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var raws = new SerializedProperty[4];
            var values = new float[4];
            for (var i = 0; i < 4; i++)
            {
                raws[i] = property.FindPropertyRelative(Names[i]).FindPropertyRelative(nameof(FixedPoint.rawValue));
                values[i] = raws[i].longValue / (float)FixedPoint.ONE_RAW;
            }
            var rotation = new Quaternion(values[0], values[1], values[2], values[3]);
            if (rotation.x == 0 && rotation.y == 0 && rotation.z == 0 && rotation.w == 0)
            {
                rotation = Quaternion.identity;
            }

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var euler = EditorGUI.Vector3Field(position, label, rotation.eulerAngles);
            if (EditorGUI.EndChangeCheck())
            {
                var edited = FixedMath.NormalizeSafe((FixedQuaternion)Quaternion.Euler(euler));
                raws[0].longValue = edited.x.rawValue;
                raws[1].longValue = edited.y.rawValue;
                raws[2].longValue = edited.z.rawValue;
                raws[3].longValue = edited.w.rawValue;
            }
            EditorGUI.EndProperty();
        }
    }
}
