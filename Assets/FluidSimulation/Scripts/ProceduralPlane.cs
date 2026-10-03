using UnityEngine;

namespace Flexus.FluidSimulation
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ProceduralPlane : MonoBehaviour
    {
        [Header("Grid Resolution")]
        [SerializeField] private bool _autoSegmentsFromDensity = true;
        [Range(1f, 30f)]
        [SerializeField] private float _segmentsPerMeter = 10f;
        [Range(2, 250)]
        [SerializeField] private int _segmentsX = 100;
        [Range(2, 250)]
        [SerializeField] private int _segmentsZ = 100;

        [Header("Plane Dimensions")]
        [SerializeField] private Vector2 _size = new Vector2(10f, 10f);

        [Header("Displacement Culling Bounds")]
        [SerializeField] private float _verticalBoundsPadding = 8.0f;

        private MeshFilter _meshFilter;
        private Mesh _mesh;

        public Vector2 Size => _size;
        public int SegmentsX => _segmentsX;
        public int SegmentsZ => _segmentsZ;
        public float SegmentsPerMeter => _segmentsPerMeter;
        public bool AutoSegmentsFromDensity => _autoSegmentsFromDensity;
        public float VerticalBoundsPadding
        {
            get => _verticalBoundsPadding;
            set
            {
                _verticalBoundsPadding = Mathf.Max(0f, value);
                if (_mesh != null)
                {
                    ApplyDisplacementBoundsPadding(_mesh, _verticalBoundsPadding);
                }
            }
        }

        private void Awake()
        {
            EnsureMesh();
        }

        private void OnValidate()
        {
            EnsureMesh();
        }

        public void SetDimensions(Vector2 size, bool regenerate = true)
        {
            _size = new Vector2(Mathf.Max(0.5f, size.x), Mathf.Max(0.5f, size.y));
            if (_autoSegmentsFromDensity)
            {
                _segmentsX = Mathf.Clamp(Mathf.RoundToInt(_size.x * _segmentsPerMeter), 2, 250);
                _segmentsZ = Mathf.Clamp(Mathf.RoundToInt(_size.y * _segmentsPerMeter), 2, 250);
            }

            if (regenerate)
            {
                EnsureMesh();
            }
        }

        [ContextMenu("Regenerate Mesh")]
        public void EnsureMesh()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            if (_autoSegmentsFromDensity)
            {
                _segmentsX = Mathf.Clamp(Mathf.RoundToInt(_size.x * _segmentsPerMeter), 2, 250);
                _segmentsZ = Mathf.Clamp(Mathf.RoundToInt(_size.y * _segmentsPerMeter), 2, 250);
            }

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = $"ProceduralPlane_{_segmentsX}x{_segmentsZ}"
                };
                _meshFilter.sharedMesh = _mesh;
            }

            GeneratePlaneMesh(_mesh, _segmentsX, _segmentsZ, _size, _verticalBoundsPadding);
        }

        public static Mesh GeneratePlaneMesh(Mesh mesh, int segmentsX, int segmentsZ, Vector2 size, float verticalBoundsPadding = 8.0f)
        {
            if (mesh == null)
            {
                mesh = new Mesh();
            }

            mesh.Clear();

            int vertexCountX = segmentsX + 1;
            int vertexCountZ = segmentsZ + 1;
            int totalVertices = vertexCountX * vertexCountZ;

            Vector3[] vertices = new Vector3[totalVertices];
            Vector3[] normals = new Vector3[totalVertices];
            Vector4[] tangents = new Vector4[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];

            float stepX = size.x / segmentsX;
            float stepZ = size.y / segmentsZ;
            float halfWidth = size.x * 0.5f;
            float halfDepth = size.y * 0.5f;

            for (int z = 0; z < vertexCountZ; z++)
            {
                for (int x = 0; x < vertexCountX; x++)
                {
                    int index = z * vertexCountX + x;

                    float posX = x * stepX - halfWidth;
                    float posZ = z * stepZ - halfDepth;

                    vertices[index] = new Vector3(posX, 0f, posZ);
                    normals[index] = Vector3.up;
                    tangents[index] = new Vector4(1f, 0f, 0f, 1f);
                    uvs[index] = new Vector2((float)x / segmentsX, (float)z / segmentsZ);
                }
            }

            int totalQuads = segmentsX * segmentsZ;
            int[] triangles = new int[totalQuads * 6];
            int triIndex = 0;

            for (int z = 0; z < segmentsZ; z++)
            {
                for (int x = 0; x < segmentsX; x++)
                {
                    int bottomLeft = z * vertexCountX + x;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertexCountX;
                    int topRight = topLeft + 1;

                    triangles[triIndex++] = bottomLeft;
                    triangles[triIndex++] = topLeft;
                    triangles[triIndex++] = bottomRight;

                    triangles[triIndex++] = bottomRight;
                    triangles[triIndex++] = topLeft;
                    triangles[triIndex++] = topRight;
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            ApplyDisplacementBoundsPadding(mesh, verticalBoundsPadding);

            return mesh;
        }

        // Expands bounding box along Y to prevent culling during wave displacement
        public static void ApplyDisplacementBoundsPadding(Mesh mesh, float verticalPadding)
        {
            if (mesh == null) return;
            Bounds b = mesh.bounds;
            float targetHeight = Mathf.Max(b.size.y, verticalPadding * 2f);
            b.size = new Vector3(b.size.x, targetHeight, b.size.z);
            mesh.bounds = b;
        }
    }
}
