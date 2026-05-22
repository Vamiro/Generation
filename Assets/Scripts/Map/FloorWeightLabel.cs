using UnityEngine;

[RequireComponent(typeof(TextMesh))]
public class FloorWeightLabel : MonoBehaviour
{
    TextMesh textMesh;

    void Awake()
    {
        textMesh = GetComponent<TextMesh>();
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float yaw = cam.transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(90f, yaw, 0f);
    }

    public void Configure(string text, Font font, float characterSize, Color color)
    {
        if (textMesh == null)
            textMesh = GetComponent<TextMesh>();

        textMesh.text = text;
        textMesh.font = font;
        textMesh.characterSize = characterSize;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = color;
    }
}
