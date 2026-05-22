using UnityEngine;

[RequireComponent(typeof(TextMesh))]
public class FloorWeightLabel : MonoBehaviour
{
    static readonly (float x, float y)[] OutlineDirections =
    {
        (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f),
        (0.707f, 0.707f), (-0.707f, 0.707f), (0.707f, -0.707f), (-0.707f, -0.707f)
    };

    TextMesh textMesh;
    TextMesh[] outlineMeshes;

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

    public void Configure(string text, Font font, float characterSize, Color color, float outlineWidth)
    {
        if (textMesh == null)
            textMesh = GetComponent<TextMesh>();

        EnsureOutlineMeshes();
        ApplyTextMeshSettings(textMesh, text, font, characterSize, color);

        float offset = outlineWidth * characterSize;
        for (int i = 0; i < outlineMeshes.Length; i++)
        {
            TextMesh outline = outlineMeshes[i];
            (float dx, float dy) = OutlineDirections[i];
            outline.transform.localPosition = new Vector3(dx * offset, dy * offset, 0.001f);
            ApplyTextMeshSettings(outline, text, font, characterSize, Color.black);
        }
    }

    void EnsureOutlineMeshes()
    {
        if (outlineMeshes != null && outlineMeshes.Length == OutlineDirections.Length)
            return;

        if (outlineMeshes != null)
        {
            for (int i = 0; i < outlineMeshes.Length; i++)
            {
                if (outlineMeshes[i] != null)
                    Destroy(outlineMeshes[i].gameObject);
            }
        }

        outlineMeshes = new TextMesh[OutlineDirections.Length];
        for (int i = 0; i < OutlineDirections.Length; i++)
        {
            GameObject outlineGo = new GameObject($"Outline_{i}");
            outlineGo.transform.SetParent(transform, false);
            outlineMeshes[i] = outlineGo.AddComponent<TextMesh>();
        }
    }

    static void ApplyTextMeshSettings(TextMesh mesh, string text, Font font, float characterSize, Color color)
    {
        mesh.text = text;
        mesh.font = font;
        mesh.characterSize = characterSize;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = color;
    }
}
