using UnityEngine;

/// <summary>
/// Draws the active Y-layer's grid lines with GL.Lines against the built-in
/// Hidden/Internal-Colored shader -- more performant than per-frame GL.DrawLine calls
/// and needs zero new shader/material assets (Systems Architecture, Section 11).
///
/// Setup: attach to any always-active GameObject in the editor scene (e.g. the same
/// GameObject as LevelEditorController) and assign controller.
/// </summary>
public class EditorGridRenderer : MonoBehaviour
{
    [SerializeField] private LevelEditorController controller;

    private static Material _lineMaterial;

    private static void EnsureLineMaterial()
    {
        if (_lineMaterial != null) return;

        var shader = Shader.Find("Hidden/Internal-Colored");
        _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _lineMaterial.SetInt("_ZWrite", 0);
    }

    private void OnRenderObject()
    {
        if (controller == null || controller.Blueprint == null) return;

        EnsureLineMaterial();
        _lineMaterial.SetPass(0);

        float cell = LevelEditorController.CellSize;
        float y = controller.CurrentLayer * cell;
        Vector3Int size = controller.Blueprint.GridSize;

        GL.PushMatrix();
        GL.Begin(GL.LINES);
        GL.Color(new Color(1f, 1f, 1f, 0.5f));

        for (int x = 0; x <= size.x; x++)
        {
            GL.Vertex(new Vector3(x * cell, y, 0f));
            GL.Vertex(new Vector3(x * cell, y, size.z * cell));
        }

        for (int z = 0; z <= size.z; z++)
        {
            GL.Vertex(new Vector3(0f, y, z * cell));
            GL.Vertex(new Vector3(size.x * cell, y, z * cell));
        }

        GL.End();
        GL.PopMatrix();
    }
}
