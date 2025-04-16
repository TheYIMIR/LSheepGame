using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArenaBoundaryVisualizer : MonoBehaviour
{
    [Header("Boundary Settings")]
    [Tooltip("Reference to the GameManager to get arena size")]
    public GameManager gameManager;

    [Tooltip("Height of the boundary walls")]
    public float boundaryHeight = 2.0f;

    [Tooltip("Thickness of the boundary walls")]
    public float boundaryThickness = 0.2f;

    [Tooltip("Material to use for the boundaries")]
    public Material boundaryMaterial;

    [Header("Visual Options")]
    [Tooltip("Color of the boundary walls")]
    public Color boundaryColor = new Color(1f, 0.5f, 0f, 0.6f); // Semi-transparent orange

    [Tooltip("Add pulsing effect to boundaries")]
    public bool pulseEffect = true;

    [Tooltip("Speed of the pulse effect")]
    public float pulseSpeed = 1.0f;

    [Tooltip("Intensity of the pulse effect")]
    [Range(0.0f, 1.0f)]
    public float pulseIntensity = 0.2f;

    // Private variables
    private GameObject[] boundaryWalls = new GameObject[4];
    private Material instantiatedMaterial;
    private Color originalColor;

    void Start()
    {
        if (gameManager == null)
        {
            gameManager = FindObjectOfType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("Cannot find GameManager. Please assign it in the inspector.");
                return;
            }
        }

        // Create a new material instance to avoid affecting other objects using the same material
        if (boundaryMaterial != null)
        {
            instantiatedMaterial = new Material(boundaryMaterial);
            instantiatedMaterial.color = boundaryColor;
            originalColor = boundaryColor;
        }
        else
        {
            // Create a default transparent material if none provided
            instantiatedMaterial = new Material(Shader.Find("Standard"));
            instantiatedMaterial.SetFloat("_Mode", 3); // Transparent mode
            instantiatedMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            instantiatedMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            instantiatedMaterial.SetInt("_ZWrite", 0);
            instantiatedMaterial.DisableKeyword("_ALPHATEST_ON");
            instantiatedMaterial.EnableKeyword("_ALPHABLEND_ON");
            instantiatedMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            instantiatedMaterial.renderQueue = 3000;
            instantiatedMaterial.color = boundaryColor;
            originalColor = boundaryColor;
        }

        // Create the boundary walls
        CreateBoundaryWalls();
    }

    void Update()
    {
        if (pulseEffect && instantiatedMaterial != null)
        {
            // Calculate pulse value
            float pulse = Mathf.Sin(Time.time * pulseSpeed) * pulseIntensity;

            // Apply pulse to color
            Color pulseColor = originalColor;
            pulseColor.a = originalColor.a * (1f + pulse);
            instantiatedMaterial.color = pulseColor;
        }
    }

    void CreateBoundaryWalls()
    {
        Vector3 arenaSize = gameManager.GetArenaSize();
        Vector3 arenaCenter = gameManager.GetArenaCenter();

        float halfWidth = arenaSize.x / 2;
        float halfLength = arenaSize.z / 2;

        // Create four walls
        // North wall
        CreateWall(0, new Vector3(0, 0, halfLength), new Vector3(arenaSize.x, boundaryHeight, boundaryThickness));

        // South wall
        CreateWall(1, new Vector3(0, 0, -halfLength), new Vector3(arenaSize.x, boundaryHeight, boundaryThickness));

        // East wall
        CreateWall(2, new Vector3(halfWidth, 0, 0), new Vector3(boundaryThickness, boundaryHeight, arenaSize.z));

        // West wall
        CreateWall(3, new Vector3(-halfWidth, 0, 0), new Vector3(boundaryThickness, boundaryHeight, arenaSize.z));

        // Position everything relative to arena center
        transform.position = arenaCenter;
    }

    void CreateWall(int index, Vector3 localPosition, Vector3 size)
    {
        // Create a new game object for the wall
        boundaryWalls[index] = new GameObject("BoundaryWall_" + index);
        boundaryWalls[index].transform.parent = transform;
        boundaryWalls[index].transform.localPosition = localPosition;

        // Add mesh components
        MeshFilter meshFilter = boundaryWalls[index].AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = boundaryWalls[index].AddComponent<MeshRenderer>();

        // Create a cube mesh
        meshFilter.mesh = CreateCubeMesh(size);

        // Apply material
        meshRenderer.material = instantiatedMaterial;
    }

    Mesh CreateCubeMesh(Vector3 size)
    {
        Mesh mesh = new Mesh();

        float width = size.x / 2;
        float height = size.y / 2;
        float depth = size.z / 2;

        // Define vertices (8 corners of a cube)
        Vector3[] vertices = new Vector3[8]
        {
            new Vector3(-width, -height, -depth),
            new Vector3(width, -height, -depth),
            new Vector3(width, height, -depth),
            new Vector3(-width, height, -depth),
            new Vector3(-width, -height, depth),
            new Vector3(width, -height, depth),
            new Vector3(width, height, depth),
            new Vector3(-width, height, depth)
        };

        // Define triangles (6 faces, 2 triangles each = 12 triangles, 3 vertices each = 36 indices)
        int[] triangles = new int[36]
        {
            // Front face
            0, 2, 1, 0, 3, 2,
            // Back face
            4, 5, 6, 4, 6, 7,
            // Top face
            3, 7, 6, 3, 6, 2,
            // Bottom face
            0, 1, 5, 0, 5, 4,
            // Left face
            0, 4, 7, 0, 7, 3,
            // Right face
            1, 2, 6, 1, 6, 5
        };

        // Define UVs
        Vector2[] uvs = new Vector2[8]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1),
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };

        // Assign values to the mesh
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;

        // Recalculate normals and bounds
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    // Optional: make the visualizer adjustable when arena size changes
    public void UpdateBoundaries()
    {
        // Clear existing walls
        foreach (GameObject wall in boundaryWalls)
        {
            if (wall != null)
                Destroy(wall);
        }

        // Create new walls
        CreateBoundaryWalls();
    }
}