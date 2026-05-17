using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FloatRange))]
public class FloatRangeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Рисуем label с indent.
        position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

        int prevIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        // Разбиваем строку: [minField]  "–"  [maxField]
        float sepWidth = 16f;
        float fieldWidth = (position.width - sepWidth) / 2f;

        Rect minRect = new Rect(position.x, position.y, fieldWidth, position.height);
        Rect sepRect = new Rect(position.x + fieldWidth + 2f, position.y, sepWidth - 4f, position.height);
        Rect maxRect = new Rect(position.x + fieldWidth + sepWidth, position.y, fieldWidth, position.height);

        SerializedProperty minProp = property.FindPropertyRelative("min");
        SerializedProperty maxProp = property.FindPropertyRelative("max");

        EditorGUI.PropertyField(minRect, minProp, GUIContent.none);
        EditorGUI.LabelField(sepRect, "–");
        EditorGUI.PropertyField(maxRect, maxProp, GUIContent.none);

        // Гарантируем min ≤ max.
        if (minProp.floatValue > maxProp.floatValue)
            maxProp.floatValue = minProp.floatValue;

        EditorGUI.indentLevel = prevIndent;
        EditorGUI.EndProperty();
    }
}
