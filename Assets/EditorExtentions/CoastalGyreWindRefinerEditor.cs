using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CoastalGyreWindRefinerComponent))]
public class CoastalGyreWindRefinerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (CoastalGyreWindRefinerComponent)target;

        GUILayout.Label("Step X: Coastal Gyre Wind Refiner (Hybrid)", EditorStyles.boldLabel);

        // Sources
        comp.windSource = (WindMapGeneratorComponent)EditorGUILayout.ObjectField(
            "Wind Source (global wind)",
            comp.windSource,
            typeof(WindMapGeneratorComponent),
            true
        );

        comp.voronoiSource = (VoronoiComponent)EditorGUILayout.ObjectField(
            "Voronoi Source",
            comp.voronoiSource,
            typeof(VoronoiComponent),
            true
        );

        comp.plateSource = (PlateAssignerComponent)EditorGUILayout.ObjectField(
            "Plate Source",
            comp.plateSource,
            typeof(PlateAssignerComponent),
            true
        );

        EditorGUILayout.Space(6);
        GUILayout.Label("Terrain Water Mask", EditorStyles.miniBoldLabel);

        comp.terrain = (Terrain)EditorGUILayout.ObjectField(
            "Terrain",
            comp.terrain,
            typeof(Terrain),
            true
        );

        comp.seaLevel = EditorGUILayout.Slider(
            "Sea Level",
            comp.seaLevel,
            0f, 1f
        );

        EditorGUILayout.Space(6);
        GUILayout.Label("Basin Merge Rules", EditorStyles.miniBoldLabel);

        comp.minSharedBorderForMerge = EditorGUILayout.IntField(
            "Min Shared Border (px)",
            comp.minSharedBorderForMerge
        );

        EditorGUILayout.Space(6);
        GUILayout.Label("Gyre Behaviour", EditorStyles.miniBoldLabel);

        comp.coastalInfluenceRangePx = EditorGUILayout.IntSlider(
            "Influence Range (px)",
            comp.coastalInfluenceRangePx,
            1, 256
        );

        comp.globalCoastalGyreStrength = EditorGUILayout.Slider(
            "Gyre Strength",
            comp.globalCoastalGyreStrength,
            0f, 1f
        );

        comp.minBasinPixelCount = EditorGUILayout.IntField(
            "Min Basin Size (px)",
            comp.minBasinPixelCount
        );

        comp.bigBasinPixelCount = EditorGUILayout.IntField(
            "Big Basin Size (px)",
            comp.bigBasinPixelCount
        );

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Generate Coastal Gyre Wind"))
        {
            comp.GenerateCoastalGyreWind();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture(
            "Refined Wind Preview (R=X, G=Y, B=speed)",
            comp.refinedPreviewTexture
        );

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "Pipeline:\n" +
            "1. Uses Terrain+seaLevel to find real water and shoreline distance.\n" +
            "2. Uses Voronoi+Plates to group oceanic regions into basins, but only merges\n" +
            "   regions with a wide shared border (minSharedBorderForMerge).\n" +
            "   This prevents giant snake oceans from becoming one basin.\n" +
            "3. Finds basin centroids and sizes.\n" +
            "4. Spins wind around basin centers. Strong near shore, fades offshore.\n\n" +
            "Output:\n" +
            "• refinedWindVector[y,x]\n" +
            "• refinedWindSpeed01[y,x]\n" +
            "Use this for humidity advection, rain shadows, etc.",
            MessageType.Info
        );
    }

    void DrawPreviewTexture(string label, Texture2D tex)
    {
        if (tex == null) return;

        GUILayout.Label(label, EditorStyles.miniBoldLabel);

        float maxWidth = 256f;
        float aspect = (tex.height == 0) ? 1f : ((float)tex.height / tex.width);
        Rect r = GUILayoutUtility.GetRect(maxWidth, maxWidth * aspect);
        EditorGUI.DrawPreviewTexture(r, tex, null, ScaleMode.ScaleToFit);
    }
}
