#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flexus.FluidSimulation.Editor
{
    public static class PlaneAssetGenerator
    {
        [MenuItem("Flexus/Bake Procedural Plane Mesh Asset")]
        public static void CreatePlaneAsset()
        {
            ProceduralPlane plane = Selection.activeGameObject != null 
                ? Selection.activeGameObject.GetComponent<ProceduralPlane>() 
                : Object.FindFirstObjectByType<ProceduralPlane>();

            Vector2 size = plane != null ? plane.Size : new Vector2(10f, 10f);
            int segX = plane != null ? plane.SegmentsX : 100;
            int segZ = plane != null ? plane.SegmentsZ : 100;

            const string directory = "Assets/FluidSimulation/Materials";
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            string assetPath = $"{directory}/PlaneMesh_{segX}x{segZ}.asset";

            Mesh mesh = new Mesh
            {
                name = $"PlaneMesh_{segX}x{segZ}"
            };

            ProceduralPlane.GeneratePlaneMesh(mesh, segX, segZ, size);

            AssetDatabase.CreateAsset(mesh, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = mesh;
            Debug.Log($"<color=cyan>[Flexus]</color> Successfully baked plane mesh asset at {assetPath} ({segX}x{segZ} quads, {size.x:F1}x{size.y:F1}m, {mesh.vertexCount} vertices).");
        }
    }
}
#endif
